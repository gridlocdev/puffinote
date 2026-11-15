import {
  mkdir,
  mkdtemp,
  readdir,
  readFile,
  realpath,
  rename,
  rm,
  stat,
  symlink,
  utimes,
  writeFile,
} from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { CONFLICT_CHANGED } from '../src/bridge.ts';
import {
  FolderGrants,
  fileVersion,
  folderArgs,
  isAppUrl,
  isOpenableDocument,
  listDir,
  moveEntry,
  pdfFileName,
  readWithMtime,
  sameVersion,
  saveFile,
  watchTree,
  withFileLock,
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

describe('listDir', () => {
  it('lists files and folders, leaving out temp files and links that loop or leave', async () => {
    const nb = path.join(dir, 'list-nb');
    await mkdir(path.join(nb, 'Inner'), { recursive: true });
    await writeFile(path.join(nb, 'Inner', 'n.md'), 'n');
    await writeFile(path.join(nb, 'Inner', '.n.md.0123456789abcdef.tmp'), 'x');
    await symlink('..', path.join(nb, 'Inner', 'up'));
    await symlink('.', path.join(nb, 'Inner', 'self'));
    await symlink(os.tmpdir(), path.join(nb, 'Inner', 'out'));
    await symlink('Inner', path.join(nb, 'down'));
    const grants = new FolderGrants(null);
    grants.grant(nb);
    const allows = (p: string) => grants.allows(p);
    const names = async (d: string) => (await listDir(d, allows)).map((e) => e.name).sort();
    expect(await names(path.join(nb, 'Inner'))).toEqual(['n.md']);
    expect(await names(nb)).toEqual(['Inner', 'down']);
    await mkdir(path.join(nb, 'A'));
    await mkdir(path.join(nb, 'B'));
    await symlink('../B', path.join(nb, 'A', 'toB'));
    await symlink('../A', path.join(nb, 'B', 'toA'));
    expect(await names(path.join(nb, 'A'))).toEqual(['toB']);
    expect(await names(path.join(nb, 'A', 'toB'))).toEqual([]);
  });
});

describe('listDir in a large folder', () => {
  it('lists a large folder, following only links that stay inside the notebook', async () => {
    const nb = path.join(dir, 'big-nb');
    const outside = await mkdtemp(path.join(os.tmpdir(), 'puffinote-outside-'));
    await writeFile(path.join(outside, 'secret.md'), 'nope');
    await mkdir(path.join(nb, 'Sub'), { recursive: true });
    for (let i = 0; i < 300; i++) await writeFile(path.join(nb, `n${i}.md`), String(i));
    await symlink(path.join(outside, 'secret.md'), path.join(nb, 'leak.md'));
    await symlink(outside, path.join(nb, 'leak-dir'));
    await symlink('n1.md', path.join(nb, 'inside.md'));
    await symlink('missing.md', path.join(nb, 'dangling.md'));
    const grants = new FolderGrants(null);
    grants.grant(nb);
    const entries = await listDir(nb, (p) => grants.allows(p));
    const names = entries.map((e) => e.name);
    expect(names).toHaveLength(302);
    expect(names).toContain('inside.md');
    expect(names).not.toContain('leak.md');
    expect(names).not.toContain('leak-dir');
    expect(names).not.toContain('dangling.md');
    expect(entries.find((e) => e.name === 'n12.md')).toMatchObject({ kind: 'file', size: 2 });
    await expect(listDir(outside, (p) => grants.allows(p))).rejects.toThrow('Forbidden');
    await rm(outside, { recursive: true, force: true });
  });
});

describe('moveEntry', () => {
  it('moves a file, refuses a taken name, and never moves a notebook folder itself', async () => {
    const nb = path.join(dir, 'move-nb');
    await mkdir(path.join(nb, 'Sub'), { recursive: true });
    await writeFile(path.join(nb, 'a.md'), 'a');
    await writeFile(path.join(nb, 'b.md'), 'b');
    const grants = new FolderGrants(null);
    grants.grant(nb);
    const isRoot = (p: string) => grants.isRoot(p);
    await moveEntry(path.join(nb, 'a.md'), path.join(nb, 'Sub', 'a.md'), isRoot);
    expect(await readFile(path.join(nb, 'Sub', 'a.md'), 'utf8')).toBe('a');
    await expect(moveEntry(path.join(nb, 'b.md'), path.join(nb, 'Sub', 'a.md'), isRoot)).rejects.toThrow(
      /CONFLICT_EXISTS/,
    );
    await expect(moveEntry(path.join(nb, 'Sub'), path.join(nb, 'Sub', 'In'), isRoot)).rejects.toThrow(
      /into itself/,
    );
    await expect(moveEntry(nb, path.join(dir, 'moved-nb'), isRoot)).rejects.toThrow('Forbidden');
    expect((await stat(nb)).isDirectory()).toBe(true);
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

describe('readWithMtime', () => {
  it('reads the bytes with their mtime', async () => {
    const f = path.join(dir, 'read.md');
    await writeFile(f, 'hello');
    const { bytes: got, modified } = await readWithMtime(f);
    expect(new TextDecoder().decode(got)).toBe('hello');
    expect(modified).toBe((await stat(f)).mtimeMs);
  });
});

const anywhere = () => Promise.resolve(true);
const fresh = { expected: null, force: false, create: true, allows: anywhere };
const over = (expected: string) => ({ expected, force: false, create: false, allows: anywhere });

describe('file versions', () => {
  it('compare the mtime, the size and the inode', () => {
    expect(sameVersion('1000:5:7', '1000:5:7')).toBe(true);
    expect(sameVersion('1000:5:7', '1000.6:5:7')).toBe(true); // the same instant, rounded differently
    expect(sameVersion('1000:5:7', '1000:6:7')).toBe(false);
    expect(sameVersion('1000:5:7', '1000:5:8')).toBe(false);
    expect(sameVersion('1000:5:7', '1002:5:7')).toBe(false);
  });

  it('save returns the version of exactly what it wrote', async () => {
    const p = path.join(dir, 'version.md');
    const v = await saveFile(p, bytes('first'), fresh);
    expect(sameVersion(v, fileVersion(await stat(p)))).toBe(true);
    const next = await saveFile(p, bytes('second'), over(v));
    expect(sameVersion(next, fileVersion(await stat(p)))).toBe(true);
    expect((await readWithMtime(p)).version).toBe(next);
  });

  it('a save refuses a file written outside within the same mtime', async () => {
    const p = path.join(dir, 'coarse.md');
    const v = await saveFile(p, bytes('ours'), fresh);
    const before = await stat(p);
    // Another app saves within the same second on a disk with coarse times: the mtime doesn't move.
    await writeFile(p + '.other', 'theirs, longer');
    await rename(p + '.other', p);
    await utimes(p, before.atime, before.mtime);
    await expect(saveFile(p, bytes('mine'), over(v))).rejects.toThrow(CONFLICT_CHANGED);
    expect(await readFile(p, 'utf8')).toBe('theirs, longer');
  });
});

describe('saveFile', () => {
  it('checks the grant again once it holds the lock, so a folder swapped for a link out is not written through', async () => {
    const outside = await realpath(await mkdtemp(path.join(os.tmpdir(), 'puffinote-outside-')));
    const nb = path.join(dir, 'swap-nb');
    await mkdir(path.join(nb, 'sub'), { recursive: true });
    const grants = new FolderGrants(null);
    grants.grant(nb);
    const target = path.join(nb, 'sub', 'note.md');
    let release = () => {};
    const held = withFileLock(target, () => new Promise<void>((r) => (release = r)));
    const save = saveFile(target, bytes('secret'), { ...fresh, allows: (p) => grants.allows(p) });
    await wait(20);
    // While the save waits its turn, the folder becomes a link out of the notebook.
    await rm(path.join(nb, 'sub'), { recursive: true });
    await symlink(outside, path.join(nb, 'sub'));
    release();
    await held;
    await expect(save).rejects.toThrow('Forbidden');
    expect(await readdir(outside)).toEqual([]);
    await rm(outside, { recursive: true, force: true });
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

describe('pdfFileName', () => {
  it('suggests the asked-for name without folders, ending in .pdf', () => {
    expect(pdfFileName('Trip.pdf')).toBe('Trip.pdf');
    expect(pdfFileName('Trip')).toBe('Trip.pdf');
    expect(pdfFileName('../../etc/Trip.PDF')).toBe('Trip.PDF');
    expect(pdfFileName('a\\b\\c.pdf')).toBe('c.pdf');
    expect(pdfFileName('')).toBe('Note.pdf');
    expect(pdfFileName(42)).toBe('Note.pdf');
  });
});
