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
  FileError,
  FileLocks,
  isInside,
  listFolder,
  mapLimit,
  moveEntry,
  overwriteInPlace,
  overwriteInPlaceSync,
  replaceFile,
  replaceFileSync,
  tempPath,
  TEMP_FILE,
  writeAtomic,
} from '@puffinote/node-files';

/** A file on a disk with room for `capacity` bytes: writes past it fail with ENOSPC, like a full volume. */
function fullDisk(initial: string, capacity: number) {
  let data = Buffer.from(initial);
  const write = (buf: Uint8Array, offset: number, length: number, position: number): number => {
    if (position + length > capacity) throw Object.assign(new Error('ENOSPC'), { code: 'ENOSPC' });
    const next = Buffer.alloc(Math.max(data.length, position + length));
    data.copy(next);
    next.set(buf.subarray(offset, offset + length), position);
    data = next;
    return length;
  };
  const truncate = (len: number): void => {
    data = data.subarray(0, len);
  };
  return {
    text: () => data.toString(),
    sync: { write, truncate },
    async: {
      write: async (b: Uint8Array, o: number, l: number, p: number) => ({ bytesWritten: write(b, o, l, p) }),
      truncate: async (len: number) => truncate(len),
    },
  };
}

let dir: string;
beforeAll(async () => {
  dir = await realpath(await mkdtemp(path.join(os.tmpdir(), 'puffinote-files-')));
});
afterAll(async () => {
  await rm(dir, { recursive: true, force: true });
});
const wait = (ms: number) => new Promise((r) => setTimeout(r, ms));
const bytes = (s: string) => new TextEncoder().encode(s);
const sub = async (name: string) => {
  const p = path.join(dir, name);
  await mkdir(p, { recursive: true });
  return p;
};

describe('temp files', () => {
  it('are named in the shape every host leaves out, old 12-digit ones included', () => {
    expect(TEMP_FILE.test(path.basename(tempPath('/n/Note.md')))).toBe(true);
    expect(TEMP_FILE.test('.a.md.0123456789ab.tmp')).toBe(true);
    expect(TEMP_FILE.test('.a.md.0123456789abcdef.tmp')).toBe(true);
    expect(TEMP_FILE.test('a.md.tmp')).toBe(false);
    expect(TEMP_FILE.test('.a.md.0123.tmp')).toBe(false);
  });
});

for (const [name, write] of [
  ['replaceFile', (p: string, b: Uint8Array) => replaceFile(p, b).then(() => {})],
  ['replaceFileSync', async (p: string, b: Uint8Array) => replaceFileSync(p, b)],
] as const) {
  describe(name, () => {
    it('keeps the file mode and leaves no temp file', async () => {
      const d = await sub(`${name}-mode`);
      const p = path.join(d, 'a.md');
      await writeFile(p, 'a');
      await chmod(p, 0o600);
      await write(p, bytes('b'));
      // Windows has no POSIX permission bits.
      if (process.platform !== 'win32') expect((await stat(p)).mode & 0o777).toBe(0o600);
      expect(await readFile(p, 'utf8')).toBe('b');
      expect(await readdir(d)).toEqual(['a.md']);
    });

    it('refuses to replace a read-only file', async () => {
      const p = path.join(await sub(`${name}-ro`), 'locked.md');
      await writeFile(p, 'locked');
      await chmod(p, 0o444);
      const err = await write(p, bytes('x')).catch((e: unknown) => e);
      expect(err).toBeInstanceOf(FileError);
      expect((err as FileError).code).toBe('read-only');
      expect(await readFile(p, 'utf8')).toBe('locked');
      await chmod(p, 0o644);
    });

    it('writes through a hard link instead of splitting it', async () => {
      const d = await sub(`${name}-hard`);
      const p = path.join(d, 'linked.md');
      await writeFile(p, 'one-longer');
      await link(p, path.join(d, 'other.md'));
      await write(p, bytes('two'));
      expect(await readFile(path.join(d, 'other.md'), 'utf8')).toBe('two');
    });

    it('replaces a symlink itself, not what it names', async () => {
      const d = await sub(`${name}-sym`);
      await writeFile(path.join(d, 'real.md'), 'a');
      await symlink(path.join(d, 'real.md'), path.join(d, 'alias.md'));
      await write(path.join(d, 'alias.md'), bytes('b'));
      expect((await lstat(path.join(d, 'alias.md'))).isSymbolicLink()).toBe(false);
      expect(await readFile(path.join(d, 'real.md'), 'utf8')).toBe('a');
    });
  });
}

