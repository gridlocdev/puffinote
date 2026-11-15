import { describe, expect, it, vi } from 'vitest';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import { Notebook, sessionPath } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { filterTree, visibleRows } from '../src/notebook/tree.ts';
import { outlineOf } from '@puffinote/markdown';

const text = async (store: MemoryStore, path: string) =>
  new TextDecoder().decode((await store.read(path)).bytes);

async function open(files: Record<string, string>, prefs = {}) {
  const store = new MemoryStore('Notes', files);
  const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false, ...prefs }) });
  return { store, nb };
}

describe('Notebook tree', () => {
  it('sorts folders first, hides the state folder and dot-files', async () => {
    const { nb } = await open({
      'b.md': '',
      'a/x.md': '',
      '.puffinote/settings.json': '{}',
      '.hidden.md': '',
      'img.png': '',
    });
    expect(nb.tree.value.children.map((c) => c.name)).toEqual(['a', 'b.md', 'img.png']);
    expect(visibleRows(nb.tree.value, new Set()).map((r) => r.node.path)).toEqual(['a', 'b.md', 'img.png']);
    expect(visibleRows(nb.tree.value, new Set(['a'])).map((r) => r.node.path)).toEqual([
      'a',
      'a/x.md',
      'b.md',
      'img.png',
    ]);
  });

  it('filters by name, keeping ancestors and opening them', async () => {
    const { nb } = await open({ 'work/plans/q3.md': '', 'work/notes.md': '', 'home.md': '', 'plans.md': '' });
    const r = filterTree(nb.tree.value, 'PLAN');
    expect(r.matches).toBe(2); // the plans folder and plans.md
    expect([...r.opened]).toEqual(['work']);
    expect(visibleRows(r.tree, r.opened).map((x) => x.node.path)).toEqual(['work', 'work/plans', 'plans.md']);
  });
});

describe('Notebook tabs and saving', () => {
  it('opens a note once and saves edits', async () => {
    const { store, nb } = await open({ 'a.md': 'hello' });
    const tab = (await nb.openFile('a.md'))!;
    expect(await nb.openFile('a.md')).toBe(tab);
    expect(nb.tabs.value).toHaveLength(1);
    tab.state!.select(5);
    tab.state!.insert(' world');
    expect(tab.dirty.value).toBe(true);
    expect(await nb.save(tab)).toBe(true);
    expect(tab.dirty.value).toBe(false);
    expect(tab.saved.value).toBe(true);
    expect(await text(store, 'a.md')).toBe('hello world');
    expect(nb.recent.value).toEqual(['a.md']);
  });

  it('flags a conflict when the file changed on disk under unsaved edits', async () => {
    const { store, nb } = await open({ 'a.md': 'one' });
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.select(3);
    tab.state!.insert('!');
    store.touch('a.md', 'two');
    expect(await nb.save(tab)).toBe(false);
    expect(tab.conflict.value).toBe('changed');
    expect(await text(store, 'a.md')).toBe('two');
    expect(await nb.save(tab, true)).toBe(true);
    expect(await text(store, 'a.md')).toBe('one!');
    expect(tab.conflict.value).toBe('none');
  });

  it('reloads a clean tab on refresh as an undoable step', async () => {
    const { store, nb } = await open({ 'a.md': 'one' });
    const tab = (await nb.openFile('a.md'))!;
    store.touch('a.md', 'one two');
    await nb.refresh();
    expect(tab.state!.text).toBe('one two');
    expect(tab.dirty.value).toBe(false);
    tab.state!.undo();
    expect(tab.state!.text).toBe('one');
  });

  it('marks a dirty tab changed and a removed file deleted', async () => {
    const { store, nb } = await open({ 'a.md': 'one', 'b.md': 'bee' });
    const a = (await nb.openFile('a.md'))!;
    const b = (await nb.openFile('b.md'))!;
    a.state!.insert('x');
    store.touch('a.md', 'other');
    store.touch('b.md', null);
    await nb.refresh();
    expect(a.conflict.value).toBe('changed');
    expect(a.state!.text).toBe('xone');
    expect(b.conflict.value).toBe('deleted');
  });

  it('auto-saves a second after the last edit', async () => {
    vi.useFakeTimers();
    try {
      const { store, nb } = await open({ 'a.md': '' }, { autoSave: true });
      const tab = (await nb.openFile('a.md'))!;
      tab.state!.insert('a');
      await vi.advanceTimersByTimeAsync(500);
      tab.state!.insert('b');
      await vi.advanceTimersByTimeAsync(900);
      expect(await text(store, 'a.md')).toBe('');
      await vi.advanceTimersByTimeAsync(200);
      expect(await text(store, 'a.md')).toBe('ab');
      expect(tab.dirty.value).toBe(false);
    } finally {
      vi.useRealTimers();
    }
  });

  it('closes to the neighbouring tab and cycles', async () => {
    const { nb } = await open({ 'a.md': '', 'b.md': '', 'c.md': '' });
    const a = (await nb.openFile('a.md'))!;
    const b = (await nb.openFile('b.md'))!;
    const c = (await nb.openFile('c.md'))!;
    nb.activate(b);
    nb.closeTab(b);
    expect(nb.active.value).toBe(c);
    nb.cycleTab(1);
    expect(nb.active.value).toBe(a);
  });
});

