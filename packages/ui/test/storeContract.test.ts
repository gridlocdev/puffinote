// One contract for every NotebookStore: the shell relies on these answers whatever the folder is backed by.
// The desktop store runs over a stand-in for Electron's bridge (`fakeNativeBridge.ts`).
import { mkdir, mkdtemp, rm } from 'node:fs/promises';
import type { AddressInfo } from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { createServer, FileApi, parseRoots } from '@puffinote/server';
import {
  BrowserStore,
  ConflictError,
  MemoryStore,
  NasClient,
  NasStore,
  NativeStore,
  NotFoundError,
  type NotebookStore,
} from '../src/platform/index.ts';
import { fakeFolder } from './fakeFileSystem.ts';
import { fakeNativeBridge } from './fakeNativeBridge.ts';

const enc = (s: string) => new TextEncoder().encode(s);
const dec = (b: Uint8Array) => new TextDecoder().decode(b);

let nasDir: string;
let nasClient: NasClient;
let closeNas: () => void;
let nasCount = 0;

beforeAll(async () => {
  nasDir = await mkdtemp(path.join(os.tmpdir(), 'puffinote-contract-'));
  const server = createServer({
    api: new FileApi(parseRoots(`Notes=${nasDir}`, '/')),
    staticDir: null,
    token: null,
    maxBytes: 1 << 20,
  });
  await new Promise<void>((r) => server.listen(0, '127.0.0.1', r));
  nasClient = new NasClient(`http://127.0.0.1:${(server.address() as AddressInfo).port}/api`);
  closeNas = () => server.close();
});

afterAll(async () => {
  closeNas();
  await rm(nasDir, { recursive: true, force: true });
});

const stores: [string, () => Promise<NotebookStore>][] = [
  ['MemoryStore', async () => new MemoryStore('Notes')],
  [
    'BrowserStore',
    async () => new BrowserStore(fakeFolder('Notes').root as unknown as FileSystemDirectoryHandle),
  ],
  [
    'NasStore',
    async () => {
      const base = `nb${++nasCount}`;
      await mkdir(path.join(nasDir, base));
      return new NasStore(nasClient, 'notes', base);
    },
  ],
  [
    'NativeStore',
    async () => {
      const root = path.join(nasDir, `native${++nasCount}`);
      await mkdir(root);
      return new NativeStore(fakeNativeBridge(), root);
    },
  ],
];

describe.each(stores)('%s keeps the store contract', (_name, make) => {
  let store: NotebookStore;
  beforeEach(async () => {
    store = await make();
  });

  const text = async (p: string) => dec((await store.read(p)).bytes);
  const reason = (p: Promise<unknown>) =>
    p.then(
      () => 'no error',
      (e: unknown) => (e instanceof ConflictError ? e.reason : String(e)),
    );

  it('reads back what it wrote, with the version the write returned', async () => {
    const version = await store.write('a.md', enc('hello'));
    const read = await store.read('a.md');
    expect(dec(read.bytes)).toBe('hello');
    expect(read.version).toBe(version);
  });

  it('creates parent folders and lists files and folders with notebook paths', async () => {
    await store.write('deep/er/b.md', enc('b'));
    await store.write('top.md', enc('t'));
    const root = (await store.list('')).map((e) => [e.path, e.kind]).sort();
    expect(root).toEqual([
      ['deep', 'dir'],
      ['top.md', 'file'],
    ]);
    expect((await store.list('deep/er')).map((e) => [e.path, e.name, e.size])).toEqual([
      ['deep/er/b.md', 'b.md', 1],
    ]);
  });

  it('refuses a save over a version it did not read', async () => {
    const first = await store.write('a.md', enc('one'));
    await store.write('a.md', enc('two, from elsewhere'), { force: true });
    if (first === null) return; // a store that can't tell versions can't see the change
    expect(await reason(store.write('a.md', enc('mine'), { expected: first }))).toBe('changed');
    expect(await text('a.md')).toBe('two, from elsewhere');
  });

  it('does not bring back a file deleted since it was read', async () => {
    const version = await store.write('a.md', enc('one'));
    await store.remove('a.md');
    expect(['deleted', 'changed']).toContain(
      await reason(store.write('a.md', enc('mine'), { expected: version })),
    );
    await expect(store.read('a.md')).rejects.toBeInstanceOf(NotFoundError);
  });

  it('refuses to create over an existing file, and a forced save overwrites', async () => {
    await store.write('a.md', enc('one'));
    expect(await reason(store.write('a.md', enc('new'), { create: true }))).toBe('exists');
    await store.write('a.md', enc('forced'), { force: true });
    expect(await text('a.md')).toBe('forced');
  });

  it('throws NotFoundError for a missing file', async () => {
    await expect(store.read('nope.md')).rejects.toBeInstanceOf(NotFoundError);
  });

  it('moves files and folders, and refuses a taken name', async () => {
    await store.write('a.md', enc('a'));
    await store.write('f/b.md', enc('b'));
    await store.move('a.md', 'f/a2.md');
    expect(await text('f/a2.md')).toBe('a');
    await expect(store.read('a.md')).rejects.toBeInstanceOf(NotFoundError);
    await store.move('f', 'g');
    expect(await text('g/b.md')).toBe('b');
    expect(await reason(store.move('g/a2.md', 'g/b.md'))).toBe('exists');
    expect(await text('g/b.md')).toBe('b');
  });

  it('removes a file and a folder with everything in it, and makes empty folders', async () => {
    await store.write('a.md', enc('a'));
    await store.write('f/g/b.md', enc('b'));
    await store.mkdir('empty');
    await store.remove('a.md');
    await store.remove('f');
    expect((await store.list('')).map((e) => [e.path, e.kind])).toEqual([['empty', 'dir']]);
  });
});
