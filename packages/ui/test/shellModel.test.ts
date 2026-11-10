import { describe, expect, it } from 'vitest';
import { Notebook } from '../src/notebook/notebook.ts';
import { findNode } from '../src/notebook/tree.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import { nextTabStop } from '../src/shell/Dialog.tsx';
import { ShellModel } from '../src/shell/shellModel.ts';

const buttons = [
  { label: 'Cancel', value: 'cancel' },
  { label: 'OK', value: 'ok', kind: 'primary' as const },
];

describe('ShellModel overlays', () => {
  it('answers a confirm as cancelled when another overlay replaces it', async () => {
    const m = new ShellModel();
    const first = m.confirm({ title: 'First?', message: '', buttons });
    expect(m.asking).toBe(true);
    const second = m.confirm({ title: 'Second?', message: '', buttons });
    expect(await first).toBe('cancel');
    const o = m.overlay.value;
    expect(o?.kind === 'confirm' && o.options.title).toBe('Second?');
    if (o?.kind === 'confirm') o.done('ok');
    expect(await second).toBe('ok');
    expect(m.overlay.value).toBeNull();
    expect(m.asking).toBe(false);
  });

  it('answers a prompt with null when it is replaced or closed', async () => {
    const m = new ShellModel();
    const replaced = m.prompt({ title: 'Name', label: 'Name', value: 'x', confirm: 'OK' });
    m.open({ kind: 'commands' });
    expect(await replaced).toBeNull();
    expect(m.overlay.value?.kind).toBe('commands');
    const closed = m.prompt({ title: 'Name', label: 'Name', value: 'x', confirm: 'OK' });
    m.close();
    expect(await closed).toBeNull();
  });

  it('lets a quit go ahead when a close-tab question is still up', async () => {
    const store = new MemoryStore('N', { 'a.md': '' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.setNotebook(nb);
    const tab = (await nb.openFile('a.md'))!;
    tab.state!.insert('x');
    const closing = m.closeTab(tab);
    await Promise.resolve();
    expect(m.overlay.value?.kind).toBe('confirm');
    const quitting = m.confirmDiscardAll();
    expect(await closing).toBe(false); // the close was cancelled, the tab stays
    expect(nb.tabs.value).toContain(tab);
    const o = m.overlay.value;
    if (o?.kind === 'confirm') o.done('discard');
    expect(await quitting).toBe(true);
    m.setNotebook(null);
  });
});

describe('ShellModel Move To', () => {
  it('offers every folder the item can move into, the top level first', async () => {
    const store = new MemoryStore('N', {
      'a/x.md': '',
      'a/b/y.md': '',
      'c/z.md': '',
      'c/a/w.md': '',
      'top.md': '',
    });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.setNotebook(nb);
    // Not its own folder or anything inside it, and not where a same-named item already is.
    expect(m.moveTargets('a')).toEqual(['c/a']);
    expect(m.moveTargets('a/b')).toEqual(['', 'c', 'c/a']);
    expect(m.moveTargets('top.md')).toEqual(['a', 'a/b', 'c', 'c/a']);
    m.openFileMenu(findNode(nb.tree.value, 'top.md'), '', { x: 0, y: 0 });
    const menu = m.overlay.value;
    const item = menu?.kind === 'menu' ? menu.items.find((i) => i?.label === 'Move To…') : undefined;
    item?.run();
    expect(m.overlay.value).toEqual({ kind: 'moveTo', path: 'top.md' });
    m.setNotebook(null);
  });
});

describe('ShellModel pinned tabs', () => {
  it('leaves pinned tabs open on Close Other and Close All', async () => {
    const store = new MemoryStore('N', { 'a.md': '', 'b.md': '', 'c.md': '' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.setNotebook(nb);
    const a = (await nb.openFile('a.md'))!;
    await nb.openFile('b.md');
    const c = (await nb.openFile('c.md'))!;
    nb.setTabPinned(a, true);
    await m.closeTabs(c);
    expect(nb.tabs.value.map((t) => t.path.value)).toEqual(['a.md', 'c.md']);
    await m.closeTabs(null);
    expect(nb.tabs.value.map((t) => t.path.value)).toEqual(['a.md']);
    await m.closeTab(a);
    expect(nb.tabs.value).toEqual([]);
    m.setNotebook(null);
  });
});

describe('ShellModel editor menu', () => {
  it('offers the clipboard and Select All, with Cut and Copy off for an empty selection', async () => {
    const store = new MemoryStore('N', { 'a.md': 'hello' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.setNotebook(nb);
    const tab = (await nb.openFile('a.md'))!;
    const labels = () => {
      const o = m.overlay.value;
      return o?.kind === 'menu'
        ? o.items.map((i) => (i ? `${i.label}${i.disabled ? ' (off)' : ''}` : '-'))
        : [];
    };
    m.openEditorMenu({ x: 0, y: 0 });
    expect(labels()).toEqual(['Cut (off)', 'Copy (off)', 'Paste', '-', 'Select All']);
    tab.state!.select(0, 2);
    m.openEditorMenu({ x: 0, y: 0 });
    expect(labels()).toEqual(['Cut', 'Copy', 'Paste', '-', 'Select All']);
    m.setNotebook(null);
  });
});

describe('Dialog focus', () => {
  it('cycles Tab and Shift+Tab through the controls without leaving', () => {
    expect([0, 1, 2].map((i) => nextTabStop(3, i, false))).toEqual([1, 2, 0]);
    expect([0, 1, 2].map((i) => nextTabStop(3, i, true))).toEqual([2, 0, 1]);
    // Focus on something that isn't a stop (the dialog's text): Tab goes to the first, Shift+Tab to the last.
    expect(nextTabStop(3, -1, false)).toBe(0);
    expect(nextTabStop(3, -1, true)).toBe(2);
  });
});

describe('ShellModel zoom', () => {
  it('steps by 10%, clamps, resets, and runs from the catalog', async () => {
    const m = new ShellModel();
    m.setZoom(1);
    await m.menuCatalog.run('view.zoomIn');
    await m.menuCatalog.run('view.zoomIn');
    expect(m.zoom.value).toBe(1.2);
    await m.menuCatalog.run('view.zoomOut');
    expect(m.zoom.value).toBe(1.1);
    m.setZoom(99);
    expect(m.zoom.value).toBe(3);
    expect(m.menuCatalog.isEnabled(m.menuCatalog.get('view.zoomIn')!)).toBe(false);
    m.setZoom(0);
    expect(m.zoom.value).toBe(0.5);
    await m.menuCatalog.run('view.zoomReset');
    expect(m.zoom.value).toBe(1);
  });
});