describe('Notebook pinned tabs', () => {
  it('keeps pinned tabs first, in pin order, and remembers them', async () => {
    const { store, nb } = await open({ 'a.md': '', 'b.md': '', 'c.md': '', 'd.md': '' });
    const [a, b, c, d] = [
      (await nb.openFile('a.md'))!,
      (await nb.openFile('b.md'))!,
      (await nb.openFile('c.md'))!,
      (await nb.openFile('d.md'))!,
    ];
    const order = () => nb.tabs.value.map((t) => t.path.value);
    nb.setTabPinned(c, true);
    nb.setTabPinned(d, true);
    expect(order()).toEqual(['c.md', 'd.md', 'a.md', 'b.md']);
    // Unpinning puts the tab first among the unpinned; a move can't take a tab across the line.
    nb.setTabPinned(c, false);
    expect(order()).toEqual(['d.md', 'c.md', 'a.md', 'b.md']);
    nb.moveTab(b, 0);
    expect(order()).toEqual(['d.md', 'b.md', 'c.md', 'a.md']);
    expect([a, d].map((t) => t.pinned.value)).toEqual([false, true]);

    await nb.persist();
    const again = await Notebook.open(store, { prefs: memoryPrefs() });
    expect(again.tabs.value.map((t) => `${t.path.value}${t.pinned.value ? '*' : ''}`)).toEqual([
      'd.md*',
      'b.md',
      'c.md',
      'a.md',
    ]);
  });
});