describe('writeAtomic', () => {
  it('replaces the target of a symlink and keeps the link', async () => {
    const d = await sub('atomic-sym');
    await writeFile(path.join(d, 'real.md'), 'a');
    await symlink(path.join(d, 'real.md'), path.join(d, 'alias.md'));
    await writeAtomic(path.join(d, 'alias.md'), bytes('b'));
    expect((await lstat(path.join(d, 'alias.md'))).isSymbolicLink()).toBe(true);
    expect(await readFile(path.join(d, 'real.md'), 'utf8')).toBe('b');
  });

  it('returns the stat of what it wrote', async () => {
    const p = path.join(await sub('atomic-stat'), 'n.md');
    const s = await writeAtomic(p, bytes('hello'));
    expect(s.size).toBe(5);
    expect(s.ino).toBe((await stat(p)).ino);
  });
});

describe('overwriteInPlace', () => {
  it('never empties the file, even on a full disk', async () => {
    const grow = fullDisk('old note', 10);
    await expect(overwriteInPlace(grow.async, bytes('a longer note'), 8)).rejects.toThrow('ENOSPC');
    expect(grow.text()).toBe('old note');
    const shrink = fullDisk('a longer note', 13);
    await overwriteInPlace(shrink.async, bytes('short'), 13);
    expect(shrink.text()).toBe('short');
    const fits = fullDisk('old', 100);
    await overwriteInPlace(fits.async, bytes('new and longer'), 3);
    expect(fits.text()).toBe('new and longer');
  });

  it('does the same synchronously', () => {
    const disk = fullDisk('old note', 10);
    expect(() => overwriteInPlaceSync(disk.sync, bytes('a longer note'), 8)).toThrow('ENOSPC');
    expect(disk.text()).toBe('old note');
    overwriteInPlaceSync(disk.sync, bytes('tiny'), 8);
    expect(disk.text()).toBe('tiny');
  });
});

describe('FileLocks', () => {
  const twoCalls = async (locks: FileLocks, a: string, b: string) => {
    const order: string[] = [];
    await Promise.all([
      locks.run(a, async () => {
        order.push('a1');
        await wait(20);
        order.push('a2');
      }),
      locks.run(b, async () => {
        order.push('b');
      }),
    ]);
    return order;
  };

  it('runs calls for one file one after another', async () => {
    const p = path.join(dir, 'lock.md');
    expect(await twoCalls(new FileLocks(), p, p)).toEqual(['a1', 'a2', 'b']);
  });

  it('treats a file under another spelling, through a symlink or in another case, as one file', async () => {
    const d = await sub('locks');
    const real = path.join(d, 'lock-real.md');
    await writeFile(real, '');
    await symlink(real, path.join(d, 'lock-alias.md'));
    await mkdir(path.join(d, 'lock-dir'));
    await symlink(path.join(d, 'lock-dir'), path.join(d, 'lock-dir-link'));
    const pairs = [
      [real, path.join(d, 'lock-alias.md')],
      [path.join(d, 'lock-dir', 'new.md'), path.join(d, 'lock-dir-link', 'new.md')],
      [path.join(d, 'Case.md'), path.join(d, 'case.md')],
    ] as const;
    for (const [a, b] of pairs) expect(await twoCalls(new FileLocks(), a, b)).toEqual(['a1', 'a2', 'b']);
  });

  it('lets different files run at once', async () => {
    const order = await twoCalls(new FileLocks(), path.join(dir, 'x.md'), path.join(dir, 'y.md'));
    expect(order).toEqual(['a1', 'b', 'a2']);
  });

  it('keeps going after a failed call', async () => {
    const locks = new FileLocks();
    const p = path.join(dir, 'lock2.md');
    await expect(locks.run(p, () => Promise.reject(new Error('x')))).rejects.toThrow('x');
    await expect(locks.run(p, () => Promise.resolve(1))).resolves.toBe(1);
  });

  it('runAll waits for every earlier call on any of its files', async () => {
    const locks = new FileLocks();
    const order: string[] = [];
    const first = locks.run(path.join(dir, 'b.md'), async () => {
      await wait(30);
      order.push('save b');
    });
    const move = locks.runAll([path.join(dir, 'A.md'), path.join(dir, 'b.md')], async () => {
      order.push('move');
    });
    await Promise.all([first, move]);
    expect(order).toEqual(['save b', 'move']);
  });
});

