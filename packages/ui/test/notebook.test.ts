import { describe, expect, it, vi } from 'vitest';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import { Notebook, sessionPath } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { filterTree, visibleRows } from '../src/notebook/tree.ts';
import { outlineOf } from '../src/notebook/outline.ts';

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
    const hits = await nb.search('beta');
    expect(hits.map((h) => [h.path, h.line, h.preview.slice(h.matchStart, h.matchStart + h.length)])).toEqual(
      [
        ['a.md', 1, 'Beta'],
        ['b.md', 0, 'beta'],
      ],
    );
    expect(hits[1]!.offset).toBe(3);
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
    expect(nb.settings.value.indent).toBe('tab');
    expect(nb.settings.value.autoSave).toBe(false);
    nb.setPreferences({ indent: 'spaces2', autoSave: true });
    expect(nb.settings.value.indent).toBe('tab');
    expect(nb.settings.value.autoSave).toBe(true);
    await nb.setOverrides({});
    expect(nb.settings.value.indent).toBe('spaces2');
    expect(JSON.parse(await text(store, '.puffinote/settings.json'))).toEqual({});
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
