import { describe, expect, it, vi } from 'vitest';
import { searchCachePath } from '../src/notebook/search.ts';
import { Notebook, sessionPath } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { noteFileName } from '../src/notebook/tree.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import {
  ConflictError,
  type Entry,
  type FileData,
  type WriteOptions,
} from '../src/platform/notebookStore.ts';

/** A MemoryStore that can hold writes and reads back, to force the order async work lands in. */
class GatedStore extends MemoryStore {
  holdWrites = false;
  holdReads = false;
  holdLists = false;
  readonly lists: (() => void)[] = [];
  readonly writes: { path: string; release: () => void }[] = [];
  readonly reads: (() => void)[] = [];

  override write(path: string, bytes: Uint8Array, opts?: WriteOptions): Promise<string | null> {
    if (!this.holdWrites) return super.write(path, bytes, opts);
    return new Promise((resolve, reject) =>
      this.writes.push({ path, release: () => void super.write(path, bytes, opts).then(resolve, reject) }),
    );
  }

  override list(dir: string): Promise<Entry[]> {
    if (!this.holdLists) return super.list(dir);
    // The listing sees the disk as it is now; only its answer is held back.
    const now = super.list(dir);
    return new Promise((resolve, reject) => this.lists.push(() => void now.then(resolve, reject)));
  }

  override read(path: string): Promise<FileData> {
    if (!this.holdReads) return super.read(path);
    // The read sees the disk as it is now; only its answer is held back.
    const now = super.read(path);
    return new Promise((resolve, reject) => this.reads.push(() => void now.then(resolve, reject)));
  }
}

/** A store whose move copies and deletes, giving the file a new version (like the browser store). */
class CopyingStore extends MemoryStore {
  override async move(from: string, to: string): Promise<void> {
    const data = await this.read(from);
    await this.write(to, data.bytes, { force: true });
    await this.remove(from);
  }
}

const text = async (store: MemoryStore, path: string) =>
  new TextDecoder().decode((await store.read(path)).bytes);
const settle = async () => {
  for (let i = 0; i < 10; i++) await new Promise((r) => setTimeout(r, 0));
};

async function openWith<S extends MemoryStore>(store: S, prefs = {}) {
  const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false, ...prefs }) });
  return nb;
}

