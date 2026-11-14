import {
  chmod,
  link,
  lstat,
  mkdtemp,
  mkdir,
  readFile,
  rename,
  rm,
  stat,
  symlink,
  writeFile,
} from 'node:fs/promises';
import type { AddressInfo } from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { etagOf, FileApi, mapLimit, overwriteInPlace, parseRoots } from '../src/fileApi.ts';
import { createServer, hostAllowed, isLoopback, parseHosts, refuseWithoutToken } from '../src/server.ts';

/** A file on a disk with room for `capacity` bytes: writes past it fail with ENOSPC, like a full volume. */
function fullDisk(initial: string, capacity: number) {
  let data = Buffer.from(initial);
  return {
    text: () => data.toString(),
    async write(buf: Uint8Array, offset: number, length: number, position: number) {
      if (position + length > capacity) throw Object.assign(new Error('ENOSPC'), { code: 'ENOSPC' });
      const next = Buffer.alloc(Math.max(data.length, position + length));
      data.copy(next);
      next.set(buf.subarray(offset, offset + length), position);
      data = next;
      return { bytesWritten: length };
    },
    async truncate(len: number) {
      data = data.subarray(0, len);
    },
  };
}

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

  it('tells loopback bind addresses from ones the network can reach', () => {
    for (const h of ['127.0.0.1', '127.1.2.3', '::1', '[::1]', 'localhost', '::ffff:127.0.0.1'])
      expect(isLoopback(h), h).toBe(true);
    for (const h of ['0.0.0.0', '::', '192.168.1.4', 'nas.lan', '']) expect(isLoopback(h), h).toBe(false);
  });

  it('refuses to listen on the network without a token unless told to', () => {
    expect(refuseWithoutToken({ host: '0.0.0.0', token: null, allowNoToken: false })).toMatch(
      /PUFFINOTE_TOKEN.*PUFFINOTE_ALLOW_NO_TOKEN=1/s,
    );
    expect(refuseWithoutToken({ host: '192.168.1.4', token: null, allowNoToken: false })).not.toBeNull();
    expect(refuseWithoutToken({ host: '0.0.0.0', token: 's3cret', allowNoToken: false })).toBeNull();
    expect(refuseWithoutToken({ host: '0.0.0.0', token: null, allowNoToken: true })).toBeNull();
    expect(refuseWithoutToken({ host: '127.0.0.1', token: null, allowNoToken: false })).toBeNull();
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

  it('leaves out a folder link back to the folder or one above it', async () => {
    await mkdir(path.join(dir, 'Loop', 'Inner'), { recursive: true });
    await writeFile(path.join(dir, 'Loop', 'Inner', 'n.md'), 'n');
    await symlink('..', path.join(dir, 'Loop', 'Inner', 'up'));
    await symlink('.', path.join(dir, 'Loop', 'Inner', 'self'));
    await symlink('../..', path.join(dir, 'Loop', 'Inner', 'top'));
    await symlink('Inner', path.join(dir, 'Loop', 'down'));
    const api = new FileApi(parseRoots(`My Notes=${dir}`, '/'));
    const names = async (rel: string) => (await api.list('my-notes', rel)).map((e) => e.name);
    expect(await names('Loop/Inner')).toEqual(['n.md']);
    expect(await names('Loop')).toEqual(['down', 'Inner']);
    // Two folders linking to each other: each shows the other once, then the loop is cut.
    await mkdir(path.join(dir, 'Loop', 'A'));
    await mkdir(path.join(dir, 'Loop', 'B'));
    await symlink('../B', path.join(dir, 'Loop', 'A', 'toB'));
    await symlink('../A', path.join(dir, 'Loop', 'B', 'toA'));
    expect(await names('Loop/A')).toEqual(['toB']);
    expect(await names('Loop/A/toB')).toEqual([]);
  });

  it('lists a large folder, following only links that stay inside the root', async () => {
    const big = path.join(dir, 'Big');
    await mkdir(path.join(big, 'Sub'), { recursive: true });
    for (let i = 0; i < 300; i++) await writeFile(path.join(big, `n${i}.md`), String(i));
    await symlink(path.join(outside, 'secret.md'), path.join(big, 'leak.md'));
    await symlink(outside, path.join(big, 'leak-dir'));
    await symlink('n1.md', path.join(big, 'inside.md'));
    await symlink('Sub', path.join(big, 'inside-dir'));
    await symlink('missing.md', path.join(big, 'dangling.md'));
    const api = new FileApi(parseRoots(`My Notes=${dir}`, '/'));
    const entries = await api.list('my-notes', 'Big');
    const names = entries.map((e) => e.name);
    expect(names).toHaveLength(303);
    expect(names.slice(0, 2)).toEqual(['inside-dir', 'Sub']);
    expect(names).toContain('inside.md');
    expect(names).not.toContain('leak.md');
    expect(names).not.toContain('leak-dir');
    expect(names).not.toContain('dangling.md');
    expect(entries.find((e) => e.name === 'n12.md')).toMatchObject({ kind: 'file', size: 2 });
  });

  it('maps with at most so many calls in flight, keeping the order', async () => {
    let inFlight = 0;
    let most = 0;
    const out = await mapLimit([1, 2, 3, 4, 5, 6, 7], 3, async (n) => {
      most = Math.max(most, ++inFlight);
      await new Promise((r) => setTimeout(r, 5 * (8 - n)));
      inFlight--;
      return n * 10;
    });
    expect(out).toEqual([10, 20, 30, 40, 50, 60, 70]);
    expect(most).toBe(3);
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

  it('does not bring back a deleted folder when a stale save is refused', async () => {
    await mkdir(path.join(dir, 'Gone'));
    await writeFile(path.join(dir, 'Gone', 'n.md'), 'n');
    const url = `${base}/file?${q({ path: 'Gone/n.md' })}`;
    const etag = (await fetch(url, { headers: auth })).headers.get('etag')!;
    await rm(path.join(dir, 'Gone'), { recursive: true });
    const res = await fetch(url, { method: 'PUT', body: 'late', headers: { ...auth, 'If-Match': etag } });
    expect(res.status).toBe(412);
    expect(await lstat(path.join(dir, 'Gone')).catch(() => null)).toBeNull();
  });

  it('refuses to save over a read-only file', async () => {
    const file = path.join(dir, 'locked.md');
    await writeFile(file, 'locked');
    await chmod(file, 0o444);
    const res = await fetch(`${base}/file?${q({ path: 'locked.md' })}`, {
      method: 'PUT',
      body: 'x',
      headers: auth,
    });
    expect(res.status).toBe(403);
    expect(((await res.json()) as { error: string }).error).toContain('read-only');
    expect(await readFile(file, 'utf8')).toBe('locked');
    await chmod(file, 0o644);
  });

  it('writes a hard-linked file in place without ever emptying it', async () => {
    const grow = fullDisk('old note', 10);
    await expect(overwriteInPlace(grow, Buffer.from('a longer note'), 8)).rejects.toThrow('ENOSPC');
    expect(grow.text()).toBe('old note');
    const shrink = fullDisk('a longer note', 13);
    await overwriteInPlace(shrink, Buffer.from('short'), 13);
    expect(shrink.text()).toBe('short');
    const fits = fullDisk('old', 100);
    await overwriteInPlace(fits, Buffer.from('new and longer'), 3);
    expect(fits.text()).toBe('new and longer');
  });

  it('keeps a hard link when saving through it', async () => {
    const a = path.join(dir, 'linked.md');
    await writeFile(a, 'one');
    await link(a, path.join(dir, 'linked-too.md'));
    await fetch(`${base}/file?${q({ path: 'linked.md' })}`, { method: 'PUT', body: 'two', headers: auth });
    expect(await readFile(path.join(dir, 'linked-too.md'), 'utf8')).toBe('two');
  });

  it('keeps a symlink when saving through it, and replaces the file it names', async () => {
    await writeFile(path.join(dir, 'real.md'), 'real');
    await symlink('real.md', path.join(dir, 'alias.md'));
    const url = `${base}/file?${q({ path: 'alias.md' })}`;
    const etag = (await fetch(url, { headers: auth })).headers.get('etag') ?? '';
    const res = await fetch(url, { method: 'PUT', body: 'new', headers: { ...auth, 'If-Match': etag } });
    expect(res.status).toBe(200);
    expect((await lstat(path.join(dir, 'alias.md'))).isSymbolicLink()).toBe(true);
    expect(await readFile(path.join(dir, 'real.md'), 'utf8')).toBe('new');
  });

  it('refuses changes sent by another site’s page', async () => {
    const move = (headers: Record<string, string>) =>
      fetch(`${base}/move?${q({ from: 'Notes/a.md', to: 'Notes/b.md' })}`, {
        method: 'POST',
        headers: { ...auth, ...headers },
      });
    expect((await move({ 'Sec-Fetch-Site': 'cross-site' })).status).toBe(403);
    expect((await move({ 'Sec-Fetch-Site': 'same-site' })).status).toBe(403);
    expect((await move({ Origin: 'https://evil.example' })).status).toBe(403);
    expect((await move({ Origin: 'null' })).status).toBe(403);
    expect(await stat(path.join(dir, 'Notes/a.md'))).toBeTruthy();
    const host = new URL(base).host;
    expect((await move({ Origin: `http://${host}` })).status).toBe(200);
    expect((await move({ 'Sec-Fetch-Site': 'same-origin' })).status).toBe(404); // a.md has moved
    await rename(path.join(dir, 'Notes/b.md'), path.join(dir, 'Notes/a.md'));
  });

  it('streams a file and its content hash', async () => {
    const big = Buffer.alloc(3 * 1024 * 1024, 'x');
    await writeFile(path.join(dir, 'big.bin'), big);
    const res = await fetch(`${base}/file?${q({ path: 'big.bin' })}`, { headers: auth });
    expect(res.headers.get('etag')).toBe(etagOf(big));
    expect(Buffer.from(await res.arrayBuffer()).equals(big)).toBe(true);
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
