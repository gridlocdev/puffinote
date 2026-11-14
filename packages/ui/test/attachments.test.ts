import { describe, expect, it, vi } from 'vitest';
import {
  applyRefEdits,
  attachmentDir,
  attachmentName,
  cleanFolder,
  fileRefs,
  imageTargets,
  relativePath,
  relinkEdits,
} from '../src/notebook/attachments.ts';
import { Notebook } from '../src/notebook/notebook.ts';
import { defaultSettings, memoryPrefs, resolveSettings } from '../src/notebook/settings.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';

const text = async (store: MemoryStore, path: string) =>
  new TextDecoder().decode((await store.read(path)).bytes);

async function open(files: Record<string, string>, prefs = {}) {
  const store = new MemoryStore('Notes', files);
  const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false, ...prefs }) });
  return { store, nb };
}

const png = (name = 'image.png') => ({ name, type: 'image/png', bytes: new Uint8Array([137, 80, 78, 71]) });

describe('picture links', () => {
  it('finds file links and pictures, not web links or code', () => {
    const note = '![a](x.png) [b](<y z.md>) [w](https://e.com/p.png)\n```\n![c](q.png)\n```\n';
    expect(fileRefs(note).map((r) => [r.url, r.angle])).toEqual([
      ['x.png', false],
      ['y z.md', true],
    ]);
    expect([...imageTargets('![](../att/a%20b.png) [n](n.md)', 'dir/note.md')]).toEqual(['att/a b.png']);
  });

  it('writes paths relative to the note', () => {
    expect(relativePath('note.md', 'attachments/a.png')).toBe('attachments/a.png');
    expect(relativePath('a/b/note.md', 'attachments/a.png')).toBe('../../attachments/a.png');
    expect(relativePath('a/b/note.md', 'a/c/x.png')).toBe('../c/x.png');
    expect(relativePath('a/note.md', 'a/x.png')).toBe('x.png');
  });

  it('relinks a moved note to pictures that stayed, keeping the link style', () => {
    const note = '![](attachments/a%20b.png) ![](<attachments/c d.png>) ![](x.png#frag) [n](other.md)';
    const edits = relinkEdits(note, 'note.md', 'deep/er/note.md', (p) => p);
    expect(applyRefEdits(note, edits)).toBe(
      '![](../../attachments/a%20b.png) ![](<../../attachments/c d.png>) ![](../../x.png#frag) [n](other.md)',
    );
  });

  it('relinks a note to a picture that moved, and leaves links that still resolve', () => {
    const note = '![](pics/a.png) ![](b.png)';
    const moved = (p: string) => (p === 'pics/a.png' ? 'art/a.png' : p);
    expect(applyRefEdits(note, relinkEdits(note, 'n.md', 'n.md', moved))).toBe('![](art/a.png) ![](b.png)');
    expect(relinkEdits(note, 'n.md', 'n.md', (p) => p)).toEqual([]);
  });
});

describe('picture folder and names', () => {
  it('uses the notebook folder, or one beside each note', () => {
    expect(attachmentDir(defaultSettings, 'a/note.md')).toBe('attachments');
    const custom = resolveSettings({ attachmentFolder: '/assets//img/' });
    expect(attachmentDir(custom, 'a/note.md')).toBe('assets/img');
    const perNote = resolveSettings({ attachmentLocation: 'note' });
    expect(attachmentDir(perNote, 'a/Meeting.md')).toBe('a/Meeting.assets');
    expect(cleanFolder('../x/./y')).toBe('x/y');
  });

  it('drops a blank folder name from the settings', () => {
    expect(resolveSettings({ attachmentFolder: '  ' }).attachmentFolder).toBe('attachments');
  });

  it('names a clipboard picture after the time and keeps a real file name', () => {
    const at = new Date(2026, 10, 10, 14, 3, 2);
    expect(attachmentName('image.png', 'image/png', at)).toBe('Pasted image 2026-11-10 14.03.02.png');
    expect(attachmentName('', 'image/jpeg', at)).toBe('Pasted image 2026-11-10 14.03.02.jpg');
    expect(attachmentName('Holiday.JPG', 'image/jpeg', at)).toBe('Holiday.JPG');
  });
});

