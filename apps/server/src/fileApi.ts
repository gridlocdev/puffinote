import {
  FileError,
  FileLocks,
  listFolder,
  moveEntry,
  replaceFile,
  type FolderEntry,
} from '@puffinote/node-files';
import { createHash, type Hash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { mkdir, realpath, rm, lstat, stat } from 'node:fs/promises';
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
  /** Birth time where the file system keeps one (0 otherwise). */
  readonly created: number;
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

/**
 * Notebook folders on the NAS. Every path is root-relative; anything that escapes its root (`..`, an absolute
 * path, a symlink pointing out) is refused. Dot-named files are served: a notebook keeps its session in
 * `.puffinote/`, and the client decides whether to show hidden files.
 */
export class FileApi {
  readonly roots: readonly Root[];
  /** Check-then-write runs one at a time per file. */
  private readonly locks: FileLocks;

  /** `locks` is shared with whatever else writes these folders (a test holds one to make a save wait). */
  constructor(roots: readonly Root[], locks = new FileLocks()) {
    this.roots = roots;
    this.locks = locks;
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

  /** Refuse a real (symlink-resolved) path outside the root. */
  private async assertInside(root: Root, real: string): Promise<void> {
    const rootReal = await realpath(root.dir).catch(() => root.dir);
    const inside = path.relative(rootReal, real);
    if (inside.startsWith('..') || path.isAbsolute(inside)) throw new ApiError(403, 'Path escapes root');
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
    let found: FolderEntry[];
    try {
      // A symlink is followed for its kind only when it stays inside the root.
      found = await listFolder(dir, (full) =>
        this.resolveReal(root, path.relative(root.dir, full)).then(
          () => true,
          () => false,
        ),
      );
    } catch (e) {
      throw fsError(e);
    }
    // The plain entries trust that the folder is inside the root: check it again now they are read, in case
    // it was swapped for a link out meanwhile.
    await this.resolveReal(root, rel);
    const entries = found.map((e): Entry => ({
      ...e,
      path: path.relative(root.dir, e.path).split(path.sep).join('/'),
    }));
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
   * mode (and owner, where permitted); a hard-linked target is written in place so the link survives
   * (`overwriteInPlace`).
   */
  async write(
    rootId: string | null,
    rel: string | null,
    bytes: Buffer,
    opts: { ifMatch?: string | null; ifNoneMatch?: string | null },
  ): Promise<{ etag: string }> {
    const root = this.root(rootId);
    const full = await this.resolveEntry(root, rel);
    return this.locks.run(full, async () => {
      // Check again now the save has its turn: while it waited, a folder on the path may have been swapped
      // for a link out of the root.
      await this.resolveReal(root, rel);
      return this.writeLocked(root, full, bytes, opts);
    });
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
    // Under both paths' locks, so an in-flight save can't land on the old path after the move, and a new file
    // can't appear at the destination between the check and the rename.
    await moveEntry(src, dest, this.locks).catch((e: unknown) => {
      throw fsError(e);
    });
  }

  /** Delete a file, or a folder with everything in it. */
  async remove(rootId: string | null, rel: string | null): Promise<void> {
    const root = this.root(rootId);
    const full = await this.resolveEntry(root, rel);
    return this.locks.run(full, () => this.removeLocked(full));
  }

  private async removeLocked(full: string): Promise<void> {
    if (!(await lstat(full).catch(() => null))) throw new ApiError(404, 'Not found');
    await rm(full, { recursive: true, force: true }).catch((e: unknown) => {
      throw fsError(e);
    });
  }

  private async writeLocked(
    root: Root,
    full: string,
    bytes: Buffer,
    opts: { ifMatch?: string | null; ifNoneMatch?: string | null },
  ): Promise<{ etag: string }> {
    const target = await stat(full).catch(() => null);
    if (target && !target.isFile()) throw new ApiError(400, 'Not a file');
    // A symlink stays a link: replace the file it names, as long as that is inside the root.
    const dest = target ? await realpath(full) : full;
    if (target) await this.assertInside(root, dest);
    const dir = path.dirname(dest);
    if (opts.ifNoneMatch === '*' && target) throw new ApiError(412, 'A file with that name already exists');
    if (opts.ifMatch) {
      if (!target) throw new ApiError(412, 'The file was moved or deleted since it was opened');
      if (opts.ifMatch !== '*' && (await etagOfFile(dest)) !== opts.ifMatch)
        throw new ApiError(412, 'The file changed on the server since it was opened');
    }
    try {
      // Only now, past the checks above: a refused save must not bring back a folder deleted since it read.
      if (!target) await mkdir(dir, { recursive: true });
      await this.assertInside(root, await realpath(dir));
      // Atomic, keeping the mode and owner; a read-only file is refused, a hard-linked one written in place.
      await replaceFile(dest, bytes);
    } catch (e) {
      throw fsError(e);
    }
    return { etag: etagOf(bytes) };
  }
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

/** The status for each refusal of the shared file code. */
const FILE_ERROR_STATUS = {
  'not-found': 404,
  exists: 409,
  'read-only': 403,
  'into-itself': 400,
  'not-a-file': 400,
} as const satisfies Record<FileError['code'], number>;

function fsError(e: unknown): Error {
  if (e instanceof ApiError) return e;
  if (e instanceof FileError) return new ApiError(FILE_ERROR_STATUS[e.code], e.message);
  const code = (e as NodeJS.ErrnoException | null)?.code;
  if (code === 'ENOENT') return new ApiError(404, 'Not found');
  if (code === 'EACCES' || code === 'EPERM') return new ApiError(403, 'Permission denied');
  if (code === 'ENOTDIR') return new ApiError(400, 'Not a folder');
  if (code === 'EISDIR') return new ApiError(400, 'Not a file');
  if (code === 'EEXIST' || code === 'ENOTEMPTY') return new ApiError(409, 'That name is already taken');
  return e instanceof Error ? e : new Error(String(e));
}
