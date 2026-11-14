import {
  chmod,
  link,
  lstat,
  mkdir,
  mkdtemp,
  readdir,
  readFile,
  realpath,
  rm,
  stat,
  symlink,
  writeFile,
} from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import {
  FolderGrants,
  folderArgs,
  isAppUrl,
  isOpenableDocument,
  readWithMtime,
  watchTree,
  withFileLock,
  withFileLocks,
  writeAtomic,
} from '../src/files.ts';

let dir: string;
beforeAll(async () => {
  dir = await realpath(await mkdtemp(path.join(os.tmpdir(), 'puffinote-desktop-')));
});
afterAll(async () => {
  await rm(dir, { recursive: true, force: true });
});
const wait = (ms: number) => new Promise((r) => setTimeout(r, ms));
const bytes = (s: string) => new TextEncoder().encode(s);

describe('writeAtomic', () => {
  it('keeps the file mode and leaves no temp file', async () => {
    const sub = path.join(dir, 'mode');
    await mkdir(sub);
    const p = path.join(sub, 'a.md');
    await writeFile(p, 'a');
    await chmod(p, 0o600);
    await writeAtomic(p, bytes('b'));
    // Windows has no POSIX permission bits.
    if (process.platform !== 'win32') expect((await stat(p)).mode & 0o777).toBe(0o600);
    expect(await readFile(p, 'utf8')).toBe('b');
    expect(await readdir(sub)).toEqual(['a.md']);
  });

  it('refuses to replace a read-only file', async () => {
    const p = path.join(dir, 'locked.md');
    await writeFile(p, 'locked');
    await chmod(p, 0o444);
    await expect(writeAtomic(p, bytes('x'))).rejects.toThrow(/read-only/);
    expect(await readFile(p, 'utf8')).toBe('locked');
    await chmod(p, 0o644);
  });

  it('writes through a hard link instead of splitting it', async () => {
    const p = path.join(dir, 'linked.md');
    const other = path.join(dir, 'other.md');
    await writeFile(p, 'one-longer');
    await link(p, other);
    await writeAtomic(p, bytes('two'));
    expect(await readFile(other, 'utf8')).toBe('two');
  });

  it('replaces the target of a symlink and keeps the link', async () => {
    const real = path.join(dir, 'real.md');
    const alias = path.join(dir, 'alias.md');
    await writeFile(real, 'a');
    await symlink(real, alias);
    await writeAtomic(alias, bytes('b'));
    expect((await lstat(alias)).isSymbolicLink()).toBe(true);
    expect(await readFile(real, 'utf8')).toBe('b');
  });
});

describe('withFileLock', () => {
  it('runs calls for one file one after another', async () => {
    const order: string[] = [];
    const p = path.join(dir, 'lock.md');
    await Promise.all([
      withFileLock(p, async () => {
        order.push('a1');
        await wait(20);
        order.push('a2');
      }),
      withFileLock(p, async () => {
        order.push('b');
      }),
    ]);
    expect(order).toEqual(['a1', 'a2', 'b']);
  });

  it('keeps going after a failed call', async () => {
    const p = path.join(dir, 'lock2.md');
    await expect(withFileLock(p, () => Promise.reject(new Error('x')))).rejects.toThrow('x');
    await expect(withFileLock(p, () => Promise.resolve(1))).resolves.toBe(1);
  });
});

describe('FolderGrants', () => {
  it('allows paths inside a granted folder and nothing outside', async () => {
    const nb = path.join(dir, 'nb');
    await mkdir(path.join(nb, 'sub'), { recursive: true });
    const g = new FolderGrants(null);
    await g.load();
    g.grant(nb);
    expect(g.isRoot(nb)).toBe(true);
    expect(g.isRoot(path.join(nb, 'sub'))).toBe(false);
    expect(await g.allows(nb)).toBe(true);
    expect(await g.allows(path.join(nb, 'sub', 'new.md'))).toBe(true);
    expect(await g.allows(path.join(nb, '..', 'x.md'))).toBe(false);
    expect(await g.allows(path.join(dir, 'nbx', 'a.md'))).toBe(false);
    expect(await g.allows('relative/a.md')).toBe(false);
    expect(await g.allows(123)).toBe(false);
  });

  it('refuses a symlink that leads out of the notebook', async () => {
    const nb = path.join(dir, 'nb2');
    await mkdir(nb);
    await writeFile(path.join(dir, 'secret.md'), 's');
    await symlink(path.join(dir, 'secret.md'), path.join(nb, 'leak.md'));
    await symlink(dir, path.join(nb, 'up'));
    const g = new FolderGrants(null);
    await g.load();
    g.grant(nb);
    expect(await g.allows(path.join(nb, 'leak.md'))).toBe(false);
    expect(await g.allows(path.join(nb, 'up', 'new.md'))).toBe(false);
  });

  it('follows a dangling symlink to where a save through it would land', async () => {
    const nb = path.join(dir, 'nb3');
    await mkdir(path.join(nb, '.puffinote'), { recursive: true });
    await symlink('../../out/pwned.txt', path.join(nb, '.puffinote', 'session.json'));
    await symlink('missing.md', path.join(nb, 'inside-link.md'));
    await symlink(path.join(dir, 'gone-dir'), path.join(nb, 'gone'));
    await symlink('loop-b', path.join(nb, 'loop-a'));
    await symlink('loop-a', path.join(nb, 'loop-b'));
    const g = new FolderGrants(null);
    await g.load();
    g.grant(nb);
    expect(await g.allows(path.join(nb, '.puffinote', 'session.json'))).toBe(false);
    expect(await g.allows(path.join(nb, 'gone', 'new.md'))).toBe(false);
    expect(await g.allows(path.join(nb, 'inside-link.md'))).toBe(true);
    expect(await g.allows(path.join(nb, 'loop-a'))).toBe(false);
  });

  it('persists grants and reloads them', async () => {
    const file = path.join(dir, 'grants', 'granted.json');
    const g = new FolderGrants(file);
    await g.load();
    g.grant(path.join(dir, 'nb'));
    await g.flushed();
    const again = new FolderGrants(file);
    await again.load();
    expect(again.isRoot(path.join(dir, 'nb'))).toBe(true);
  });
});