describe('Notebook saves and refreshes in flight', () => {
  it('runs a second save after the first, against the version it got back', async () => {
    const store = new GatedStore('N', { 'a.md': 'hello' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.select(5);
    tab.state!.insert(' one');
    store.holdWrites = true;
    const first = nb.save(tab);
    await settle();
    tab.state!.insert(' two');
    const second = nb.save(tab);
    await settle();
    expect(store.writes).toHaveLength(1); // the second waits for the first
    store.writes.shift()!.release();
    await settle();
    expect(store.writes).toHaveLength(1);
    store.writes.shift()!.release();
    expect(await first).toBe(true);
    expect(await second).toBe(true);
    expect(tab.conflict.value).toBe('none');
    expect(tab.dirty.value).toBe(false);
    expect(await text(store, 'a.md')).toBe('hello one two');
  });

  it('coalesces saves asked for while one waits', async () => {
    const store = new GatedStore('N', { 'a.md': '' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.insert('a');
    store.holdWrites = true;
    const first = nb.save(tab);
    await settle();
    tab.state!.insert('b');
    const second = nb.save(tab);
    tab.state!.insert('c');
    const third = nb.save(tab);
    expect(third).toBe(second);
    store.writes.shift()!.release();
    await settle();
    store.writes.shift()!.release();
    await Promise.all([first, second]);
    expect(store.writes).toHaveLength(0);
    expect(await text(store, 'a.md')).toBe('abc');
  });

  it('does not write a tab without unsaved edits unless forced', async () => {
    const store = new MemoryStore('N', { 'a.md': 'x' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('a.md'))!;
    const before = tab.version;
    expect(await nb.save(tab)).toBe(true);
    expect(tab.version).toBe(before);
    expect(await nb.save(tab, true)).toBe(true);
    expect(tab.version).not.toBe(before);
  });

  it('drops a refresh that read the disk before a save landed', async () => {
    const store = new GatedStore('N', { 'a.md': 'hello' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.select(5);
    tab.state!.insert(' world');
    store.holdReads = true;
    const refreshing = nb.refresh();
    await settle();
    store.holdReads = false;
    expect(store.reads).toHaveLength(1); // read 'hello' at the old version
    expect(await nb.save(tab)).toBe(true);
    for (const r of store.reads) r();
    await refreshing;
    expect(tab.state!.text).toBe('hello world');
    expect(tab.dirty.value).toBe(false);
    expect(tab.conflict.value).toBe('none');
    tab.state!.insert('!');
    expect(await nb.save(tab)).toBe(true);
    expect(await text(store, 'a.md')).toBe('hello world!');
  });

  it('drops a Reload from Disk that read the disk before a save landed', async () => {
    const store = new GatedStore('N', { 'a.md': 'hello' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.select(5);
    tab.state!.insert(' world');
    store.holdReads = true;
    const reloading = nb.reload(tab);
    await settle();
    store.holdReads = false;
    expect(store.reads).toHaveLength(1); // read 'hello' at the old version
    expect(await nb.save(tab)).toBe(true);
    for (const r of store.reads) r();
    await reloading;
    expect(tab.state!.text).toBe('hello world');
    expect(tab.dirty.value).toBe(false);
    tab.state!.insert('!');
    expect(await nb.save(tab)).toBe(true);
    expect(await text(store, 'a.md')).toBe('hello world!');
  });

  it("takes the encoding of the disk's version with its text, and saves in it", async () => {
    const store = new MemoryStore('N', { 'a.md': 'hello', 'b.md': 'same' });
    const nb = await openWith(store);
    const a = (await nb.openFile('a.md'))!;
    const b = (await nb.openFile('b.md'))!;
    const bom = (t: string) => new Uint8Array([0xef, 0xbb, 0xbf, ...new TextEncoder().encode(t)]);
    // Rewritten outside with a byte-order mark: a with new text, b with the same text.
    await store.write('a.md', bom('theirs'), { force: true });
    await store.write('b.md', bom('same'), { force: true });
    await nb.refresh();
    for (const tab of [a, b]) {
      expect(tab.format.bom).toBe(true);
      tab.state!.select(tab.state!.document.length);
      tab.state!.insert('!');
      expect(await nb.save(tab)).toBe(true);
    }
    expect((await store.read('a.md')).bytes).toEqual(bom('theirs!'));
    expect((await store.read('b.md')).bytes).toEqual(bom('same!'));
  });

  it('keeps the encoding a save wrote, when a reload lands while it is in flight', async () => {
    const store = new GatedStore('N', { 'a.md': 'hello' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.insert('mine ');
    await store.write('a.md', new Uint8Array([0xef, 0xbb, 0xbf, 0x74]), { force: true });
    await nb.refresh();
    expect(tab.conflict.value).toBe('changed');
    store.holdWrites = true;
    const keeping = nb.save(tab, true); // Keep My Version, in the tab's encoding: no byte-order mark
    await settle();
    await nb.reload(tab); // Reload from Disk takes the file's mark
    expect(tab.format.bom).toBe(true);
    store.writes.shift()!.release();
    expect(await keeping).toBe(true);
    // The disk now holds the write, without a mark, and the tab knows it.
    expect((await store.read('a.md')).bytes[0]).toBe(0x6d);
    expect(tab.format.bom).toBe(false);
  });

  it('skips a tab whose save is in flight', async () => {
    const store = new GatedStore('N', { 'a.md': 'hello' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.select(5);
    tab.state!.insert('!');
    store.holdWrites = true;
    const saving = nb.save(tab);
    await settle();
    await nb.refresh();
    expect(tab.conflict.value).toBe('none');
    store.writes.shift()!.release();
    expect(await saving).toBe(true);
    expect(tab.state!.text).toBe('hello!');
  });

  it('clears the deleted flag when the file comes back with the same text', async () => {
    const store = new MemoryStore('N', { 'a.md': 'hello' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('a.md'))!;
    store.touch('a.md', null);
    await nb.refresh();
    expect(tab.conflict.value).toBe('deleted');
    store.touch('a.md', 'hello');
    await nb.refresh();
    expect(tab.conflict.value).toBe('none');
    tab.state!.insert('x');
    expect(await nb.save(tab)).toBe(true);
  });

  it('flags a note deleted outside the app instead of recreating it on save', async () => {
    const store = new MemoryStore('N', { 'a.md': 'hello' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.insert('x');
    store.touch('a.md', null);
    expect(await nb.save(tab)).toBe(false);
    expect(tab.conflict.value).toBe('deleted');
    await expect(store.read('a.md')).rejects.toThrow();
    // Saving again on purpose recreates it (PRD §5).
    expect(await nb.save(tab, true)).toBe(true);
    expect(await text(store, 'a.md')).toBe('xhello');
  });

  it('tells deleted from changed when the store only says "changed"', async () => {
    class NasLike extends MemoryStore {
      override write(path: string, bytes: Uint8Array, opts?: WriteOptions): Promise<string | null> {
        return super.write(path, bytes, opts).catch((e: unknown) => {
          if (e instanceof ConflictError && e.reason === 'deleted')
            throw new ConflictError('changed', 'Precondition failed');
          throw e;
        });
      }
    }
    const store = new NasLike('N', { 'a.md': 'hello' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.insert('x');
    store.touch('a.md', null);
    expect(await nb.save(tab)).toBe(false);
    expect(tab.conflict.value).toBe('deleted');
  });
});

describe('Notebook moves, names, settings and session', () => {
  it('takes the new version of a moved file, so the next save is no conflict', async () => {
    const store = new CopyingStore('N', { 'f/a.md': 'hello' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('f/a.md'))!;
    expect(await nb.rename('f/a.md', 'b.md')).toBe('f/b.md');
    expect(await nb.move('f', '')).toBeNull(); // already at the top
    tab.state!.select(5);
    tab.state!.insert('!');
    expect(await nb.save(tab)).toBe(true);
    expect(tab.conflict.value).toBe('none');
    expect(await text(store, 'f/b.md')).toBe('hello!');
  });

  it('still conflicts when the file changed on disk before the move', async () => {
    const store = new CopyingStore('N', { 'a.md': 'hello' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('a.md'))!;
    store.touch('a.md', 'theirs');
    await nb.rename('a.md', 'b.md');
    tab.state!.insert('mine ');
    expect(await nb.save(tab)).toBe(false);
    expect(tab.conflict.value).toBe('changed');
  });

  it('adds .md unless the name ends in a note extension', async () => {
    expect(noteFileName('Meeting 2024.11')).toBe('Meeting 2024.11.md');
    expect(noteFileName('v1.2')).toBe('v1.2.md');
    expect(noteFileName('Ideas')).toBe('Ideas.md');
    expect(noteFileName('todo.txt')).toBe('todo.txt');
    expect(noteFileName('Notes.MARKDOWN')).toBe('Notes.MARKDOWN');
    expect(noteFileName('photo.png')).toBe('photo.png.md');
    const downloads: string[] = [];
    const store = new MemoryStore('N', {});
    const nb = await Notebook.open(store, {
      prefs: memoryPrefs({ autoSave: false }),
      download: (n) => downloads.push(n),
    });
    expect(await nb.createNote('', 'Meeting 2024.11')).toBe('Meeting 2024.11.md');
    expect(nb.active.value?.path.value).toBe('Meeting 2024.11.md');
    expect(downloads).toEqual([]);
  });

  it('shows hidden files as soon as the preference or the override changes', async () => {
    const store = new MemoryStore('N', { '.hidden.md': 'x', 'a.md': '' });
    const nb = await openWith(store);
    const names = () => nb.tree.value.children.map((c) => c.name);
    expect(names()).toEqual(['a.md']);
    nb.config.setPreferences({ showHiddenFiles: true });
    await vi.waitFor(() => expect(names()).toEqual(['.hidden.md', 'a.md']));
    await nb.config.setOverrides({ showHiddenFiles: false });
    await vi.waitFor(() => expect(names()).toEqual(['a.md']));
  });

  it('keeps the tree object when a refresh finds the same entries', async () => {
    const store = new MemoryStore('N', { 'a.md': '', 'f/b.md': '' });
    const nb = await openWith(store);
    const before = nb.tree.value;
    await nb.refresh();
    expect(nb.tree.value).toBe(before);
    store.touch('c.md', '');
    await nb.refresh();
    expect(nb.tree.value).not.toBe(before);
  });

  it('drops a tree read that started before a newer one', async () => {
    const store = new GatedStore('N', { 'a.md': '', 'f/b.md': '' });
    const nb = await openWith(store);
    store.holdLists = true;
    const slow = nb.refresh();
    await settle();
    store.holdLists = false;
    expect(await nb.rename('a.md', 'c.md')).toBe('c.md');
    expect(nb.tree.value.children.map((c) => c.name)).toEqual(['f', 'c.md']);
    for (const release of store.lists.splice(0)) release();
    await slow;
    expect(nb.tree.value.children.map((c) => c.name)).toEqual(['f', 'c.md']);
  });

  it('answers an aborted search at once, while the index keeps catching up for the next one', async () => {
    // An empty saved index, so the only reads held are the notes'.
    const store = new GatedStore('N', {
      'a.md': 'x',
      'b.md': 'x',
      'c.md': 'x',
      [searchCachePath]: '{"format": 1, "notes": []}',
    });
    const nb = await openWith(store);
    store.holdReads = true;
    const abort = new AbortController();
    const searching = nb.textSearch.find('x', { signal: abort.signal });
    await settle();
    abort.abort();
    expect(await searching).toEqual([]);
    store.holdReads = false;
    while (store.reads.length > 0) {
      store.reads.shift()!();
      await settle();
    }
    const read = vi.spyOn(store, 'read');
    expect(new Set((await nb.textSearch.find('x')).map((h) => h.path))).toEqual(
      new Set(['a.md', 'b.md', 'c.md']),
    );
    expect(read.mock.calls.filter(([p]) => p.endsWith('.md'))).toEqual([]);
  });

  it('writes a pending session on dispose, and session writes land in order', async () => {
    vi.useFakeTimers();
    try {
      const store = new MemoryStore('N', { 'a.md': '', 'b.md': '' });
      const nb = await openWith(store);
      await nb.openFile('a.md');
      nb.panel.value = 'outline';
      nb.dispose();
      await vi.runAllTimersAsync();
      const saved = JSON.parse(await text(store, sessionPath)) as {
        tabs: { path: string }[];
        sidebar: { panel: string };
      };
      expect(saved.tabs.map((t) => t.path)).toEqual(['a.md']);
      expect(saved.sidebar.panel).toBe('outline');
    } finally {
      vi.useRealTimers();
    }

    const store = new GatedStore('N', { 'a.md': '' });
    const nb = await openWith(store);
    store.holdWrites = true;
    nb.panel.value = 'search';
    const first = nb.persist();
    nb.panel.value = 'recent';
    const second = nb.persist();
    await settle();
    // One write is out at a time. Releasing the newest first would land the older session last if they
    // weren't serialized.
    for (let i = 0; i < 10 && store.writes.length; i++) {
      expect(store.writes).toHaveLength(1);
      store.writes.pop()!.release();
      await settle();
    }
    await Promise.all([first, second]);
    expect((JSON.parse(await text(store, sessionPath)) as { sidebar: { panel: string } }).sidebar.panel).toBe(
      'recent',
    );
    nb.dispose();
  });
});

describe('Notebook opens in flight', () => {
  it('opens a note once when it is asked for twice while the read is out', async () => {
    const store = new GatedStore('N', { 'a.md': 'a' });
    const nb = await openWith(store);
    store.holdReads = true;
    const first = nb.openFile('a.md');
    const second = nb.openFile('a.md');
    await settle();
    for (const r of store.reads.splice(0)) r();
    expect(await first).toBe(await second);
    expect(nb.tabs.value).toHaveLength(1);
  });

  it('keeps the last asked-for note in front when reads come back out of order', async () => {
    const store = new GatedStore('N', { 'a.md': 'a', 'b.md': 'b' });
    const nb = await openWith(store);
    store.holdReads = true;
    const a = nb.openFile('a.md');
    await settle();
    const b = nb.openFile('b.md');
    await settle();
    const [readA, readB] = store.reads.splice(0);
    readB!();
    await b;
    readA!();
    await a;
    expect(nb.active.value?.path.value).toBe('b.md');
  });

  it('leaves a tab picked while the read was out in front', async () => {
    const store = new GatedStore('N', { 'a.md': 'a', 'b.md': 'b' });
    const nb = await openWith(store);
    const tabA = (await nb.openFile('a.md'))!;
    store.holdReads = true;
    const b = nb.openFile('b.md');
    await settle();
    nb.activate(tabA);
    for (const r of store.reads.splice(0)) r();
    await b;
    expect(nb.active.value).toBe(tabA);
    expect(nb.tabs.value).toHaveLength(2);
  });
});

describe('Notebook moves and saves in flight', () => {
  class SlowMoves extends GatedStore {
    holdMoves = false;
    readonly moves: (() => void)[] = [];
    /** Copies, then (held when `holdMoves`) deletes the original, as the browser store does. */
    override async move(from: string, to: string): Promise<void> {
      const data = await MemoryStore.prototype.read.call(this, from);
      await MemoryStore.prototype.write.call(this, to, data.bytes, { force: true });
      if (this.holdMoves) await new Promise<void>((r) => this.moves.push(r));
      await this.remove(from);
    }
  }

  it('waits for a save in flight before moving the note', async () => {
    const store = new SlowMoves('N', { 'a.md': 'hello', 'sub/x.md': '' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.select(5);
    tab.state!.insert(' world');
    store.holdWrites = true;
    const saving = nb.save(tab);
    await settle();
    const moving = nb.move('a.md', 'sub');
    await settle();
    expect(store.writes).toHaveLength(1);
    store.holdWrites = false;
    store.writes.shift()!.release();
    expect(await saving).toBe(true);
    expect(await moving).toBe('sub/a.md');
    expect(await text(store, 'sub/a.md')).toBe('hello world');
    await expect(store.read('a.md')).rejects.toThrow();
    expect(tab.path.value).toBe('sub/a.md');
    expect(tab.conflict.value).toBe('none');
  });

  it('holds a save asked for during a move, then writes to the new place', async () => {
    const store = new SlowMoves('N', { 'a.md': 'hello', 'sub/x.md': '' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('a.md'))!;
    store.holdMoves = true;
    const moving = nb.move('a.md', 'sub');
    await settle();
    tab.state!.select(5);
    tab.state!.insert('!');
    const saving = nb.save(tab);
    await settle();
    store.holdMoves = false;
    store.moves.shift()!();
    expect(await moving).toBe('sub/a.md');
    expect(await saving).toBe(true);
    expect(await text(store, 'sub/a.md')).toBe('hello!');
    await expect(store.read('a.md')).rejects.toThrow();
  });
});

/** A MemoryStore that counts the folders it lists and the files it reads. */
class CountingStore extends MemoryStore {
  lists = 0;
  readonly readPaths: string[] = [];

  override list(dir: string): Promise<Entry[]> {
    this.lists++;
    return super.list(dir);
  }

  override read(path: string): Promise<FileData> {
    this.readPaths.push(path);
    return super.read(path);
  }
}

describe('Notebook refreshes (PRD §5)', () => {
  it('leaves changes inside .git/ alone', async () => {
    const store = new CountingStore('N', { 'a.md': 'a', '.git/index': 'i' });
    const nb = await openWith(store);
    store.lists = 0;
    store.touch('.git/index', 'i2');
    store.touch('.git/objects/ab/cdef', 'o');
    await new Promise((r) => setTimeout(r, 300));
    expect(store.lists).toBe(0);
    store.touch('b.md', 'b');
    await vi.waitFor(() => expect(store.lists).toBeGreaterThan(0));
    nb.dispose();
  });

  it('reads an open note again only when the listing says it changed', async () => {
    const store = new CountingStore('N', { 'a.md': 'a', 'b.md': 'b' });
    const nb = await openWith(store);
    const a = (await nb.openFile('a.md'))!;
    await nb.openFile('b.md');
    await nb.refresh();
    store.readPaths.length = 0;
    await nb.refresh();
    expect(store.readPaths.filter((p) => p.endsWith('.md'))).toEqual([]);
    store.touch('a.md', 'a changed');
    await nb.refresh();
    expect(store.readPaths.filter((p) => p.endsWith('.md'))).toEqual(['a.md']);
    expect(a.state!.text).toBe('a changed');
    nb.dispose();
  });
});

describe('Notebook after it closes (PRD §5)', () => {
  it('writes no queued save once disposed', async () => {
    const store = new GatedStore('N', { 'a.md': 'one' });
    const nb = await openWith(store);
    const tab = (await nb.openFile('a.md'))!;
    store.holdWrites = true;
    tab.state!.insert('A');
    const first = nb.save(tab);
    await settle();
    tab.state!.insert('B');
    const second = nb.save(tab);
    nb.dispose(); // switched notebooks and chose Don't Save
    store.writes.shift()!.release();
    await first;
    await settle();
    expect(store.writes.filter((w) => w.path === 'a.md')).toHaveLength(0);
    expect(await second).toBe(false);
    expect(await text(store, 'a.md')).toBe('Aone');
  });

  it('rewrites no picture links in closed notes once disposed', async () => {
    const store = new GatedStore('N', { 'a.md': '![](pic.png)', 'pic.png': '', 'sub/x.md': '' });
    const nb = await openWith(store);
    store.holdReads = true;
    const moving = nb.move('pic.png', 'sub');
    await vi.waitFor(() => expect(store.reads.length).toBeGreaterThan(0));
    nb.dispose();
    store.holdReads = false;
    for (const r of store.reads.splice(0)) r();
    await moving;
    await settle();
    expect(await text(store, 'a.md')).toBe('![](pic.png)');
  });
});