describe('Notebook files', () => {
  it('creates notes with .md and unique folders', async () => {
    const { store, nb } = await open({ 'a.md': '' });
    expect(await nb.createNote('', 'Ideas')).toBe('Ideas.md');
    expect(nb.active.value?.path.value).toBe('Ideas.md');
    expect(await nb.createNote('', 'a')).toBeNull();
    expect(nb.notice.value?.kind).toBe('error');
    expect(await nb.createFolder('', 'Work')).toBe('Work');
    expect((await store.list('')).map((e) => e.name).sort()).toEqual(['Ideas.md', 'Work', 'a.md']);
  });

  it('moves and renames, carrying open tabs and recent along', async () => {
    const { nb } = await open({ 'f/a.md': '', 'g/x.md': '' });
    const tab = (await nb.openFile('f/a.md'))!;
    expect(nb.canMove('f', 'f')).toBe(false);
    expect(nb.canMove('f/a.md', 'f')).toBe(false);
    expect(await nb.move('f', 'g')).toBe('g/f');
    expect(tab.path.value).toBe('g/f/a.md');
    expect(nb.recent.value).toEqual(['g/f/a.md']);
    expect(await nb.rename('g/f/a.md', 'b.md')).toBe('g/f/b.md');
    expect(tab.path.value).toBe('g/f/b.md');
  });

  it('removes a folder and closes its tabs', async () => {
    const { nb } = await open({ 'f/a.md': '', 'b.md': '' });
    await nb.openFile('f/a.md');
    await nb.openFile('b.md');
    expect(await nb.remove('f')).toBe(true);
    expect(nb.tabs.value.map((t) => t.path.value)).toEqual(['b.md']);
    expect(nb.tree.value.children.map((c) => c.path)).toEqual(['b.md']);
  });

  it('searches notes, preferring unsaved text', async () => {
    const { nb } = await open({ 'a.md': 'alpha\nBeta gamma', 'b.md': 'beta', 'c.pdf': 'beta' });
    const tab = (await nb.openFile('b.md'))!;
    tab.state!.insert('xx ');
    const hits = await nb.textSearch.find('beta');
    expect(hits.map((h) => [h.path, h.line, h.preview.slice(h.matchStart, h.matchStart + h.length)])).toEqual(
      [
        ['a.md', 1, 'Beta'],
        ['b.md', 0, 'beta'],
      ],
    );
    expect(hits[1]!.offset).toBe(3);
  });

  it('finds note names first, then text', async () => {
    const { nb } = await open({ 'a.md': 'about plans', 'f/Plans.md': 'nothing here', 'plans.pdf': '' });
    const hits = await nb.textSearch.find('plan');
    expect(
      hits.map((h) => [h.path, !!h.inTitle, h.preview.slice(h.matchStart, h.matchStart + h.length)]),
    ).toEqual([
      ['f/Plans.md', true, 'Plan'],
      ['a.md', false, 'plan'],
    ]);
    // The extension isn't part of the name a search sees.
    expect(await nb.textSearch.find('md')).toEqual([]);
  });

  it('finds every note whose text has the filter words, however many there are', async () => {
    const files: Record<string, string> = { 'pic.png': 'the', 'none.md': 'nothing' };
    for (let i = 0; i < 600; i++) files[`n${i}.md`] = 'the cat and the hat\n'.repeat(3);
    const { nb } = await open(files);
    const tab = (await nb.openFile('none.md'))!;
    tab.state!.insert('THE ');
    const paths = [...Object.keys(files)];
    const found = await nb.textSearch.notesContaining('the', paths);
    expect(found.size).toBe(601);
    expect(found.has('none.md')).toBe(true);
    expect(found.has('pic.png')).toBe(false);
    // Only the paths asked about are read.
    expect(await nb.textSearch.notesContaining('the', ['n1.md', 'none.md'])).toEqual(
      new Set(['n1.md', 'none.md']),
    );
  });
});

describe('Notebook session and settings', () => {
  it('remembers tabs, folders and the sidebar in .puffinote', async () => {
    const { store, nb } = await open({ 'f/a.md': 'abc', 'b.md': '' });
    const a = (await nb.openFile('f/a.md'))!;
    await nb.openFile('b.md');
    a.state!.select(2);
    nb.activate(a);
    nb.toggleFolder('f', true);
    nb.panel.value = 'outline';
    await nb.persist();
    expect(await text(store, '.puffinote/.gitignore')).toContain('session.json');
    expect(JSON.parse(await text(store, sessionPath)).active).toBe('f/a.md');

    const again = await Notebook.open(store, { prefs: memoryPrefs() });
    expect(again.tabs.value.map((t) => t.path.value)).toEqual(['f/a.md', 'b.md']);
    expect(again.active.value?.path.value).toBe('f/a.md');
    expect(again.active.value?.state?.selection.value.caret).toBe(2);
    expect([...again.expanded.value]).toEqual(['f']);
    expect(again.panel.value).toBe('outline');
  });

  it('layers notebook overrides over user preferences', async () => {
    const { store, nb } = await open({ '.puffinote/settings.json': '{"indent":"tab","bogus":1}' });
    expect(nb.config.settings.value.indent).toBe('tab');
    expect(nb.config.settings.value.autoSave).toBe(false);
    nb.config.setPreferences({ indent: 'spaces2', autoSave: true });
    expect(nb.config.settings.value.indent).toBe('tab');
    expect(nb.config.settings.value.autoSave).toBe(true);
    await nb.config.setOverrides({});
    expect(nb.config.settings.value.indent).toBe('spaces2');
    // A key this version doesn't know (from a newer Puffinote, say) is kept, not dropped.
    expect(JSON.parse(await text(store, '.puffinote/settings.json'))).toEqual({ bogus: 1 });
  });
});

