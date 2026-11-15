import { signal } from '@preact/signals';
import { describe, expect, it, vi } from 'vitest';
import type { Notice } from '../src/notebook/notebook.ts';
import { NotebookPictures } from '../src/notebook/notebookPictures.ts';
import { NotebookTrash } from '../src/notebook/notebookTrash.ts';
import { defaultSettings } from '../src/notebook/settings.ts';
import { emptyTree, loadTree, type TreeNode } from '../src/notebook/tree.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';

const png = (n: number) => new Uint8Array([0x89, 0x50, 0x4e, 0x47, n]);

async function setup(files: Record<string, string | Uint8Array>) {
  const store = new MemoryStore('N', files);
  const notice = signal<Notice | null>(null);
  let tree: TreeNode = emptyTree;
  const reloadTree = async () => void (tree = await loadTree(store, true));
  const deleted: string[] = [];
  const trash = new NotebookTrash({
    store,
    closed: () => false,
    restored: async () => {},
    offer: (text, label, run) => (notice.value = { kind: 'info', text, action: { label, run } }),
    notify: (kind, text) => (notice.value = { kind, text }),
    report: (e) => (notice.value = { kind: 'error', text: String(e) }),
  });
  const pictures = new NotebookPictures({
    store,
    notice,
    trash,
    settings: () => defaultSettings,
    tree: () => tree,
    closed: () => false,
    noteText: async (path) => new TextDecoder().decode((await store.read(path)).bytes),
    reloadTree,
    deleteEntry: async (path) => {
      deleted.push(path);
      const item = await trash.take(path, 'file');
      await reloadTree();
      return item;
    },
    notify: (kind, text) => (notice.value = { kind, text }),
    report: (e) => (notice.value = { kind: 'error', text: String(e) }),
  });
  await reloadTree();
  return { store, notice, pictures, deleted };
}

describe('NotebookPictures', () => {
  it('saves pasted pictures in the picture folder, linking to one that is already there', async () => {
    const { store, pictures } = await setup({ 'a.md': '', 'attachments/old.png': png(1) });
    const links = await pictures.add('a.md', [
      { name: 'same.png', type: 'image/png', bytes: png(1) },
      { name: 'new.png', type: 'image/png', bytes: png(2) },
      { name: 'notes.txt', type: 'text/plain', bytes: png(3) },
    ]);
    expect(links).toHaveLength(2);
    expect(links[0]).toContain('attachments/old.png');
    expect(links[1]).toMatch(/attachments\/.+\.png/);
    expect((await store.list('attachments')).length).toBe(2);
  });

  it('reads a picture once for the editor, and again after it is forgotten', async () => {
    const { store, pictures } = await setup({ 'a.md': '', 'p.png': png(1) });
    const read = vi.spyOn(store, 'read');
    expect(pictures.resolve('a.md', 'https://x/y.png')).toBe('https://x/y.png');
    expect(pictures.resolve('a.md', 'p.png')).toBe('');
    await vi.waitFor(() => expect(pictures.loaded.value).toBe(1));
    expect(pictures.resolve('a.md', 'p.png')).toMatch(/^blob:/);
    expect(read).toHaveBeenCalledTimes(1);
    pictures.forget(['p.png']);
    expect(pictures.resolve('a.md', 'p.png')).toBe('');
    expect(read).toHaveBeenCalledTimes(2);
  });

  it('finds the pictures no note shows, and the notes that show one', async () => {
    const { pictures } = await setup({
      'a.md': '![](p.png)',
      'b.md': '![](p.png) ![](q.png)',
      'p.png': png(1),
      'q.png': png(2),
      'r.png': png(3),
    });
    expect(await pictures.unused()).toEqual(['r.png']);
    expect(await pictures.notesShowing('p.png', 'a.md')).toEqual(['b.md']);
    expect(await pictures.notesShowingPicturesIn('q.png')).toEqual(['b.md']);
    expect(pictures.shows('![](p.png)', 'a.md', 'p.png')).toBe(true);
  });

  it('offers to delete a picture a save stopped showing, and withdraws when a save shows it again', async () => {
    const { store, notice, pictures, deleted } = await setup({ 'a.md': '', 'p.png': png(1) });
    pictures.saved('a.md', '![](p.png)', '');
    await vi.waitFor(() => expect(notice.value?.text).toBe('No note shows “p.png” any more.'));
    pictures.saved('a.md', '', '![](p.png)');
    expect(notice.value).toBeNull();

    pictures.saved('a.md', '![](p.png)', '');
    await vi.waitFor(() => expect(notice.value?.action?.label).toBe('Move to Trash'));
    notice.value!.action!.run();
    await vi.waitFor(() => expect(deleted).toEqual(['p.png']));
    await expect(store.read('p.png')).rejects.toThrow();
    expect(notice.value?.action?.label).toBe('Undo');
  });

  it('keeps a picture a note shows again by the time it would be deleted', async () => {
    const { notice, pictures, deleted } = await setup({ 'a.md': '![](p.png)', 'p.png': png(1) });
    await pictures.removeUnused(['p.png']);
    expect(deleted).toEqual([]);
    expect(notice.value?.text).toBe('Kept “p.png”: a note shows it again.');
  });
});
