import { createHash, randomBytes, type Hash } from 'node:crypto';
import { createReadStream, constants as fsConstants } from 'node:fs';
import { chown, lstat, mkdir, open, readdir, realpath, rename, rm, stat, unlink } from 'node:fs/promises';
import path from 'node:path';

/** A named folder on the NAS that holds notebooks; the client browses it and opens a folder in it. */
export interface Root {
  readonly id: string;
  readonly name: string;
  readonly dir: string;
}

export interface Entry {
  readonly name: string;
  /** Root-relative POSIX path. */
  readonly path: string;
  readonly kind: 'file' | 'dir';
  readonly size: number;
  readonly modified: number;
}

/** A failure the HTTP layer maps to a status code. */
export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

/**
 * Parse `PUFFINOTE_ROOTS` — `Name=/abs/dir;Other=/abs/dir2` — into roots. Ids are URL-safe slugs of the names,
 * so a root keeps its id across restarts as long as its name is stable.
 */
export function parseRoots(spec: string, baseDir: string): Root[] {
  const roots: Root[] = [];
  for (const part of spec.split(';')) {
    const trimmed = part.trim();
    if (!trimmed) continue;
    const eq = trimmed.indexOf('=');
    if (eq <= 0) throw new Error(`PUFFINOTE_ROOTS entry '${trimmed}' must look like Name=/path`);
    const name = trimmed.slice(0, eq).trim();
    const dir = path.resolve(baseDir, trimmed.slice(eq + 1).trim());
    const id =
      name
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/^-|-$/g, '') || `root${roots.length + 1}`;
    if (roots.some((r) => r.id === id)) throw new Error(`Duplicate root id '${id}'`);
    roots.push({ id, name, dir });
  }
  return roots;
}

/** Our own in-flight save files (`.<name>.<hex>.tmp`): never listed. */
const TEMP = /^\..+\.[0-9a-f]{12}\.tmp$/;

/**
 * Notebook folders on the NAS. Every path is root-relative; anything that escapes its root (`..`, an absolute
 * path, a symlink pointing out) is refused. Dot-named files are served: a notebook keeps its session in
 * `.puffinote/`, and the client decides whether to show hidden files.
 */
export class FileApi {
  readonly roots: readonly Root[];
  /** Tail of the write queue per resolved path: check-then-write runs one at a time per file. */
  private readonly writeQueue = new Map<string, Promise<unknown>>();

  constructor(roots: readonly Root[]) {
    this.roots = roots;
  }

  root(id: string | null): Root {
    const r = this.roots.find((x) => x.id === id);
    if (!r) throw new ApiError(404, `Unknown root '${id ?? ''}'`);
    return r;
  }

  /** Resolve a root-relative path inside the root, refusing `..`, absolute paths and NUL bytes. */
  resolve(root: Root, rel: string | null): string {
    const clean = (rel ?? '').replaceAll('\\', '/');
    if (clean.includes('\0')) throw new ApiError(400, 'Bad path');
    const full = path.resolve(root.dir, '.' + path.posix.normalize('/' + clean));
    const inside = path.relative(root.dir, full);
    if (inside.startsWith('..') || path.isAbsolute(inside)) throw new ApiError(403, 'Path escapes root');
    return full;
  }

  /** Like `resolve`, then also refuse a path whose nearest existing ancestor symlinks out of the root. */
  async resolveReal(root: Root, rel: string | null): Promise<string> {
    const full = this.resolve(root, rel);
    const rootReal = await realpath(root.dir).catch(() => root.dir);
    let probe = full;
    for (;;) {
      const real = await realpath(probe).catch(() => null);
      if (real !== null) {
        const inside = path.relative(rootReal, real);
        if (inside.startsWith('..') || path.isAbsolute(inside)) throw new ApiError(403, 'Path escapes root');
        return full;
      }
      const parent = path.dirname(probe);
      if (parent === probe) return full;
      probe = parent;
    }
  }

  /** A path that must name something inside the root, not the root itself. */
  private async resolveEntry(root: Root, rel: string | null): Promise<string> {
    const full = await this.resolveReal(root, rel);
    if (full === path.resolve(root.dir)) throw new ApiError(400, 'The root folder can’t be changed');
    return full;
  }

  async list(rootId: string | null, rel: string | null): Promise<Entry[]> {
    const root = this.root(rootId);
    const dir = await this.resolveReal(root, rel);
    let dirents;
    try {
      dirents = await readdir(dir, { withFileTypes: true });
    } catch (e) {
      throw fsError(e);
    }
    const entries: Entry[] = [];
    for (const d of dirents) {
      if (TEMP.test(d.name)) continue;
      const full = path.join(dir, d.name);
      // Follow symlinks for the kind, but only ones that stay inside the root.
      const s = await this.resolveReal(root, path.relative(root.dir, full))
        .then(() => stat(full))
        .catch(() => null);
      if (!s || (!s.isDirectory() && !s.isFile())) continue;
      entries.push({
        name: d.name,
        path: path.relative(root.dir, full).split(path.sep).join('/'),
        kind: s.isDirectory() ? 'dir' : 'file',
        size: s.isDirectory() ? 0 : s.size,
        modified: s.mtimeMs,
      });
    }
    entries.sort((a, b) => (a.kind === b.kind ? a.name.localeCompare(b.name) : a.kind === 'dir' ? -1 : 1));
    return entries;
  }

