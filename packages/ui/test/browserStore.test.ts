import { describe, expect, it, vi } from 'vitest';
import { BrowserStore } from '../src/platform/browserStore.ts';
import { fakeFolder } from './fakeFileSystem.ts';
import { ConflictError } from '../src/platform/notebookStore.ts';
import { listTrash, moveToTrash, restoreFromTrash } from '../src/notebook/trash.ts';
import { Notebook } from '../src/notebook/notebook.ts';
import { searchCachePath } from '../src/notebook/search.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';

async function storeWith(files: Record<string, string>, caseInsensitive: boolean) {
  const { root, fs } = fakeFolder('Notes', caseInsensitive);
  const store = new BrowserStore(root as unknown as FileSystemDirectoryHandle);
  for (const [path, text] of Object.entries(files)) await store.write(path, new TextEncoder().encode(text));
  return { store, fs };
}

const text = async (store: BrowserStore, path: string) =>
  new TextDecoder().decode((await store.read(path)).bytes);
const names = async (store: BrowserStore, dir = '') => (await store.list(dir)).map((e) => e.name).sort();

describe('BrowserStore move', () => {
  it('renames by case only on a case-insensitive disk without losing the file', async () => {
    const { store } = await storeWith({ 'notes.md': 'keep me', 'f/a.md': 'a' }, true);
    await store.move('notes.md', 'Notes.md');
    expect(await names(store)).toEqual(['Notes.md', 'f']);
    expect(await text(store, 'Notes.md')).toBe('keep me');
    await store.move('f', 'F');
    expect(await names(store)).toEqual(['F', 'Notes.md']);
    expect(await text(store, 'F/a.md')).toBe('a');
  });

  it('refuses a name another entry has, in any case on a case-insensitive disk', async () => {
    const { store } = await storeWith({ 'a.md': 'a', 'B.md': 'b' }, true);
    await expect(store.move('a.md', 'b.md')).rejects.toBeInstanceOf(ConflictError);
    expect(await text(store, 'a.md')).toBe('a');
    expect(await text(store, 'B.md')).toBe('b');
  });

  it('treats a case-only rename as a plain move on a case-sensitive disk', async () => {
    const { store } = await storeWith({ 'notes.md': 'x' }, false);
    await store.move('notes.md', 'Notes.md');
    expect(await names(store)).toEqual(['Notes.md']);
  });

  it('keeps the original and drops the partial copy when the copy fails', async () => {
    const { store, fs } = await storeWith({ 'f/a.md': 'a', 'f/b.md': 'b' }, false);
    fs.failWrites.add('b.md');
    await expect(store.move('f', 'g')).rejects.toThrow('disk full');
    expect(await names(store)).toEqual(['f']);
    expect(await text(store, 'f/a.md')).toBe('a');
    expect(await text(store, 'f/b.md')).toBe('b');
  });
});

describe('BrowserStore write', () => {
  it('refuses to recreate a file deleted since it was read, unless forced', async () => {
    const { store } = await storeWith({ 'a.md': 'a' }, false);
    const { version } = await store.read('a.md');
    await store.remove('a.md');
    const bytes = new TextEncoder().encode('again');
    await expect(store.write('a.md', bytes, { expected: version })).rejects.toMatchObject({
      reason: 'deleted',
    });
    await store.write('a.md', bytes, { expected: version, force: true });
    expect(await text(store, 'a.md')).toBe('again');
  });
  it('doesn’t bring back the folder of a note deleted since it was read', async () => {
    const { store } = await storeWith({ 'f/a.md': 'a' }, false);
    const { version } = await store.read('f/a.md');
    await store.remove('f');
    const bytes = new TextEncoder().encode('again');
    await expect(store.write('f/a.md', bytes, { expected: version })).rejects.toMatchObject({
      reason: 'deleted',
    });
    expect(await names(store)).toEqual([]);
    await store.write('f/a.md', bytes, { expected: version, force: true });
    expect(await text(store, 'f/a.md')).toBe('again');
  });
});

describe('BrowserStore trash (PRD §6.1)', () => {
  it('moves a folder into the notebook’s Trash and back, under a free name when it is taken', async () => {
    const { store } = await storeWith({ 'f/a.md': 'one', 'f/sub/b.md': 'two' }, true);
    const item = await moveToTrash(store, 'f', 'dir', 5);
    expect((await store.list('')).map((e) => e.name)).toEqual(['.puffinote']);
    expect(new TextDecoder().decode((await store.read('.puffinote/trash/5/f/sub/b.md')).bytes)).toBe('two');
    expect(await listTrash(store)).toEqual([item]);
    await store.mkdir('F'); // the same name to this disk
    expect(await restoreFromTrash(store, item)).toBe('f 2');
    expect(new TextDecoder().decode((await store.read('f 2/a.md')).bytes)).toBe('one');
    expect(await listTrash(store)).toEqual([]);
  });
});

describe('search index on a browser folder', () => {
  const openNotebook = (store: BrowserStore) =>
    Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
  const notesRead = (read: { mock: { calls: unknown[][] } }) =>
    read.mock.calls.map(([p]) => p as string).filter((p) => p.endsWith('.md'));
  const found = async (nb: Notebook, query: string) =>
    [...new Set((await nb.textSearch.find(query)).map((h) => h.path))].sort();

  it('saves the index in the folder, and a reopened notebook reads only the notes that changed', async () => {
    const { store } = await storeWith(
      { 'a.md': 'apple pie', 'f/b.md': 'banana bread', 'c.md': 'cherry' },
      true,
    );
    const first = await openNotebook(store);
    expect(await found(first, 'apple')).toEqual(['a.md']);
    await first.textSearch.save();
    first.dispose();
    const saved = JSON.parse(await text(store, searchCachePath)) as { notes: [string][] };
    expect(saved.notes.map(([p]) => p).sort()).toEqual(['a.md', 'c.md', 'f/b.md']);
    expect(await text(store, '.puffinote/cache/.gitignore')).toBe('*\n');

    // While it was closed: one note edited, one deleted, one added.
    await store.write('f/b.md', new TextEncoder().encode('blueberry bread'));
    await store.remove('c.md');
    await store.write('d.md', new TextEncoder().encode('date bread'));

    const again = await openNotebook(store);
    const read = vi.spyOn(store, 'read');
    expect(await found(again, 'bread')).toEqual(['d.md', 'f/b.md']);
    expect(notesRead(read).sort()).toEqual(['d.md', 'f/b.md']);
    expect(await found(again, 'banana')).toEqual([]);
    expect(await found(again, 'cherry')).toEqual([]);
    expect(notesRead(read).sort()).toEqual(['d.md', 'f/b.md']);
    again.dispose();
  });
});
