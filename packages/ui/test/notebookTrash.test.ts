import { describe, expect, it, vi } from 'vitest';
import { NotebookTrash } from '../src/notebook/notebookTrash.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';

function setup(store: MemoryStore) {
  const said: string[] = [];
  const offers: { text: string; label: string; run: () => void }[] = [];
  const restored: string[][] = [];
  const trash = new NotebookTrash({
    store,
    closed: () => false,
    restored: async (paths) => void restored.push([...paths]),
    offer: (text, label, run) => offers.push({ text, label, run }),
    notify: (_kind, text) => said.push(text),
    report: (e) => said.push(String(e)),
  });
  return { trash, said, offers, restored };
}

describe('NotebookTrash', () => {
  it('takes a file into the notebook’s Trash and puts it back', async () => {
    const store = new MemoryStore('N', { 'a.md': 'hi' });
    const { trash, said, restored } = setup(store);
    expect(trash.available).toBe(true);
    const item = (await trash.take('a.md', 'file'))!;
    expect(item).toMatchObject({ path: 'a.md', kind: 'file' });
    await expect(store.read('a.md')).rejects.toThrow();
    expect(await trash.items()).toEqual([item]);
    expect(await trash.restore([item])).toEqual(['a.md']);
    expect(restored).toEqual([['a.md']]);
    expect(said).toEqual(['Restored “a.md”.']);
    expect(await trash.items()).toEqual([]);
  });

  it('offers Undo, which restores the items', async () => {
    const store = new MemoryStore('N', { 'a.md': 'hi' });
    const { trash, offers } = setup(store);
    const item = (await trash.take('a.md', 'file'))!;
    trash.offerUndo([item], 'Gone');
    expect(offers.map((o) => [o.text, o.label])).toEqual([['Gone', 'Undo']]);
    offers[0]!.run();
    await vi.waitFor(async () => expect((await store.read('a.md')).bytes.length).toBe(2));
  });

  it('empties the Trash for good', async () => {
    const store = new MemoryStore('N', { 'a.md': 'hi' });
    const { trash, said } = setup(store);
    await trash.take('a.md', 'file');
    expect(await trash.empty()).toBe(true);
    expect(await trash.items()).toEqual([]);
    expect(said).toEqual(['The notebook’s Trash is empty.']);
  });

  it('removes straight away where the system keeps the Trash', async () => {
    const store = new MemoryStore('N', { 'a.md': 'hi' });
    Object.defineProperty(store, 'kind', { value: 'native' });
    const { trash } = setup(store);
    expect(trash.available).toBe(false);
    expect(await trash.take('a.md', 'file')).toBeNull();
    await expect(store.read('a.md')).rejects.toThrow();
    expect(await trash.items()).toEqual([]);
    expect(await trash.empty()).toBe(false);
  });
});