describe('outline', () => {
  it('lists ATX and setext headings without markup', async () => {
    const { nb } = await open({
      'a.md': '# One **bold**\n\ntext\n\nTwo\n---\n\n```\n# not\n```\n### Three ###',
    });
    const st = (await nb.openFile('a.md'))!.state!;
    expect(outlineOf(st.document, st.model).map((o) => [o.level, o.text, o.line])).toEqual([
      [1, 'One bold', 0],
      [2, 'Two', 4],
      [3, 'Three', 10],
    ]);
  });
});

describe('opening other files', () => {
  it('asks before opening in the default app, unless the setting is off', async () => {
    const store = new MemoryStore('Notes', { 'a.pdf': '' });
    const opened: string[] = [];
    Object.assign(store, { openExternal: async (p: string) => void opened.push(p) });
    let answer = false;
    const asked: string[] = [];
    const prefs = memoryPrefs({ autoSave: false });
    const nb = await Notebook.open(store, {
      prefs,
      confirmOpenExternal: async (name) => (asked.push(name), answer),
    });
    expect(await nb.openFile('a.pdf')).toBeNull();
    expect([asked, opened]).toEqual([['a.pdf'], []]);
    answer = true;
    await nb.openFile('a.pdf');
    expect(opened).toEqual(['a.pdf']);
    nb.config.setPreferences({ confirmOpenExternal: false });
    await nb.openFile('a.pdf');
    expect([asked.length, opened.length]).toEqual([2, 2]);
  });
});

describe('Notebook settings written by someone else too', () => {
  const read = async (store: MemoryStore) =>
    JSON.parse(new TextDecoder().decode((await store.read('.puffinote/settings.json')).bytes)) as unknown;

  it('merges a change into what the file holds now, keeping keys it doesn’t know', async () => {
    const store = new MemoryStore('N', { '.puffinote/settings.json': '{"accent":"teal"}' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs() });
    store.touch('.puffinote/settings.json', '{"accent":"purple","futureThing":1}');
    await nb.config.setOverrides({ ...nb.config.overrides.value, layout: 'library' });
    expect(await read(store)).toEqual({ accent: 'purple', futureThing: 1, layout: 'library' });
    expect(nb.config.overrides.value).toEqual({ accent: 'purple', layout: 'library' });
  });

  it('a dialog opened before a refresh writes only what it changed, keeping what the refresh brought', async () => {
    const store = new MemoryStore('N', {});
    const nb = await Notebook.open(store, { prefs: memoryPrefs() });
    const atOpen = nb.config.overrides.value; // the Settings dialog opens
    store.touch('.puffinote/settings.json', '{"accent":"teal"}'); // git pull
    await nb.refresh();
    await nb.config.setOverrides({ ...atOpen, layout: 'library' }, atOpen); // Done
    expect(await read(store)).toEqual({ accent: 'teal', layout: 'library' });
    expect(nb.config.overrides.value).toEqual({ accent: 'teal', layout: 'library' });
  });

  it('a dialog writes only the preferences it changed, keeping another window’s', async () => {
    const prefs = memoryPrefs({ indent: 'tab' });
    const nb = await Notebook.open(new MemoryStore('N', {}), { prefs });
    const atOpen = nb.config.userPrefs.value;
    prefs.save({ ...prefs.load(), accent: 'purple' }); // another window
    nb.config.setPreferences({ ...atOpen, autoSave: false }, atOpen);
    expect(prefs.load()).toEqual({ indent: 'tab', accent: 'purple', autoSave: false });
    expect(nb.config.userPrefs.value).toEqual({ indent: 'tab', accent: 'purple', autoSave: false });
  });

  it('takes the file’s changes on refresh', async () => {
    const store = new MemoryStore('N', {});
    const nb = await Notebook.open(store, { prefs: memoryPrefs() });
    store.touch('.puffinote/settings.json', '{"layout":"library"}');
    await nb.refresh();
    expect(nb.config.settings.value.layout).toBe('library');
  });
});