describe('mapLimit', () => {
  it('maps with at most so many calls in flight, keeping the order', async () => {
    let inFlight = 0;
    let most = 0;
    const out = await mapLimit([1, 2, 3, 4, 5, 6, 7], 3, async (n) => {
      most = Math.max(most, ++inFlight);
      await wait(5 * (8 - n));
      inFlight--;
      return n * 10;
    });
    expect(out).toEqual([10, 20, 30, 40, 50, 60, 70]);
    expect(most).toBe(3);
  });
});

describe('listFolder', () => {
  it('leaves out temp files, links it may not follow, dangling links and links that loop', async () => {
    const nb = await sub('list');
    await mkdir(path.join(nb, 'Inner'));
    await writeFile(path.join(nb, 'Inner', 'n.md'), 'n');
    await writeFile(path.join(nb, 'Inner', '.n.md.0123456789abcdef.tmp'), 'x');
    await symlink('..', path.join(nb, 'Inner', 'up'));
    await symlink('.', path.join(nb, 'Inner', 'self'));
    await symlink(os.tmpdir(), path.join(nb, 'Inner', 'out'));
    await symlink('missing.md', path.join(nb, 'Inner', 'dangling.md'));
    await symlink('Inner', path.join(nb, 'down'));
    const linkAllowed = async (p: string) => isInside(nb, await realpath(p).catch(() => p));
    const names = async (d: string) => (await listFolder(d, linkAllowed)).map((e) => e.name).sort();
    expect(await names(path.join(nb, 'Inner'))).toEqual(['n.md']);
    expect(await names(nb)).toEqual(['Inner', 'down']);
    const entry = (await listFolder(path.join(nb, 'Inner'), linkAllowed))[0];
    expect(entry).toMatchObject({
      name: 'n.md',
      path: path.join(nb, 'Inner', 'n.md'),
      kind: 'file',
      size: 1,
    });
  });
});

describe('moveEntry', () => {
  it('moves a file, refuses a taken name, and refuses a folder into itself', async () => {
    const locks = new FileLocks();
    const nb = await sub('move');
    await mkdir(path.join(nb, 'Sub'));
    await writeFile(path.join(nb, 'a.md'), 'a');
    await writeFile(path.join(nb, 'b.md'), 'b');
    await moveEntry(path.join(nb, 'a.md'), path.join(nb, 'Sub', 'New', 'a.md'), locks);
    expect(await readFile(path.join(nb, 'Sub', 'New', 'a.md'), 'utf8')).toBe('a');
    const code = (p: Promise<void>) =>
      p.then(
        () => null,
        (e: FileError) => e.code,
      );
    expect(await code(moveEntry(path.join(nb, 'b.md'), path.join(nb, 'Sub', 'New', 'a.md'), locks))).toBe(
      'exists',
    );
    expect(await code(moveEntry(path.join(nb, 'Sub'), path.join(nb, 'Sub', 'In'), locks))).toBe(
      'into-itself',
    );
    expect(await code(moveEntry(path.join(nb, 'gone.md'), path.join(nb, 'x.md'), locks))).toBe('not-found');
    expect(await readFile(path.join(nb, 'b.md'), 'utf8')).toBe('b');
  });

  it('waits for a save of either file to finish', async () => {
    const locks = new FileLocks();
    const nb = await sub('move-lock');
    await writeFile(path.join(nb, 'a.md'), 'a');
    const save = locks.run(path.join(nb, 'a.md'), async () => {
      await wait(20);
      await writeFile(path.join(nb, 'a.md'), 'saved');
    });
    await moveEntry(path.join(nb, 'a.md'), path.join(nb, 'b.md'), locks);
    await save;
    expect(await readFile(path.join(nb, 'b.md'), 'utf8')).toBe('saved');
  });
});
