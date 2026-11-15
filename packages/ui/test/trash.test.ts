import { describe, expect, it, vi } from 'vitest';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import { Notebook } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { listTrash, trashDir } from '../src/notebook/trash.ts';

const text = async (store: MemoryStore, path: string) =>
  new TextDecoder().decode((await store.read(path)).bytes);

async function open(files: Record<string, string>) {
  const store = new MemoryStore('Notes', files);
  const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
  return { store, nb };
}

describe('the notebook’s Trash (PRD §6.1)', () => {
  it('moves a deleted note into .puffinote/trash, hidden, with Undo', async () => {
    const { store, nb } = await open({ 'work/a.md': 'hello' });
    await nb.openFile('work/a.md');
    expect(await nb.remove('work/a.md')).toBe(true);
    expect(nb.tabs.value).toHaveLength(0);
    expect(nb.tree.value.children.map((c) => c.name)).toEqual(['work']);
    const [item] = await nb.trash.items();
    expect(item).toMatchObject({ path: 'work/a.md', kind: 'file' });
    expect(await text(store, `${trashDir}/${item!.id}/work/a.md`)).toBe('hello');
    expect(await text(store, `${trashDir}/.gitignore`)).toBe('*\n');
    expect(nb.notice.value?.text).toBe('Moved “a.md” to the notebook’s Trash.');
    nb.notice.value!.action!.run();
    await vi.waitFor(async () => expect(await text(store, 'work/a.md')).toBe('hello'));
    expect(await nb.trash.items()).toEqual([]);
    expect(nb.notice.value?.text).toBe('Restored “a.md”.');
  });

  it('restores under a free name and never overwrites', async () => {
    const { store, nb } = await open({ 'a.md': 'old' });
    await nb.remove('a.md');
    await store.write('a.md', new TextEncoder().encode('new'), { force: true });
    expect(await nb.trash.restore(await nb.trash.items())).toEqual(['a 2.md']);
    expect(await text(store, 'a.md')).toBe('new');
    expect(await text(store, 'a 2.md')).toBe('old');
    expect(nb.notice.value?.text).toBe('Restored “a.md” as “a 2.md”: the name was taken.');
  });

  it('restores a whole folder into a folder that is gone', async () => {
    const { store, nb } = await open({ 'p/q/a.md': '1', 'p/q/b.png': '' });
    await nb.remove('p/q');
    await nb.remove('p');
    const items = await nb.trash.items();
    expect(items.map((i) => i.path).sort()).toEqual(['p', 'p/q']);
    const q = items.find((i) => i.path === 'p/q')!;
    expect(await nb.trash.restore([q])).toEqual(['p/q']);
    expect(await text(store, 'p/q/a.md')).toBe('1');
  });

  it('keeps trashed notes and pictures out of search and Find Unused Pictures', async () => {
    const { nb } = await open({ 'gone.md': 'needle ![](x.png)', 'x.png': '', 'kept.md': 'hay' });
    await nb.remove('gone.md');
    await nb.remove('x.png');
    expect(await nb.textSearch.find('needle')).toEqual([]);
    expect(await nb.unusedPictures()).toEqual([]);
  });

  it('empties the Trash for good', async () => {
    const { store, nb } = await open({ 'a.md': '', 'b.md': '' });
    await nb.remove('a.md');
    await nb.remove('b.md');
    expect(await listTrash(store)).toHaveLength(2);
    expect(await nb.trash.empty()).toBe(true);
    expect(await nb.trash.items()).toEqual([]);
    await expect(store.read(`${trashDir}/.gitignore`)).rejects.toThrow();
  });

  it('offers Undo for pictures moved to the Trash from Find Unused Pictures', async () => {
    const { store, nb } = await open({ 'a.png': 'A', 'b.png': 'B' });
    await nb.removeUnused(['a.png', 'b.png']);
    expect(nb.notice.value?.text).toBe('Moved 2 pictures to the notebook’s Trash.');
    nb.notice.value!.action!.run();
    await vi.waitFor(async () => expect(await text(store, 'b.png')).toBe('B'));
    expect(await text(store, 'a.png')).toBe('A');
  });
});
