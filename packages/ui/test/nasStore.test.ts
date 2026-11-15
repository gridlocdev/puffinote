// The NAS client against the real server: the two halves of one HTTP contract.
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import type { AddressInfo } from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { createServer, FileApi, parseRoots } from '@puffinote/server';
import { ConflictError, NasClient, NasStore, NotFoundError } from '../src/platform/index.ts';

let dir: string;
let client: NasClient;
let close: () => void;
const enc = (s: string) => new TextEncoder().encode(s);
const dec = (b: Uint8Array) => new TextDecoder().decode(b);

beforeAll(async () => {
  dir = await mkdtemp(path.join(os.tmpdir(), 'puffinote-nas-'));
  await mkdir(path.join(dir, 'Work', 'Projects'), { recursive: true });
  await writeFile(path.join(dir, 'Work', 'Plan.md'), '# Plan\n');
  const server = createServer({
    api: new FileApi(parseRoots(`Notes=${dir}`, '/')),
    staticDir: null,
    token: null,
    maxBytes: 1 << 20,
  });
  await new Promise<void>((r) => server.listen(0, '127.0.0.1', r));
  client = new NasClient(`http://127.0.0.1:${(server.address() as AddressInfo).port}/api`);
  close = () => server.close();
});

afterAll(async () => {
  close();
  await rm(dir, { recursive: true, force: true });
});

describe('NasStore over the NAS server', () => {
  it('finds the server and its roots', async () => {
    expect(await client.info()).toEqual({ name: 'Puffinote', auth: false });
    expect(await client.roots()).toEqual([{ id: 'notes', name: 'Notes' }]);
    expect((await client.list('notes', '')).map((e) => e.name)).toEqual(['Work']);
  });

  it('lists, reads and saves with notebook-relative paths', async () => {
    const store = new NasStore(client, 'notes', 'Work');
    expect(store.name).toBe('Work');
    expect((await store.list('')).map((e) => [e.path, e.kind])).toEqual([
      ['Projects', 'dir'],
      ['Plan.md', 'file'],
    ]);
    const { bytes, version } = await store.read('Plan.md');
    expect(dec(bytes)).toBe('# Plan\n');
    const next = await store.write('Plan.md', enc('# Plan v2\n'), { expected: version });
    expect(next).not.toBe(version);
    expect(await readFile(path.join(dir, 'Work/Plan.md'), 'utf8')).toBe('# Plan v2\n');
    expect(store.fileUrl('Projects/x.png')).toContain('path=Work%2FProjects%2Fx.png');
  });

  it('turns HTTP failures into the store’s errors', async () => {
    const store = new NasStore(client, 'notes', 'Work');
    const { version } = await store.read('Plan.md');
    await writeFile(path.join(dir, 'Work/Plan.md'), 'changed elsewhere');
    await expect(store.write('Plan.md', enc('mine'), { expected: version })).rejects.toMatchObject({
      reason: 'changed',
    });
    await store.write('Plan.md', enc('mine'), { expected: version, force: true });
    await expect(store.write('Plan.md', enc('new'), { create: true })).rejects.toMatchObject({
      reason: 'exists',
    });
    await store.write('Projects/Roadmap.md', enc('# Roadmap\n'), { create: true });
    await expect(store.move('Plan.md', 'Projects/Roadmap.md')).rejects.toBeInstanceOf(ConflictError);
    await expect(store.read('Gone.md')).rejects.toBeInstanceOf(NotFoundError);
  });

  it('makes folders, moves and deletes', async () => {
    const store = new NasStore(client, 'notes', 'Work');
    await store.mkdir('Archive/2026');
    await store.move('Projects/Roadmap.md', 'Archive/2026/Roadmap.md');
    expect((await store.list('Archive/2026')).map((e) => e.path)).toEqual(['Archive/2026/Roadmap.md']);
    await store.remove('Archive');
    expect((await store.list('')).map((e) => e.name)).not.toContain('Archive');
  });
});