describe('Notebook pictures read into the editor', () => {
  const blobText = async (url: string) => (await (await fetch(url)).text()) as string;
  const loaded = async (nb: Notebook, ref: string) => {
    let url = '';
    await vi.waitFor(() => {
      url = nb.resolveImage('a.md', ref);
      expect(url).toMatch(/^blob:/);
    });
    return url;
  };

  it('tries again for a picture that couldn’t be read', async () => {
    const { store, nb } = await open({ 'a.md': '![](p.png)' });
    expect(nb.resolveImage('a.md', 'p.png')).toBe('');
    await new Promise((r) => setTimeout(r, 10));
    await store.write('p.png', new TextEncoder().encode('png'), { create: true });
    expect(await blobText(await loaded(nb, 'p.png'))).toBe('png');
  });

  it('reads a picture again when the disk says it changed', async () => {
    const { store, nb } = await open({ 'a.md': '![](p.png)', 'p.png': 'old' });
    const first = await loaded(nb, 'p.png');
    store.touch('p.png', 'new');
    await vi.waitFor(async () => {
      const url = nb.resolveImage('a.md', 'p.png');
      expect(url).not.toBe(first);
      expect(url && (await blobText(url))).toBe('new');
    });
  });

  it('reads a picture again when a refresh finds it changed', async () => {
    const { store, nb } = await open({ 'a.md': '![](p.png)', 'p.png': 'old' });
    const first = await loaded(nb, 'p.png');
    await store.write('p.png', new TextEncoder().encode('newer'), { force: true });
    await nb.refresh();
    await vi.waitFor(async () => {
      const url = nb.resolveImage('a.md', 'p.png');
      expect(url).not.toBe(first);
      expect(url && (await blobText(url))).toBe('newer');
    });
  });
});

describe('Notebook saving to a store that has no versions', () => {
  /** Like a NAS that sends no ETag: every read and write reports a null version. */
  class Unversioned extends MemoryStore {
    override async read(path: string) {
      return { ...(await super.read(path)), version: null };
    }
    override async write(...args: Parameters<MemoryStore['write']>) {
      await super.write(...args);
      return null;
    }
  }
  const enc = (s: string) => new TextEncoder().encode(s);

  async function edited() {
    const store = new Unversioned('N', { 'a.md': 'one' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.insert('mine ');
    return { store, nb, tab };
  }

  it('saves when the file still holds what was read', async () => {
    const { store, nb, tab } = await edited();
    expect(await nb.save(tab)).toBe(true);
    expect(await text(store, 'a.md')).toBe('mine one');
    tab.state!.insert('again ');
    expect(await nb.save(tab)).toBe(true);
    expect(await text(store, 'a.md')).toBe('mine again one');
  });

  it('flags a file changed by someone else instead of writing over it', async () => {
    const { store, nb, tab } = await edited();
    await MemoryStore.prototype.write.call(store, 'a.md', enc('theirs'), { force: true });
    expect(await nb.save(tab)).toBe(false);
    expect(tab.conflict.peek()).toBe('changed');
    expect(await text(store, 'a.md')).toBe('theirs');
    expect(await nb.save(tab, true)).toBe(true);
    expect(await text(store, 'a.md')).toBe('mine one');
  });

  it('flags a file deleted by someone else instead of recreating it', async () => {
    const { store, nb, tab } = await edited();
    await store.remove('a.md');
    expect(await nb.save(tab)).toBe(false);
    expect(tab.conflict.peek()).toBe('deleted');
    await expect(store.read('a.md')).rejects.toThrow();
  });
});
