import { link, mkdtemp, mkdir, readFile, rm, stat, symlink, writeFile } from 'node:fs/promises';
import type { AddressInfo } from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { etagOf, FileApi, parseRoots } from '../src/fileApi.ts';
import { createServer, hostAllowed, parseHosts } from '../src/server.ts';

let dir: string;
let outside: string;
let base: string;
let close: () => void;

beforeAll(async () => {
  dir = await mkdtemp(path.join(os.tmpdir(), 'puffinote-root-'));
  outside = await mkdtemp(path.join(os.tmpdir(), 'puffinote-outside-'));
  await mkdir(path.join(dir, 'Notes', '.puffinote'), { recursive: true });
  await writeFile(path.join(dir, 'Notes', 'a.md'), '# A\n');
  await writeFile(path.join(dir, 'Notes', 'pic.svg'), '<svg xmlns="http://www.w3.org/2000/svg"/>');
  await writeFile(path.join(dir, 'Notes', '.puffinote', 'session.json'), '{}');
  await writeFile(path.join(dir, 'Notes', '.a.md.0123456789ab.tmp'), 'in flight');
  await writeFile(path.join(outside, 'secret.md'), 'nope');
  await symlink(outside, path.join(dir, 'escape'));
  const api = new FileApi(parseRoots(`My Notes=${dir}`, '/'));
  const server = createServer({ api, staticDir: null, token: 't0k', maxBytes: 1024 });
  await new Promise<void>((r) => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${(server.address() as AddressInfo).port}/api`;
  close = () => server.close();
});

afterAll(async () => {
  close();
  await rm(dir, { recursive: true, force: true });
  await rm(outside, { recursive: true, force: true });
});

const auth = { Authorization: 'Bearer t0k' };
const q = (params: Record<string, string>) => new URLSearchParams({ root: 'my-notes', ...params });

describe('configuration', () => {
  it('slugs root names into ids and rejects malformed entries', () => {
    expect(parseRoots('My Notes=/a;Shared=/b', '/').map((r) => r.id)).toEqual(['my-notes', 'shared']);
    expect(() => parseRoots('nopath', '/')).toThrow();
  });

  it('answers IP literals, localhost and listed names only', () => {
    const extra = parseHosts(' NAS.lan:8790, notes.example.com');
    expect(extra).toEqual(['nas.lan', 'notes.example.com']);
    expect(hostAllowed('192.168.1.4:8790', [])).toBe(true);
    expect(hostAllowed('[::1]:8790', [])).toBe(true);
    expect(hostAllowed('localhost:8790', [])).toBe(true);
    expect(hostAllowed('nas.lan', extra)).toBe(true);
    expect(hostAllowed('evil.example', extra)).toBe(false);
    expect(hostAllowed(undefined, extra)).toBe(false);
  });
});

describe('the file API', () => {
  it('needs the token everywhere but /info', async () => {
    expect((await fetch(`${base}/roots`)).status).toBe(401);
    const info = await fetch(`${base}/info`);
    expect(await info.json()).toEqual({ name: 'Puffinote', auth: true });
  });

  it('lists every file, dotfiles too, but not its own temp files', async () => {
    const res = await fetch(`${base}/list?${q({ path: 'Notes' })}`, { headers: auth });
    const entries = (await res.json()) as { name: string; path: string; kind: string }[];
    expect(entries.map((e) => e.name)).toEqual(['.puffinote', 'a.md', 'pic.svg']);
    expect(entries[1]).toMatchObject({ path: 'Notes/a.md', kind: 'file' });
  });

  it('refuses paths that leave the root', async () => {
    // `..` is clamped to the root, so it names a file that isn't there.
    const up = await fetch(`${base}/file?${q({ path: `../${path.basename(outside)}/secret.md` })}`, {
      headers: auth,
    });
    expect(up.status).toBe(404);
    const linked = await fetch(`${base}/file?${q({ path: 'escape/secret.md' })}`, { headers: auth });
    expect(linked.status).toBe(403);
    const listed = await fetch(`${base}/list?${q({ path: '' })}`, { headers: auth });
    expect(((await listed.json()) as { name: string }[]).map((e) => e.name)).not.toContain('escape');
  });

  it('serves pictures as pictures, sandboxed, and everything else as bytes', async () => {
    const svg = await fetch(`${base}/file?${q({ path: 'Notes/pic.svg' })}`, { headers: auth });
    expect(svg.headers.get('content-type')).toBe('image/svg+xml');
    expect(svg.headers.get('content-security-policy')).toContain('sandbox');
    const md = await fetch(`${base}/file?${q({ path: 'Notes/a.md' })}`, { headers: auth });
    expect(md.headers.get('content-type')).toBe('application/octet-stream');
    expect(md.headers.get('etag')).toBe(etagOf(Buffer.from('# A\n')));
  });

  it('saves only over the version the client read', async () => {
    const url = `${base}/file?${q({ path: 'Notes/a.md' })}`;
    const etag = (await fetch(url, { headers: auth })).headers.get('etag')!;
    const first = await fetch(url, { method: 'PUT', body: '# A2\n', headers: { ...auth, 'If-Match': etag } });
    expect(first.status).toBe(200);
    const stale = await fetch(url, { method: 'PUT', body: '# A3\n', headers: { ...auth, 'If-Match': etag } });
    expect(stale.status).toBe(412);
    expect(await readFile(path.join(dir, 'Notes/a.md'), 'utf8')).toBe('# A2\n');
    const create = await fetch(url, { method: 'PUT', body: 'x', headers: { ...auth, 'If-None-Match': '*' } });
    expect(create.status).toBe(412);
    expect(((await create.json()) as { error: string }).error).toContain('exists');
  });

  it('keeps a hard link when saving through it', async () => {
    const a = path.join(dir, 'linked.md');
    await writeFile(a, 'one');
    await link(a, path.join(dir, 'linked-too.md'));
    await fetch(`${base}/file?${q({ path: 'linked.md' })}`, { method: 'PUT', body: 'two', headers: auth });
    expect(await readFile(path.join(dir, 'linked-too.md'), 'utf8')).toBe('two');
  });

  it('refuses uploads over the limit', async () => {
    const res = await fetch(`${base}/file?${q({ path: 'big.md' })}`, {
      method: 'PUT',
      body: 'x'.repeat(2048),
      headers: auth,
    });
    expect(res.status).toBe(413);
  });

  it('makes folders, moves, refuses a taken name, and deletes', async () => {
    const post = (p: string, params: Record<string, string>) =>
      fetch(`${base}/${p}?${q(params)}`, { method: 'POST', headers: auth });
    expect((await post('mkdir', { path: 'Notes/Sub/Deeper' })).status).toBe(200);
    expect((await post('move', { from: 'Notes/a.md', to: 'Notes/Sub/a.md' })).status).toBe(200);
    expect((await stat(path.join(dir, 'Notes/Sub/a.md'))).isFile()).toBe(true);
    expect((await post('move', { from: 'Notes/pic.svg', to: 'Notes/Sub/a.md' })).status).toBe(409);
    expect((await post('move', { from: 'Notes/Sub', to: 'Notes/Sub/Deeper/Sub' })).status).toBe(400);
    expect((await post('move', { from: '', to: 'x' })).status).toBe(400);
    const del = await fetch(`${base}/file?${q({ path: 'Notes/Sub' })}`, { method: 'DELETE', headers: auth });
    expect(del.status).toBe(200);
    expect(await stat(path.join(dir, 'Notes/Sub')).catch(() => null)).toBeNull();
  });
});
