import { effect } from '@preact/signals';
import { describe, expect, it, vi } from 'vitest';
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
    m.session.set(nb);
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
    m.session.set(null);
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
    m.session.set(nb);
    // Not its own folder or anything inside it, and not where a same-named item already is.
    expect(m.moveTargets('a')).toEqual(['c/a']);
    expect(m.moveTargets('a/b')).toEqual(['', 'c', 'c/a']);
    expect(m.moveTargets('top.md')).toEqual(['a', 'a/b', 'c', 'c/a']);
    m.openFileMenu(findNode(nb.tree.value, 'top.md'), '', { x: 0, y: 0 });
    const menu = m.overlay.value;
    const item = menu?.kind === 'menu' ? menu.items.find((i) => i?.label === 'Move To…') : undefined;
    item?.run();
    expect(m.overlay.value).toEqual({ kind: 'moveTo', path: 'top.md' });
    m.session.set(null);
  });
});

describe('ShellModel pinned tabs', () => {
  it('leaves pinned tabs open on Close Other and Close All', async () => {
    const store = new MemoryStore('N', { 'a.md': '', 'b.md': '', 'c.md': '' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.session.set(nb);
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
    m.session.set(null);
  });
});

describe('ShellModel context menus run through the catalog', () => {
  const menuItem = (m: ShellModel, label: string) => {
    const o = m.overlay.value;
    if (o?.kind !== 'menu') throw new Error('no menu');
    const item = o.items.find((i) => i?.label === label);
    if (!item) throw new Error(`no ${label}`);
    m.close();
    return item;
  };

  it('acts on the tab it was opened on, not the one in front, and counts as a recent command', async () => {
    const store = new MemoryStore('N', { 'a.md': '', 'b.md': '', 'c.md': '' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.session.set(nb);
    const a = (await nb.openFile('a.md'))!;
    await nb.openFile('b.md');
    await nb.openFile('c.md');
    m.openTabMenu(a, { x: 0, y: 0 });
    menuItem(m, 'Pin Tab').run();
    expect(a.pinned.value).toBe(true);
    m.openTabMenu(a, { x: 0, y: 0 });
    menuItem(m, 'Close Other Tabs').run();
    await vi.waitFor(() => expect(nb.tabs.value.map((t) => t.path.value)).toEqual(['a.md']));
    expect(m.menuCatalog.recentIds.slice(0, 2)).toEqual(['shell.closeOtherTabs', 'shell.pinTab']);
    m.session.set(null);
  });

  it('moves the file it was opened on, not the selected one', async () => {
    const store = new MemoryStore('N', { 'a.md': '', 'b.md': '' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.session.set(nb);
    m.openFileMenu(findNode(nb.tree.value, 'b.md')!, '', { x: 0, y: 0 });
    menuItem(m, 'Move To…').run();
    const o = m.overlay.value;
    expect(o?.kind === 'moveTo' && o.path).toBe('b.md');
    m.session.set(null);
  });

  it('runs nothing while a question waits on the user', async () => {
    const store = new MemoryStore('N', { 'a.md': '', 'b.md': '' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.session.set(nb);
    const a = (await nb.openFile('a.md'))!;
    m.openTabMenu(a, { x: 0, y: 0 });
    const close = menuItem(m, 'Close Tab');
    void m.confirm({ title: 'Wait', message: '', buttons });
    close.run();
    expect(m.menuCatalog.run('shell.closeAllTabs')).toBe(false);
    await Promise.resolve();
    expect(nb.tabs.value).toEqual([a]);
    m.close();
    m.session.set(null);
  });
});

describe('ShellModel editor menu', () => {
  it('offers the clipboard and Select All, with Cut and Copy off for an empty selection', async () => {
    const store = new MemoryStore('N', { 'a.md': 'hello' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.session.set(nb);
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
    m.session.set(null);
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

describe('ShellModel Delete Picture', () => {
  async function setup(files: Record<string, string>) {
    const store = new MemoryStore('N', files);
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.session.set(nb);
    const tab = (await nb.openFile('a.md'))!;
    const text = tab.state!.text;
    const start = text.indexOf('![');
    const end = text.indexOf(')', start) + 1;
    return { nb, m, tab, picture: { start, end, url: 'pic.png' } };
  }

  it('takes the picture and its line out of the note and deletes a file no other note shows', async () => {
    const { nb, m, tab, picture } = await setup({ 'a.md': 'one\n![](pic.png)\ntwo', 'pic.png': '' });
    await m.deletePicture(picture);
    expect(tab.state!.text).toBe('one\ntwo');
    expect(m.overlay.value).toBeNull();
    expect(findNode(nb.tree.value, 'pic.png')).toBeNull();
  });

  it('brings the file back on Undo and deletes it again on Redo', async () => {
    const { nb, m, tab, picture } = await setup({ 'a.md': 'one\n![](pic.png)\ntwo', 'pic.png': 'PNG' });
    await m.deletePicture(picture);
    expect(findNode(nb.tree.value, 'pic.png')).toBeNull();
    tab.state!.undo();
    expect(tab.state!.text).toBe('one\n![](pic.png)\ntwo');
    await vi.waitFor(() => expect(findNode(nb.tree.value, 'pic.png')).not.toBeNull());
    expect(new TextDecoder().decode((await nb.store.read('pic.png')).bytes)).toBe('PNG');
    tab.state!.redo();
    expect(tab.state!.text).toBe('one\ntwo');
    await vi.waitFor(() => expect(findNode(nb.tree.value, 'pic.png')).toBeNull());
  });

  it('asks before deleting a file other notes show, listing them, and keeps it by default', async () => {
    const { nb, m, tab, picture } = await setup({
      'a.md': 'see ![](pic.png) here',
      'sub/b.md': '![x](../pic.png)',
      'c.md': 'nothing',
      'pic.png': '',
    });
    const deleting = m.deletePicture(picture);
    await vi.waitFor(() => expect(m.overlay.value?.kind).toBe('confirm'));
    const o = m.overlay.value;
    expect(o?.kind === 'confirm' && o.options.list).toEqual(['sub/b']);
    if (o?.kind === 'confirm') o.done('link');
    await deleting;
    expect(tab.state!.text).toBe('see  here');
    expect(findNode(nb.tree.value, 'pic.png')).not.toBeNull();
  });

  it('deletes the shared file when asked to', async () => {
    const { nb, m, picture } = await setup({ 'a.md': '![](pic.png)', 'b.md': '![](pic.png)', 'pic.png': '' });
    const deleting = m.deletePicture(picture);
    await vi.waitFor(() => expect(m.overlay.value?.kind).toBe('confirm'));
    const o = m.overlay.value;
    if (o?.kind === 'confirm') o.done('file');
    await deleting;
    expect(findNode(nb.tree.value, 'pic.png')).toBeNull();
  });
});

describe('ShellModel picture preview and menu (PRD §10.6)', () => {
  async function setup(files: Record<string, string>) {
    const store = new MemoryStore('N', files);
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.session.set(nb);
    const tab = (await nb.openFile('a.md'))!;
    const text = tab.state!.text;
    const start = text.indexOf('![');
    const end = text.indexOf(')', start) + 1;
    return { nb, m, tab, picture: { start, end, url: 'pic.png' } };
  }
  const labels = (items: ReturnType<ShellModel['pictureMenuItems']>) =>
    items?.map((it) => (it ? it.label : '-'));

  it('offers the same menu in the note and in the preview, less Preview Picture', async () => {
    const { m, picture } = await setup({ 'a.md': '![](pic.png)', 'pic.png': '' });
    expect(labels(m.pictureMenuItems(picture))).toEqual([
      'Preview Picture',
      'Open in Tab',
      '-',
      'Copy Picture',
      'Copy Markdown',
      '-',
      'Edit Description…',
      'Rename Picture…',
      'View Link Text',
      '-',
      'Delete Picture',
    ]);
    m.openPreview(picture);
    expect(m.preview.value).toMatchObject({ picture, name: 'pic.png' });
    expect(labels(m.pictureMenuItems(picture))?.[0]).toBe('Open in Tab');
  });

  it('View Link Text closes the preview and shows the picture’s Markdown, selected', async () => {
    const { m, tab, picture } = await setup({ 'a.md': 'x ![](pic.png)', 'pic.png': '' });
    m.openPreview(picture);
    m.pictureMenuItems(picture)!
      .find((it) => it?.label === 'View Link Text')!
      .run();
    expect(m.preview.value).toBeNull();
    expect(tab.state!.shownPicture.value).toBe(picture.start);
    expect(tab.state!.selection.value).toEqual({ anchor: picture.start, caret: picture.end });
    tab.state!.select(0);
    expect(tab.state!.shownPicture.value).toBeNull();
  });

  it('Edit Description… rewrites the alt text as one undo step', async () => {
    const { m, tab, picture } = await setup({ 'a.md': 'x ![old \\] one](pic.png "t") y', 'pic.png': '' });
    const editing = m.editPictureDescription(picture);
    await vi.waitFor(() => expect(m.overlay.value?.kind).toBe('prompt'));
    const o = m.overlay.value;
    expect(o?.kind === 'prompt' && o.options.value).toBe('old ] one');
    if (o?.kind === 'prompt') o.done('a [new] one');
    await editing;
    expect(tab.state!.text).toBe('x ![a \\[new\\] one](pic.png "t") y');
    tab.state!.undo();
    expect(tab.state!.text).toBe('x ![old \\] one](pic.png "t") y');
  });

  it('closes the preview when another note comes to the front', async () => {
    const { nb, m, picture } = await setup({ 'a.md': '![](pic.png)', 'b.md': 'b', 'pic.png': '' });
    m.openPreview(picture);
    expect(m.preview.value).not.toBeNull();
    await nb.openFile('b.md');
    expect(m.preview.value).toBeNull();
  });

  it('Delete Picture and Edit Description… change nothing in a note that has no such picture there', async () => {
    const { nb, m, picture } = await setup({
      'a.md': '![](pic.png)',
      'b.md': 'plain words here',
      'other.png': '',
      'pic.png': '',
    });
    const b = (await nb.openFile('b.md'))!;
    await m.deletePicture(picture);
    expect(b.state!.text).toBe('plain words here');
    expect(findNode(nb.tree.value, 'pic.png')).not.toBeNull();
    await m.editPictureDescription(picture);
    expect(m.overlay.value).toBeNull();
    // A different picture at the same offsets isn't the one asked for either.
    b.state!.load('![](other.png)');
    await m.deletePicture(picture);
    expect(b.state!.text).toBe('![](other.png)');
    expect(findNode(nb.tree.value, 'other.png')).not.toBeNull();
  });

  it('Delete Picture from the preview closes it', async () => {
    const { m, picture } = await setup({ 'a.md': '![](pic.png)', 'pic.png': '' });
    m.openPreview(picture);
    await m.deletePicture(picture);
    expect(m.preview.value).toBeNull();
  });
});

describe('ShellModel Delete Picture Redo', () => {
  it('keeps the file on Redo when another note shows it by then', async () => {
    const store = new MemoryStore('N', { 'a.md': '![](pic.png)', 'b.md': '', 'pic.png': 'PNG' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.session.set(nb);
    const b = (await nb.openFile('b.md'))!;
    const tab = (await nb.openFile('a.md'))!;
    await m.deletePicture({ start: 0, end: 12, url: 'pic.png' });
    expect(findNode(nb.tree.value, 'pic.png')).toBeNull();
    tab.state!.undo();
    await vi.waitFor(() => expect(findNode(nb.tree.value, 'pic.png')).not.toBeNull());
    b.state!.insert('![](pic.png)');
    tab.state!.redo();
    await new Promise((r) => setTimeout(r, 10));
    expect(findNode(nb.tree.value, 'pic.png')).not.toBeNull();
    await store.read('pic.png');
    m.session.set(null);
  });
});

describe('ShellModel switching notebooks', () => {
  it('closes overlays, the preview and the find bar, and answers open questions as cancelled', async () => {
    const one = new MemoryStore('One', { 'a.md': '![](pic.png)', 'pic.png': '' });
    const two = new MemoryStore('Two', { 'a.md': '', 'pic.png': '' });
    const nb1 = await Notebook.open(one, { prefs: memoryPrefs({ autoSave: false }) });
    const nb2 = await Notebook.open(two, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.session.set(nb1);
    await nb1.openFile('a.md');
    m.openPreview({ start: 0, end: 12, url: 'pic.png' });
    m.findQuery.value = 'x';
    const asking = m.confirm({ title: 'Delete?', message: '', buttons });
    m.session.set(nb2);
    expect(await asking).toBe('cancel');
    expect(m.overlay.value).toBeNull();
    expect(m.preview.value).toBeNull();
    expect(m.findQuery.value).toBeNull();
    m.session.set(null);
  });

  it('a closed notebook refuses to delete or move files', async () => {
    const one = new MemoryStore('One', { 'pic.png': '', 'a.md': '' });
    const nb1 = await Notebook.open(one, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.session.set(nb1);
    m.open({ kind: 'unusedPictures', paths: ['pic.png'] });
    m.session.set(null);
    expect(m.overlay.value).toBeNull();
    await nb1.pictures.removeUnused(['pic.png']);
    await nb1.pictures.removeAll(['pic.png']);
    await nb1.move('a.md', 'x');
    await one.read('pic.png');
    await one.read('a.md');
  });
});

describe('ShellModel Delete…', () => {
  async function asked(files: Record<string, string>, path: string, dirty: string[] = []) {
    const store = new MemoryStore('N', files);
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.session.set(nb);
    for (const p of dirty) (await nb.openFile(p))!.state!.insert('typed ');
    const removing = m.remove(path);
    await vi.waitFor(() => expect(m.overlay.value?.kind).toBe('confirm'));
    const o = m.overlay.value;
    if (o?.kind !== 'confirm') throw new Error('no confirm');
    return { o, removing, nb, m };
  }

  it('says when notes inside have unsaved changes', async () => {
    const { o, removing } = await asked({ 'f/a.md': '', 'f/b.md': '', 'c.md': '' }, 'f', ['f/a.md', 'c.md']);
    expect(o.options.message).toContain('1 note has unsaved changes that will be lost.');
    o.done('cancel');
    await removing;
  });

  it('lists the notes outside that show pictures in it', async () => {
    const { o, removing } = await asked(
      { 'f/pic.png': '', 'f/in.md': '![](pic.png)', 'out.md': '![](f/pic.png)', 'other.md': '' },
      'f',
    );
    expect(o.options.list).toEqual(['out']);
    expect(o.options.message).toMatch(/This note shows pictures in it:/);
    o.done('cancel');
    await removing;
  });
});

describe('ShellModel opening another notebook slowly', () => {
  const opened = (name: string, files: Record<string, string> = { 'a.md': '' }) =>
    Notebook.open(new MemoryStore(name, files), { prefs: memoryPrefs({ autoSave: false }) });

  it('asks again about text typed while the new notebook was opening', async () => {
    const m = new ShellModel();
    const old = await opened('Old');
    m.session.set(old);
    const ticket = m.session.beginOpen();
    const next = await opened('New');
    (await old.openFile('a.md'))!.state!.insert('typed meanwhile');
    const adopting = m.session.adopt(next, ticket);
    await vi.waitFor(() => expect(m.overlay.value?.kind).toBe('confirm'));
    const o = m.overlay.value;
    if (o?.kind === 'confirm') o.done('cancel');
    expect(await adopting).toBe(false);
    expect(m.notebook.value).toBe(old);
    expect(old.dirtyTabs).toHaveLength(1);
    m.session.set(null);
  });

  it('drops an open that a later one overtook', async () => {
    const m = new ShellModel();
    const first = m.session.beginOpen();
    const second = m.session.beginOpen();
    const two = await opened('Two');
    expect(await m.session.adopt(two, second)).toBe(true);
    const one = await opened('One');
    expect(await m.session.adopt(one, first)).toBe(false);
    expect(m.notebook.value).toBe(two);
    m.session.set(null);
  });
});

describe('ShellModel closing a tab that changed on disk', () => {
  async function conflicted() {
    const store = new MemoryStore('N', { 'a.md': 'one', 'b.md': 'two' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.session.set(nb);
    const a = (await nb.openFile('a.md'))!;
    const b = (await nb.openFile('b.md'))!;
    a.state!.insert('mine ');
    await store.write('a.md', new TextEncoder().encode('theirs'), { force: true });
    expect(await nb.save(a)).toBe(false);
    expect(a.conflict.peek()).toBe('changed');
    return { store, nb, m, a, b };
  }

  async function answer(m: ShellModel, value: string) {
    await vi.waitFor(() => expect(m.overlay.value?.kind).toBe('confirm'));
    const o = m.overlay.value;
    if (o?.kind !== 'confirm') throw new Error('no confirm');
    o.done(value);
    return o;
  }

  it('offers to keep my version when one tab is closed', async () => {
    const { store, nb, m, a } = await conflicted();
    const closing = m.closeTab(a);
    const o = await answer(m, 'save');
    expect(o.options.message).toMatch(/changed on disk/);
    expect(await closing).toBe(true);
    expect(nb.tabs.peek()).not.toContain(a);
    expect(new TextDecoder().decode((await store.read('a.md')).bytes)).toBe('mine one');
  });

  it('brings the conflicted tab to the front and says why when Save All can’t save it', async () => {
    const { nb, m, a, b } = await conflicted();
    b.state!.insert('more ');
    nb.activate(b);
    const asking = m.confirmDiscardAll();
    await answer(m, 'save');
    expect(await asking).toBe(false);
    expect(nb.active.peek()).toBe(a);
    expect(nb.notice.peek()?.text).toMatch(/“a.md” changed on disk/);
    expect(b.dirty.peek()).toBe(false);
  });
});

describe('ShellModel in the Library layout (PRD §6)', () => {
  it('renames, deletes and creates for the note in front and the folder shown, not an old tree selection', async () => {
    const store = new MemoryStore('N', { 'a/x.md': 'x', 'b/y.md': 'y' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false, layout: 'library' }) });
    const m = new ShellModel();
    m.session.set(nb);
    await nb.createNote('a', 'new'); // leaves the tree's selection on a/new.md
    nb.scope.value = 'folder:b';
    await nb.openFile('b/y.md'); // a card click
    const asked = () => {
      const o = m.overlay.value;
      const title = o?.kind === 'prompt' || o?.kind === 'confirm' ? o.options.title : null;
      const value = o?.kind === 'prompt' ? (o.options.value ?? '') : '';
      const label = o?.kind === 'prompt' ? (o.options.label ?? '') : '';
      m.close();
      return { title, value, label };
    };

    void m.menuCatalog.run('shell.rename');
    await vi.waitFor(() => expect(m.overlay.value).not.toBeNull());
    expect(asked()).toMatchObject({ title: 'Rename', value: 'y.md' });

    void m.menuCatalog.run('shell.delete');
    await vi.waitFor(() => expect(m.overlay.value).not.toBeNull());
    expect(asked().title).toBe('Delete “y.md”?');

    void m.menuCatalog.run('shell.newNote');
    await vi.waitFor(() => expect(m.overlay.value).not.toBeNull());
    expect(asked().label).toBe('Name (in b)');
    m.session.set(null);
  });
});

describe('ShellModel menu inputs', () => {
  it('changes when the find bar’s Replace row opens or closes, which Replace and Replace All follow', async () => {
    const store = new MemoryStore('N', { 'a.md': 'cat cat' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.session.set(nb);
    await nb.openFile('a.md');
    m.setFindQuery('cat');
    let runs = 0;
    const stop = effect(() => {
      void m.menuInputs.value;
      runs++;
    });
    const before = runs;
    m.replaceOpen.value = true;
    expect(runs).toBe(before + 1);
    expect(m.menuCatalog.isEnabled(m.menuCatalog.get('shell.replaceAll')!)).toBe(true);
    m.replaceOpen.value = false;
    expect(runs).toBe(before + 2);
    stop();
    m.session.set(null);
  });

  it('follows a tab being pinned', async () => {
    const store = new MemoryStore('N', { 'a.md': '' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const m = new ShellModel();
    m.session.set(nb);
    const a = (await nb.openFile('a.md'))!;
    let runs = 0;
    const stop = effect(() => {
      void m.menuInputs.value;
      runs++;
    });
    const before = runs;
    nb.setTabPinned(a, true);
    expect(runs).toBeGreaterThan(before);
    stop();
    m.session.set(null);
  });
});