describe('Notebook pictures', () => {
  it('saves pasted pictures in the folder and returns their links', async () => {
    const { store, nb } = await open({ 'a/note.md': '', 'attachments/Holiday.png': '' });
    const links = await nb.addPictures('a/note.md', [
      png('Holiday.png'),
      { name: 'x.txt', type: 'text/plain', bytes: new Uint8Array() },
    ]);
    expect(links).toEqual(['![](../attachments/Holiday%202.png)']);
    expect((await store.read('attachments/Holiday 2.png')).bytes).toHaveLength(4);
  });

  it('links the same picture pasted again to the file already there', async () => {
    const { store, nb } = await open({ 'a.md': '', 'b.md': '' });
    const [first] = await nb.addPictures('a.md', [png('Photo.png')]);
    const [again] = await nb.addPictures('b.md', [png('image.png')]);
    expect(again).toBe(first);
    expect((await store.list('attachments')).map((e) => e.name)).toEqual(['Photo.png']);
    const other = { ...png('Photo.png'), bytes: new Uint8Array([1, 2, 3, 4]) };
    expect(await nb.addPictures('b.md', [other])).toEqual(['![](attachments/Photo%202.png)']);
  });

  it('saves beside the note when set to', async () => {
    const { nb } = await open({ 'note.md': '' }, { attachmentLocation: 'note' });
    const [link] = await nb.addPictures('note.md', [png('p.png')]);
    expect(link).toBe('![](note.assets/p.png)');
  });

  it('keeps a moved note’s pictures where they are and fixes its links', async () => {
    const { store, nb } = await open({
      'note.md': '![](attachments/a.png)',
      'attachments/a.png': '',
      'sub/x.md': '',
    });
    await nb.move('note.md', 'sub');
    expect(await text(store, 'sub/note.md')).toBe('![](../attachments/a.png)');
    expect(nb.tree.value.children.map((c) => c.name)).toContain('attachments');
  });

  it('fixes every note’s links when a picture moves, open tabs included', async () => {
    const { store, nb } = await open({
      'one.md': '![](attachments/a.png)',
      'deep/two.md': 'x ![](../attachments/a.png) y',
      'attachments/a.png': '',
      'pics/keep.txt': '',
    });
    const tab = (await nb.openFile('deep/two.md'))!;
    tab.state!.select(1);
    await nb.move('attachments/a.png', 'pics');
    expect(await text(store, 'one.md')).toBe('![](pics/a.png)');
    expect(tab.state!.text).toBe('x ![](../pics/a.png) y');
    expect(tab.dirty.value).toBe(false); // it was clean, so it saved
    expect(await text(store, 'deep/two.md')).toBe('x ![](../pics/a.png) y');
    expect(tab.state!.selection.value.caret).toBe(1);
  });

  it('offers to delete a picture when a save drops its last link', async () => {
    const { store, nb } = await open({
      'one.md': '![](a.png) ![](b.png)',
      'two.md': '![](b.png)',
      'a.png': '',
      'b.png': '',
    });
    const tab = (await nb.openFile('one.md'))!;
    tab.state!.select(0, tab.state!.text.length);
    tab.state!.insert('');
    await nb.save(tab);
    await vi.waitFor(() => expect(nb.notice.value?.action).toBeDefined());
    expect(nb.notice.value!.text).toBe('No note shows “a.png” any more.');
    nb.notice.value!.action!.run();
    await vi.waitFor(() => expect(store.read('a.png')).rejects.toThrow());
    await store.read('b.png'); // two.md still shows it
  });

  it('keeps it quietly when set to', async () => {
    const { nb } = await open({ 'one.md': '![](a.png)', 'a.png': '' }, { unusedImages: 'keep' });
    const tab = (await nb.openFile('one.md'))!;
    tab.state!.select(0, 10);
    tab.state!.insert('');
    await nb.save(tab);
    await new Promise((r) => setTimeout(r, 10));
    expect(nb.notice.value).toBeNull();
  });

  it('offers to delete the pictures of a deleted note', async () => {
    const { nb } = await open({ 'one.md': '![](a.png)', 'a.png': '' });
    await nb.remove('one.md');
    await vi.waitFor(() => expect(nb.notice.value?.action).toBeDefined());
  });

  it('lists the pictures no note links to', async () => {
    const { nb } = await open({
      'one.md': '![](a.png) [file](docs/b.png)',
      'a.png': '',
      'docs/b.png': '',
      'docs/c.png': '',
      'z.jpg': '',
    });
    expect(await nb.unusedPictures()).toEqual(['docs/c.png', 'z.jpg']);
  });
});