  async read(
    rootId: string | null,
    rel: string | null,
  ): Promise<{ file: string; size: number; etag: string; modified: number }> {
    const root = this.root(rootId);
    const full = await this.resolveEntry(root, rel);
    try {
      const s = await stat(full);
      if (!s.isFile()) throw new ApiError(400, 'Not a file');
      // Hashed as a stream and served as one (server.ts), so a large file never sits in memory.
      return { file: full, size: s.size, etag: await etagOfFile(full), modified: s.mtimeMs };
    } catch (e) {
      throw fsError(e);
    }
  }

  /**
   * Atomic save: write a temp file beside the target, fsync, rename over it. `ifMatch` is the etag the client
   * read (changed or gone → 412); `ifNoneMatch: '*'` refuses to overwrite (a new note). Writes to one path
   * run one at a time, so two saves from the same etag cannot both win. The replacement keeps the target's
   * mode (and owner, where permitted); a hard-linked target is written in place so the link survives.
   */
  async write(
    rootId: string | null,
    rel: string | null,
    bytes: Buffer,
    opts: { ifMatch?: string | null; ifNoneMatch?: string | null },
  ): Promise<{ etag: string }> {
    const root = this.root(rootId);
    const full = await this.resolveEntry(root, rel);
    return this.serialized(await queueKey(full), () => this.writeLocked(full, bytes, opts));
  }

  async mkdir(rootId: string | null, rel: string | null): Promise<void> {
    const root = this.root(rootId);
    const full = await this.resolveEntry(root, rel);
    const s = await stat(full).catch(() => null);
    if (s?.isFile()) throw new ApiError(409, `“${path.basename(full)}” already exists as a file`);
    await mkdir(full, { recursive: true }).catch((e: unknown) => {
      throw fsError(e);
    });
  }

  /** Rename or move a file or folder. A taken name is refused (409), except a change of case on one file. */
  async move(rootId: string | null, from: string | null, to: string | null): Promise<void> {
    const root = this.root(rootId);
    const src = await this.resolveEntry(root, from);
    const dest = await this.resolveEntry(root, to);
    if (src === dest) return;
    // Hold the write lock of both paths, so an in-flight save can't land on the old path after the move, and
    // a new file can't appear at the destination between the check and the rename.
    const keys = [...new Set([await queueKey(src), await queueKey(dest)])].sort();
    return this.serializedAll(keys, () => this.moveLocked(src, dest));
  }

  private async moveLocked(src: string, dest: string): Promise<void> {
    const s = await lstat(src).catch(() => null);
    if (!s) throw new ApiError(404, 'Not found');
    if (s.isDirectory() && (dest + path.sep).startsWith(src + path.sep))
      throw new ApiError(400, 'A folder can’t move into itself');
    const there = await realpath(dest).catch(() => null);
    if (there !== null && there !== (await realpath(src)))
      throw new ApiError(409, `“${path.basename(dest)}” already exists there`);
    try {
      await mkdir(path.dirname(dest), { recursive: true });
      await rename(src, dest);
      await syncDir(path.dirname(dest));
      if (path.dirname(src) !== path.dirname(dest)) await syncDir(path.dirname(src));
    } catch (e) {
      throw fsError(e);
    }
  }

  /** Delete a file, or a folder with everything in it. */
  async remove(rootId: string | null, rel: string | null): Promise<void> {
    const root = this.root(rootId);
    const full = await this.resolveEntry(root, rel);
    return this.serialized(await queueKey(full), () => this.removeLocked(full));
  }

  private async removeLocked(full: string): Promise<void> {
    if (!(await lstat(full).catch(() => null))) throw new ApiError(404, 'Not found');
    await rm(full, { recursive: true, force: true }).catch((e: unknown) => {
      throw fsError(e);
    });
  }

  /** Run `fn` after every earlier write to `key` has settled, so the If-Match check and the rename are atomic. */
  private async serialized<T>(key: string, fn: () => Promise<T>): Promise<T> {
    const prev = this.writeQueue.get(key) ?? Promise.resolve();
    const run = prev.then(fn, fn);
    const tail = run.catch(() => {});
    this.writeQueue.set(key, tail);
    try {
      return await run;
    } finally {
      if (this.writeQueue.get(key) === tail) this.writeQueue.delete(key);
    }
  }

