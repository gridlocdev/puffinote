import { lstat, mkdir, readdir, realpath, rename, stat } from 'node:fs/promises';
import path from 'node:path';
import { syncDir, TEMP_FILE } from './atomicWrite.ts';
import { FileError } from './errors.ts';
import type { FileLocks } from './locks.ts';
import { isInside } from './paths.ts';

/** How many entries a listing stats at once: enough to hide disk latency, few enough not to hog it. */
const LIST_CONCURRENCY = 32;

/** One entry of a folder listing. */
export interface FolderEntry {
  readonly name: string;
  /** Absolute path. */
  readonly path: string;
  readonly kind: 'file' | 'dir';
  readonly size: number;
  readonly modified: number;
  /** Birth time where the file system keeps one (0 otherwise). */
  readonly created: number;
}

/**
 * The files and folders in `dir`, stat'ed a few dozen at a time, in no set order. Our own temp files are left
 * out. A symlink is followed for its kind only if `linkAllowed` says so (it stays inside the notebook) and it
 * doesn't dangle. A folder link back to the listed folder, a folder above it, or a folder holding one is left
 * out too: the client loads the tree recursively, and following it would never end. The caller checks `dir`
 * itself, before and after: the plain entries trust it.
 */
export async function listFolder(
  dir: string,
  linkAllowed: (p: string) => Promise<boolean>,
): Promise<FolderEntry[]> {
  const dirents = await readdir(dir, { withFileTypes: true });
  const above = await realAncestors(dir);
  const found = await mapLimit(dirents, LIST_CONCURRENCY, async (d): Promise<FolderEntry | null> => {
    if (TEMP_FILE.test(d.name)) return null;
    const p = path.join(dir, d.name);
    // lstat, not the dirent's type: an entry swapped for a symlink since readdir still gets checked.
    let s = await lstat(p).catch(() => null);
    if (s?.isSymbolicLink()) {
      s = (await linkAllowed(p)) ? await stat(p).catch(() => null) : null;
      if (s?.isDirectory() && (await closesLoop(p, above))) return null;
    }
    if (!s || !(s.isFile() || s.isDirectory())) return null;
    return {
      name: d.name,
      path: p,
      kind: s.isDirectory() ? 'dir' : 'file',
      size: s.isDirectory() ? 0 : s.size,
      modified: s.mtimeMs,
      created: s.birthtimeMs,
    };
  });
  return found.filter((e): e is FolderEntry => e !== null);
}

/** `items.map(fn)`, with at most `limit` calls in flight at once; the results keep the items' order. */
export async function mapLimit<T, R>(
  items: readonly T[],
  limit: number,
  fn: (item: T) => Promise<R>,
): Promise<R[]> {
  const out = new Array<R>(items.length);
  let next = 0;
  const worker = async (): Promise<void> => {
    while (next < items.length) {
      const i = next++;
      out[i] = await fn(items[i]!);
    }
  };
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker));
  return out;
}

/** The real paths of `dir` and every folder above it. */
async function realAncestors(dir: string): Promise<string[]> {
  const out: string[] = [];
  for (let p = dir; ; p = path.dirname(p)) {
    const real = await realpath(p).catch(() => null);
    if (real !== null) out.push(real);
    if (path.dirname(p) === p) return out;
  }
}

/** Does the folder link at `p` lead to one of `above` or a folder holding it? (Dangling counts as yes.) */
async function closesLoop(p: string, above: readonly string[]): Promise<boolean> {
  const real = await realpath(p).catch(() => null);
  return real === null || above.some((a) => isInside(real, a));
}

/**
 * Rename or move a file or folder. A taken name is refused, except a change of case on one file. Runs under
 * both paths' locks, so a save in flight can't land on the old path after the move, and a new file can't
 * appear at the destination between the check and the rename. Which paths may move at all is the caller's
 * to decide.
 */
export async function moveEntry(src: string, dest: string, locks: FileLocks): Promise<void> {
  if (dest === src) return;
  if (isInside(src, dest)) throw new FileError('into-itself', 'A folder can’t move into itself');
  await locks.runAll([src, dest], async () => {
    if (!(await lstat(src).catch(() => null))) throw new FileError('not-found', `${path.basename(src)}`);
    const there = await realpath(dest).catch(() => null);
    if (there !== null && there !== (await realpath(src).catch(() => null)))
      throw new FileError('exists', `“${path.basename(dest)}” already exists there`);
    await mkdir(path.dirname(dest), { recursive: true });
    await rename(src, dest);
    await syncDir(path.dirname(dest));
    if (path.dirname(src) !== path.dirname(dest)) await syncDir(path.dirname(src));
  });
}