describe('folderArgs', () => {
  it('keeps existing folders, resolved against the working directory', async () => {
    await mkdir(path.join(dir, 'arg'), { recursive: true });
    await writeFile(path.join(dir, 'file.md'), '');
    expect(folderArgs(['--flag', 'arg', 'file.md', 'missing'], dir)).toEqual([path.join(dir, 'arg')]);
  });
});

describe('watchTree', () => {
  it('reports changes in subfolders once, without our temp files', async () => {
    const root = path.join(dir, 'watched');
    await mkdir(path.join(root, 'sub'), { recursive: true });
    const calls: string[][] = [];
    const stop = watchTree(root, (paths) => calls.push(paths), 50);
    try {
      await wait(50);
      await writeFile(path.join(root, 'sub', 'a.md'), 'x');
      await writeFile(path.join(root, 'sub', '.a.md.0123456789abcdef.tmp'), 'x');
      await wait(300);
      expect(calls.length).toBeGreaterThanOrEqual(1);
      const all = calls.flat();
      expect(all).toContain(path.join(root, 'sub', 'a.md'));
      expect(all.some((p) => p.endsWith('.tmp'))).toBe(false);
    } finally {
      stop();
    }
  });

  it('still reports during a steady stream of changes', async () => {
    const root = path.join(dir, 'busy');
    await mkdir(root, { recursive: true });
    const calls: string[][] = [];
    const stop = watchTree(root, (paths) => calls.push(paths), 100, { maxWaitMs: 250 });
    try {
      await wait(50);
      for (let i = 0; i < 20; i++) {
        await writeFile(path.join(root, 'log.txt'), String(i));
        await wait(40); // always inside the 100 ms quiet period
      }
      expect(calls.length).toBeGreaterThanOrEqual(2);
    } finally {
      stop();
    }
  });
});

describe('withFileLocks and readWithMtime', () => {
  it('runs after every earlier lock on any of its files', async () => {
    const order: string[] = [];
    const a = path.join(dir, 'A.md');
    const b = path.join(dir, 'b.md');
    const first = withFileLock(b, async () => {
      await wait(30);
      order.push('save b');
    });
    const move = withFileLocks([a, b], async () => {
      order.push('move');
    });
    await Promise.all([first, move]);
    expect(order).toEqual(['save b', 'move']);
  });

  it('reads the bytes with their mtime', async () => {
    const f = path.join(dir, 'read.md');
    await writeFile(f, 'hello');
    const { bytes: got, modified } = await readWithMtime(f);
    expect(new TextDecoder().decode(got)).toBe('hello');
    expect(modified).toBe((await stat(f)).mtimeMs);
  });
});

describe('isAppUrl', () => {
  it('trusts only the app scheme and host', () => {
    expect(isAppUrl('app://puffinote/index.html')).toBe(true);
    expect(isAppUrl('app://evil/index.html')).toBe(false);
    expect(isAppUrl('https://puffinote/')).toBe(false);
    expect(isAppUrl(undefined)).toBe(false);
  });
});

describe('isOpenableDocument', () => {
  it('opens documents but never apps, scripts or folders', async () => {
    const d = path.join(dir, 'openable');
    await mkdir(path.join(d, 'Evil.app'), { recursive: true });
    await mkdir(path.join(d, 'folder.pdf'));
    await writeFile(path.join(d, 'note.PDF'), 'x');
    await writeFile(path.join(d, 'run.command'), 'x');
    await writeFile(path.join(d, 'noext'), 'x');
    await symlink(path.join(d, 'run.command'), path.join(d, 'disguised.pdf'));
    expect(await isOpenableDocument(path.join(d, 'note.PDF'))).toBe(true);
    expect(await isOpenableDocument(path.join(d, 'run.command'))).toBe(false);
    expect(await isOpenableDocument(path.join(d, 'noext'))).toBe(false);
    expect(await isOpenableDocument(path.join(d, 'Evil.app'))).toBe(false);
    expect(await isOpenableDocument(path.join(d, 'folder.pdf'))).toBe(false);
    expect(await isOpenableDocument(path.join(d, 'disguised.pdf'))).toBe(false);
    expect(await isOpenableDocument(path.join(d, 'missing.pdf'))).toBe(false);
  });
});