  /** `serialized` over several keys, taken in sorted order so two callers can't wait on each other. */
  private serializedAll<T>(keys: readonly string[], fn: () => Promise<T>): Promise<T> {
    const [first, ...rest] = keys;
    if (first === undefined) return fn();
    return this.serialized(first, () => this.serializedAll(rest, fn));
  }

  private async writeLocked(
    full: string,
    bytes: Buffer,
    opts: { ifMatch?: string | null; ifNoneMatch?: string | null },
  ): Promise<{ etag: string }> {
    await mkdir(path.dirname(full), { recursive: true }).catch((e: unknown) => {
      throw fsError(e);
    });

    const target = await stat(full).catch(() => null);
    if (target && !target.isFile()) throw new ApiError(400, 'Not a file');
    // A symlink stays a link: replace the file it names (resolveEntry checked that it is inside the root).
    const dest = target ? await realpath(full) : full;
    const dir = path.dirname(dest);
    const base = path.basename(dest);
    if (opts.ifNoneMatch === '*' && target) throw new ApiError(412, 'A file with that name already exists');
    if (opts.ifMatch) {
      if (!target) throw new ApiError(412, 'The file was moved or deleted since it was opened');
      if (opts.ifMatch !== '*' && (await etagOfFile(dest)) !== opts.ifMatch)
        throw new ApiError(412, 'The file changed on the server since it was opened');
    }

    try {
      if (target && target.nlink > 1) {
        // A rename would split the hard link; write through it instead (not atomic, but keeps the link).
        const handle = await open(dest, fsConstants.O_WRONLY | fsConstants.O_TRUNC);
        try {
          await handle.writeFile(bytes);
          await handle.sync();
        } finally {
          await handle.close();
        }
        return { etag: etagOf(bytes) };
      }
    } catch (e) {
      throw fsError(e);
    }

    const mode = target ? target.mode & 0o7777 : 0o644;
    const tmp = path.join(dir, `.${base}.${randomBytes(6).toString('hex')}.tmp`);
    try {
      const handle = await open(tmp, fsConstants.O_CREAT | fsConstants.O_EXCL | fsConstants.O_WRONLY, mode);
      try {
        await handle.chmod(mode); // open() applies the umask; the replacement keeps the original bits
        await handle.writeFile(bytes);
        await handle.sync();
      } finally {
        await handle.close();
      }
      if (target) await chown(tmp, target.uid, target.gid).catch(() => {}); // only root may give files away
      await rename(tmp, dest);
      await syncDir(dir);
    } catch (e) {
      await unlink(tmp).catch(() => {});
      throw fsError(e);
    }
    return { etag: etagOf(bytes) };
  }
}

/**
 * The write-queue key for a path: the same file under any spelling gets the same key. Symlinks are resolved
 * (for a new file, through its nearest existing folder), and the key is case- and normalization-folded, since
 * `Note.md` and `note.md` are one file on a case-insensitive volume.
 */
async function queueKey(full: string): Promise<string> {
  let probe = full;
  const tail: string[] = [];
  for (;;) {
    const real = await realpath(probe).catch(() => null);
    if (real !== null)
      return path
        .join(real, ...tail)
        .normalize('NFC')
        .toLowerCase();
    const parent = path.dirname(probe);
    if (parent === probe) return full.normalize('NFC').toLowerCase();
    tail.unshift(path.basename(probe));
    probe = parent;
  }
}

/**
 * Make a rename in `dir` durable: without this, a power cut soon after a save can bring the old file back.
 * Windows can't open a folder, so this is best effort.
 */
async function syncDir(dir: string): Promise<void> {
  const handle = await open(dir, fsConstants.O_RDONLY).catch(() => null);
  if (!handle) return;
  await handle.sync().catch(() => {});
  await handle.close();
}

export function etagOf(bytes: Uint8Array): string {
  return etagOfHash(createHash('sha256').update(bytes));
}

/** `etagOf` the file's contents, read as a stream. */
async function etagOfFile(file: string): Promise<string> {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(file)) hash.update(chunk as Buffer);
  return etagOfHash(hash);
}

function etagOfHash(hash: Hash): string {
  return '"' + hash.digest('base64url').slice(0, 27) + '"';
}

function fsError(e: unknown): Error {
  if (e instanceof ApiError) return e;
  const code = (e as NodeJS.ErrnoException | null)?.code;
  if (code === 'ENOENT') return new ApiError(404, 'Not found');
  if (code === 'EACCES' || code === 'EPERM') return new ApiError(403, 'Permission denied');
  if (code === 'ENOTDIR') return new ApiError(400, 'Not a folder');
  if (code === 'EISDIR') return new ApiError(400, 'Not a file');
  if (code === 'EEXIST' || code === 'ENOTEMPTY') return new ApiError(409, 'That name is already taken');
  return e instanceof Error ? e : new Error(String(e));
}
