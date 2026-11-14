import { describe, expect, it } from 'vitest';
import { BrowserStore } from '../src/platform/browserStore.ts';
import { ConflictError } from '../src/platform/notebookStore.ts';
import { listTrash, moveToTrash, restoreFromTrash } from '../src/notebook/trash.ts';

/**
 * Just enough of the File System Access API, in memory. `caseInsensitive` resolves names the way macOS and
 * Windows disks do: "Notes.md" finds "notes.md". `failWrites` names files whose writes fail.
 */
let clock = 1;

class FakeFile {
  readonly kind = 'file';
  bytes = new Uint8Array();
  lastModified = clock++;
  readonly name: string;
  private readonly fs: { failWrites: Set<string> };
  constructor(name: string, fs: { failWrites: Set<string> }) {
    this.name = name;
    this.fs = fs;
  }
  isSameEntry(other: unknown): Promise<boolean> {
    return Promise.resolve(other === this);
  }
  getFile() {
    const bytes = this.bytes;
    return Promise.resolve({
      size: bytes.length,
      lastModified: this.lastModified,
      arrayBuffer: () => Promise.resolve(bytes.slice().buffer),
    });
  }
  createWritable() {
    let next = this.bytes;
    return Promise.resolve({
      write: (b: Uint8Array) => {
        if (this.fs.failWrites.has(this.name)) return Promise.reject(new Error('disk full'));
        next = b.slice();
        return Promise.resolve();
      },
      close: () => {
        this.bytes = next;
        this.lastModified = clock++;
        return Promise.resolve();
      },
    });
  }
}

class FakeDir {
  readonly kind = 'directory';
  private readonly children = new Map<string, FakeFile | FakeDir>();
  readonly name: string;
  private readonly fs: { caseInsensitive: boolean; failWrites: Set<string> };
  constructor(name: string, fs: { caseInsensitive: boolean; failWrites: Set<string> }) {
    this.name = name;
    this.fs = fs;
  }
  private key(name: string) {
    return this.fs.caseInsensitive ? name.toLowerCase() : name;
  }
  isSameEntry(other: unknown): Promise<boolean> {
    return Promise.resolve(other === this);
  }
  getDirectoryHandle(name: string, opts: { create?: boolean } = {}): Promise<FakeDir> {
    const found = this.children.get(this.key(name));
    if (found instanceof FakeDir) return Promise.resolve(found);
    if (found) return Promise.reject(new DOMException('file', 'TypeMismatchError'));
    if (!opts.create) return Promise.reject(new DOMException('missing', 'NotFoundError'));
    const d = new FakeDir(name, this.fs);
    this.children.set(this.key(name), d);
    return Promise.resolve(d);
  }
  getFileHandle(name: string, opts: { create?: boolean } = {}): Promise<FakeFile> {
    const found = this.children.get(this.key(name));
    if (found instanceof FakeFile) return Promise.resolve(found);
    if (found) return Promise.reject(new DOMException('dir', 'TypeMismatchError'));
    if (!opts.create) return Promise.reject(new DOMException('missing', 'NotFoundError'));
    const f = new FakeFile(name, this.fs);
    this.children.set(this.key(name), f);
    return Promise.resolve(f);
  }
  removeEntry(name: string): Promise<void> {
    if (!this.children.delete(this.key(name)))
      return Promise.reject(new DOMException('missing', 'NotFoundError'));
    return Promise.resolve();
  }
  async *entries(): AsyncGenerator<[string, FakeFile | FakeDir]> {
    for (const c of [...this.children.values()]) yield [c.name, c];
  }
  async *keys(): AsyncGenerator<string> {
    for (const c of [...this.children.values()]) yield c.name;
  }
}

async function storeWith(files: Record<string, string>, caseInsensitive: boolean) {
  const fs = { caseInsensitive, failWrites: new Set<string>() };
  const root = new FakeDir('Notes', fs);
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