describe('Find Unused Pictures safety', () => {
  it('counts links in hidden folders, whatever Show Hidden Files says', async () => {
    const { nb } = await open({ '.drafts/one.md': '![](../a.png)', 'a.png': '', 'b.png': '' });
    expect(await nb.unusedPictures()).toEqual(['b.png']);
  });

  it('stops with an error when a note can’t be read', async () => {
    const store = new MemoryStore('Notes', { 'one.md': '![](a.png)', 'two.md': '', 'a.png': '' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const read = store.read.bind(store);
    store.read = (p: string) => (p === 'one.md' ? Promise.reject(new Error('offline')) : read(p));
    await expect(nb.unusedPictures()).rejects.toThrow(/one\.md/);
  });

  it('makes no offer when a note can’t be read', async () => {
    const store = new MemoryStore('Notes', { 'one.md': '![](a.png)', 'two.md': '![](a.png)', 'a.png': '' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const read = store.read.bind(store);
    store.read = (p: string) => (p === 'two.md' ? Promise.reject(new Error('offline')) : read(p));
    await nb.remove('one.md');
    await new Promise((r) => setTimeout(r, 10));
    expect(nb.notice.value?.action).toBeUndefined();
  });
});

describe('pictures shown by wiki embeds and <img>', () => {
  it('counts ![[…]] embeds and <img src> as used', async () => {
    const { nb } = await open({
      'notes/one.md': 'x ![[a.png|300]] y\n\n<img src="../pics/b.png" width=50>',
      'two.md': '![[deep/c.png]]\n\n```\n![[d.png]]\n```',
      'attachments/a.png': '',
      'pics/b.png': '',
      'x/deep/c.png': '',
      'd.png': '',
      'e.png': '',
    });
    expect(await nb.unusedPictures()).toEqual(['d.png', 'e.png']);
  });

  it('keeps a picture an embed shows out of the no-longer-used offer and lists the note', async () => {
    const { nb } = await open({ 'one.md': '![](a.png)', 'two.md': '![[a.png]]', 'a.png': '' });
    expect(await nb.notesShowing('a.png', 'one.md')).toEqual(['two.md']);
    await nb.remove('one.md');
    await new Promise((r) => setTimeout(r, 10));
    expect(nb.notice.value?.action).toBeUndefined();
  });
});

describe('picture names compared as the disk compares them', () => {
  it('matches a link to a picture whatever its Unicode form', async () => {
    const { nb } = await open({ 'one.md': '![](café.png)', 'café.png': '' });
    expect(await nb.unusedPictures()).toEqual([]);
  });

  it('ignores case where the store says names do', async () => {
    class Folded extends MemoryStore {
      override readonly capabilities = { ...new MemoryStore().capabilities, ignoresCase: true };
    }
    const store = new Folded('N', { 'one.md': '![](Photo.PNG)', 'photo.png': '' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    expect(await nb.unusedPictures()).toEqual([]);
    expect(await nb.notesShowing('photo.png', '')).toEqual(['one.md']);
  });

  it('keeps case apart where the store says names don’t ignore it', async () => {
    const { nb } = await open({ 'one.md': '![](Photo.png)', 'photo.png': '' });
    expect(await nb.unusedPictures()).toEqual(['photo.png']);
  });
});
