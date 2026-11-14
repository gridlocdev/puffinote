import { describe, expect, it } from 'vitest';
import { Notebook } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { findAll, matchAt, matchFrom, matchesBetween, replaceAllEdit } from '../src/editor/find.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import { ShellModel } from '../src/shell/shellModel.ts';

describe('find in note', () => {
  it('finds every match, ignoring case, and treats the query as plain text', () => {
    expect(findAll('Puffin puffins PUFFIN', 'puffin')).toEqual([
      { start: 0, end: 6 },
      { start: 7, end: 13 },
      { start: 15, end: 21 },
    ]);
    expect(findAll('a.b axb (c)', 'a.b')).toEqual([{ start: 0, end: 3 }]);
    expect(findAll('(c) [d]', '(c)')).toEqual([{ start: 0, end: 3 }]);
    expect(findAll('aaaa', 'aa')).toEqual([
      { start: 0, end: 2 },
      { start: 2, end: 4 },
    ]);
    expect(findAll('anything', '')).toEqual([]);
  });

  it('steps forward and back from the selection, wrapping around the note', () => {
    const m = findAll('x x x', 'x'); // starts 0, 2, 4
    expect(matchFrom(m, 2, 0)).toBe(1);
    expect(matchFrom(m, 2, 1)).toBe(2);
    expect(matchFrom(m, 4, 1)).toBe(0);
    expect(matchFrom(m, 2, -1)).toBe(0);
    expect(matchFrom(m, 0, -1)).toBe(2);
    expect(matchFrom(m, 5, 0)).toBe(0);
    expect(matchFrom([], 0, 1)).toBe(-1);
    expect(matchAt(m, 2, 3)).toBe(1);
    expect(matchAt(m, 2, 2)).toBe(-1);
    expect(matchAt(m, 1, 2)).toBe(-1);
  });

  async function setup(text: string) {
    const nb = await Notebook.open(new MemoryStore('N', { 'a.md': text }), { prefs: memoryPrefs() });
    const m = new ShellModel();
    m.setNotebook(nb);
    const tab = (await nb.openFile('a.md'))!;
    return { m, state: tab.state! };
  }

  it('starts from the selected text, selects matches as you type and step, and counts them', async () => {
    const { m, state } = await setup('Puffins nest in burrows. A puffin lays one egg. PUFFIN!');
    state.select(27, 33); // "puffin"
    m.shellCatalog.run('shell.find');
    expect(m.findQuery.value).toBe('puffin');
    expect(m.findMatches.value).toEqual({ matches: findAll(state.text, 'puffin'), current: 1 });
    m.shellCatalog.run('shell.findNext');
    expect(state.selection.value).toEqual({ anchor: 48, caret: 54 });
    m.shellCatalog.run('shell.findNext');
    expect(state.selection.value).toEqual({ anchor: 0, caret: 6 });
    m.shellCatalog.run('shell.findPrevious');
    expect(state.selection.value).toEqual({ anchor: 48, caret: 54 });
    m.setFindQuery('puffin l');
    expect(state.selection.value).toEqual({ anchor: 27, caret: 35 });
    expect(m.findMatches.value?.current).toBe(0);
    m.setFindQuery('gannet');
    expect(m.findMatches.value).toEqual({ matches: [], current: -1 });
    expect(state.selection.value).toEqual({ anchor: 27, caret: 35 });
    m.closeFind();
    expect(m.findQuery.value).toBeNull();
    expect(m.findMatches.value).toBeNull();
    expect(m.shellCatalog.isEnabled(m.shellCatalog.get('shell.findNext')!)).toBe(false);
  });

  it('keeps the last text when opened again with nothing selected, and follows edits', async () => {
    const { m, state } = await setup('egg egg');
    m.openFind();
    expect(m.findQuery.value).toBe('');
    m.setFindQuery('egg');
    m.closeFind();
    state.select(0);
    m.openFind();
    expect(m.findQuery.value).toBe('egg');
    state.select(7);
    state.insert(' egg');
    expect(m.findMatches.value?.matches).toHaveLength(3);
  });

  it('searches the note again only when the text or the query changes, not when the selection moves', async () => {
    const { m, state } = await setup('egg egg egg');
    m.openFind();
    m.setFindQuery('egg');
    const first = m.findMatches.value!.matches;
    state.select(4, 7);
    expect(m.findMatches.value).toEqual({ matches: first, current: 1 });
    expect(m.findMatches.value!.matches).toBe(first);
    state.select(5);
    expect(m.findMatches.value!.current).toBe(-1);
    expect(m.findMatches.value!.matches).toBe(first);
    state.insert('x');
    expect(m.findMatches.value!.matches).not.toBe(first);
  });

  it('picks out the matches that overlap a range, for drawing only the rows in view', () => {
    const matches = findAll('ab ab ab ab', 'ab'); // 0, 3, 6, 9
    expect(matchesBetween(matches, 0, 11)).toEqual([0, 4]);
    expect(matchesBetween(matches, 4, 7)).toEqual([1, 3]);
    expect(matchesBetween(matches, 5, 6)).toEqual([2, 2]);
    expect(matchesBetween(matches, 11, 20)).toEqual([4, 4]);
    expect(matchesBetween([], 0, 5)).toEqual([0, 0]);
  });

  it('opens the notebook search with ⇧⌘F', () => {
    const m = new ShellModel();
    expect(m.shellCatalog.get('view.sidebar.search')?.shortcut).toBe('mod+shift+f');
    expect(m.shellCatalog.get('shell.find')?.shortcut).toBe('mod+f');
  });

  it('builds Replace All as one edit over the span of the matches, keeping the text between them', () => {
    const text = 'a cat, a Cat; dog';
    const m = findAll(text, 'cat');
    expect(replaceAllEdit(text, m, 'puffin')).toEqual({
      start: 2,
      length: 10,
      text: 'puffin, a puffin',
      anchor: 18,
      caret: 18,
    });
    expect(replaceAllEdit(text, [], 'x')).toBeNull();
    expect(findAll('x'.repeat(20_000), 'x', Infinity)).toHaveLength(20_000);
  });

  it('opens the bar with Replace showing on ⌥⌘F / Ctrl+H', () => {
    const m = new ShellModel();
    const spec = m.shellCatalog.get('shell.replace')!;
    expect(['mod+alt+f', 'mod+h']).toContain(spec.shortcut);
    expect(spec.global).toBe(true);
  });

  it('replaces the match the selection is on and goes to the next, one undo step each', async () => {
    const { m, state } = await setup('egg and Egg and egg');
    state.select(0);
    m.shellCatalog.run('shell.replace');
    expect(m.replaceOpen.value).toBe(true);
    m.setFindQuery('egg');
    expect(state.selection.value).toEqual({ anchor: 0, caret: 3 });
    m.replaceText.value = 'eggs';
    expect(m.shellCatalog.run('shell.replaceOne')).toBe(true);
    expect(state.text).toBe('eggs and Egg and egg');
    expect(state.selection.value).toEqual({ anchor: 9, caret: 12 });
    m.shellCatalog.run('shell.replaceOne');
    expect(state.text).toBe('eggs and eggs and egg');
    expect(state.selection.value).toEqual({ anchor: 18, caret: 21 });
    state.undo();
    expect(state.text).toBe('eggs and Egg and egg');
    state.undo();
    expect(state.text).toBe('egg and Egg and egg');
  });

  it('only goes to the next match when the selection is on none, and replaces the last one in place', async () => {
    const { m, state } = await setup('ab ab');
    m.openFind(true);
    m.setFindQuery('ab');
    m.replaceText.value = 'abab'; // the replacement holds the query: it is never matched again in this step
    state.select(1);
    m.replaceCurrent();
    expect(state.text).toBe('ab ab');
    expect(state.selection.value).toEqual({ anchor: 3, caret: 5 });
    m.replaceCurrent();
    expect(state.text).toBe('ab abab');
    expect(state.selection.value).toEqual({ anchor: 0, caret: 2 });
  });

  it('replaces every match as one undo step, and says how many', async () => {
    const { m, state } = await setup('Puffin puffin PUFFIN gannet');
    m.openFind(true);
    m.setFindQuery('puffin');
    m.replaceText.value = '';
    m.shellCatalog.run('shell.replaceAll');
    expect(state.text).toBe('   gannet');
    expect(m.notebook.value!.notice.value?.text).toBe('Replaced 3 matches');
    state.undo();
    expect(state.text).toBe('Puffin puffin PUFFIN gannet');
    m.setFindQuery('heron');
    expect(m.shellCatalog.isEnabled(m.shellCatalog.get('shell.replaceAll')!)).toBe(true);
    m.replaceAll();
    expect(m.notebook.value!.notice.value?.text).toBe('Nothing to replace');
    m.closeFind();
    expect(m.replaceOpen.value).toBe(false);
    expect(m.shellCatalog.isEnabled(m.shellCatalog.get('shell.replaceAll')!)).toBe(false);
  });

  it('never replaces in a note Puffinote won’t save', async () => {
    const bytes = new Uint8Array([0x65, 0x67, 0x67, 0x20, 0xff, 0xfe, 0x80]); // "egg " and bytes that aren't UTF-8
    const nb = await Notebook.open(new MemoryStore('N', { 'a.md': bytes }), { prefs: memoryPrefs() });
    const m = new ShellModel();
    m.setNotebook(nb);
    const tab = (await nb.openFile('a.md'))!;
    expect(tab.readOnly).toBe(true);
    const before = tab.state!.text;
    m.openFind(true);
    m.setFindQuery('egg');
    m.replaceText.value = 'x';
    expect(m.shellCatalog.isEnabled(m.shellCatalog.get('shell.replaceOne')!)).toBe(false);
    m.replaceCurrent();
    m.replaceAll();
    expect(tab.state!.text).toBe(before);
  });
});
