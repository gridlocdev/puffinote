import { describe, expect, it } from 'vitest';
import { Notebook } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { findAll, matchAt, matchFrom } from '../src/editor/find.ts';
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

  it('opens the notebook search with ⇧⌘F', () => {
    const m = new ShellModel();
    expect(m.shellCatalog.get('view.sidebar.search')?.shortcut).toBe('mod+shift+f');
    expect(m.shellCatalog.get('shell.find')?.shortcut).toBe('mod+f');
  });
});
