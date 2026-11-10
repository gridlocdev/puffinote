// File-system helpers for the main process. No Electron imports, so the tests can run them in Node.
import { randomBytes } from 'node:crypto';
import { statSync, watch, type FSWatcher } from 'node:fs';
import { chown, mkdir, open, readFile, readlink, realpath, rename, stat, unlink } from 'node:fs/promises';
import path from 'node:path';

/** Only the app's own pages may use the bridge (not a navigated-away or injected frame). */
export function isAppUrl(url: string | undefined | null): boolean {
  return typeof url === 'string' && url.startsWith('app://puffinote/');
}

/** Our own in-flight save files (`.<name>.<hex>.tmp`): never listed, never reported as changes. */
export const TEMP_FILE = /^\..+\.[0-9a-f]{16}\.tmp$/;

/** Folders named on a command line (`puffinote ~/Notes`), made absolute against the directory it ran in. */
export function folderArgs(argv: readonly string[], cwd: string): string[] {
  return argv
    .filter((a) => !a.startsWith('-'))
    .map((a) => path.resolve(cwd, a))
    .filter((p) => {
      try {
        return statSync(p).isDirectory();
      } catch {
        return false;
      }
    });
}

/**
 * Atomic save: a temp file beside the target, fsync, rename. A symlink is followed, so the link stays and its
 * target is replaced. The replacement keeps the target's mode (and owner, where permitted). A hard-linked
 * target is written in place instead, since a rename would split the link.
 */
export async function writeAtomic(p: string, bytes: Uint8Array): Promise<void> {
  const real = await realTarget(path.resolve(p));
  const target = await stat(real).catch(() => null);
  if (target && target.nlink > 1) {
    const h = await open(real, 'r+');
    try {
      await h.truncate(0);
      await h.writeFile(bytes);
      await h.sync();
    } finally {
      await h.close();
    }
    return;
  }
  const mode = target ? target.mode & 0o7777 : 0o644;
  const tmp = path.join(path.dirname(real), `.${path.basename(real)}.${randomBytes(8).toString('hex')}.tmp`);
  let created = false;
  try {
    const h = await open(tmp, 'wx', mode);
    created = true;
    try {
      await h.chmod(mode); // open() applies the umask
      await h.writeFile(bytes);
      await h.sync();
    } finally {
      await h.close();
    }
    if (target) await chown(tmp, target.uid, target.gid).catch(() => {});
    await rename(tmp, real);
  } catch (e) {
    if (created) await unlink(tmp).catch(() => {});
    throw e;
  }
}

/** The file a path names after following symlinks; a path that doesn't exist yet is its own target. */
async function realTarget(abs: string): Promise<string> {
  const real = await realpath(abs).catch(() => null);
  if (real !== null) return real;
  const link = await readlink(abs).catch(() => null); // dangling: write where it points
  return link === null ? abs : path.resolve(path.dirname(abs), link);
}

const fileLocks = new Map<string, Promise<unknown>>();

/**
 * Run `fn` after every earlier `withFileLock` call for the same file has settled, so a save's check-then-write
 * cannot interleave with another save of that file. Paths are compared resolved, and case-folded where the
 * volume usually is case-insensitive (macOS, Windows).
 */
export async function withFileLock<T>(p: string, fn: () => Promise<T>): Promise<T> {
  const abs = path.resolve(p);
  const key = process.platform === 'linux' ? abs : abs.normalize('NFC').toLowerCase();
  const prev = fileLocks.get(key) ?? Promise.resolve();
  const run = prev.then(fn, fn);
  const tail = run.catch(() => {});
  fileLocks.set(key, tail);
  try {
    return await run;
  } finally {
    if (fileLocks.get(key) === tail) fileLocks.delete(key);
  }
}

/**
 * Watch a notebook folder and everything in it. A burst of events becomes one call after `delayMs` with the
 * absolute paths that changed; our own temp files are left out.
 */
export function watchTree(root: string, onChange: (paths: string[]) => void, delayMs = 200): () => void {
  let timer: NodeJS.Timeout | null = null;
  const pending = new Set<string>();
  let watcher: FSWatcher | null = null;
  try {
    watcher = watch(root, { recursive: true }, (_event, filename) => {
      const name = filename?.toString() ?? '';
      if (TEMP_FILE.test(path.basename(name))) return;
      pending.add(name ? path.join(root, name) : root);
      if (timer) clearTimeout(timer);
      timer = setTimeout(() => {
        timer = null;
        const paths = [...pending];
        pending.clear();
        onChange(paths);
      }, delayMs);
    });
    watcher.on('error', () => {});
  } catch {
    watcher = null;
  }
  return () => {
    watcher?.close();
    watcher = null;
    if (timer) clearTimeout(timer);
    timer = null;
  };
}

function inside(parent: string, child: string): boolean {
  const rel = path.relative(parent, child);
  return rel === '' || (!rel.startsWith('..') && !path.isAbsolute(rel));
}

/**
 * The notebook folders the renderer may touch: ones the user picked in a dialog, opened from the OS (Dock,
 * command line), or opened before (persisted, so Recent still works). A path is allowed when it lies inside a
 * granted folder, also after following symlinks.
 */
export class FolderGrants {
  readonly #file: string | null;
  readonly #limit: number;
  #folders: string[] = [];
  /** Persisting waits for `load()`, so a grant made first (argv, open-file) can't overwrite the file. */
  #writing: Promise<void>;
  #loaded: () => void = () => {};
  #persistEnabled = true;

  constructor(file: string | null, limit = 50) {
    this.#file = file;
    this.#limit = limit;
    this.#writing = new Promise((resolve) => (this.#loaded = resolve));
  }

  async load(): Promise<void> {
    try {
      if (!this.#file) return;
      const data: unknown = JSON.parse(await readFile(this.#file, 'utf8'));
      if (!Array.isArray(data)) return;
      const loaded = data
        .filter((x): x is string => typeof x === 'string' && path.isAbsolute(x))
        .filter((x) => !this.#folders.includes(x));
      this.#folders = [...this.#folders, ...loaded].slice(0, this.#limit);
    } catch {
      // Missing or unreadable: start empty.
    } finally {
      this.#loaded();
    }
  }

  /** Keep grants in memory only (an instance that lost the single-instance lock and is quitting). */
  disablePersist(): void {
    this.#persistEnabled = false;
  }

  /** Is `p` a granted folder itself? */
  isRoot(p: unknown): p is string {
    return typeof p === 'string' && path.isAbsolute(p) && this.#folders.includes(path.resolve(p));
  }

  /**
   * Is `p` inside a granted folder? Checked on the path as given, then again with symlinks resolved (through
   * the nearest existing ancestor), so a link inside a notebook can't reach out of it.
   */
  async allows(p: unknown): Promise<boolean> {
    if (typeof p !== 'string' || !path.isAbsolute(p) || p.includes('\0')) return false;
    const abs = path.resolve(p);
    const root = this.#folders.find((f) => inside(f, abs));
    if (!root) return false;
    const realRoot = await realpath(root).catch(() => root);
    let probe = abs;
    for (;;) {
      const real = await realpath(probe).catch(() => null);
      if (real !== null) return inside(realRoot, real);
      const parent = path.dirname(probe);
      if (parent === probe) return false;
      probe = parent;
    }
  }

  grant(p: string): void {
    const abs = path.resolve(p);
    this.#folders = [abs, ...this.#folders.filter((x) => x !== abs)].slice(0, this.#limit);
    this.#writing = this.#writing.then(() => this.#persist());
  }

  /** Resolves once every grant so far is on disk (for tests). Waits for `load()` first. */
  flushed(): Promise<void> {
    return this.#writing;
  }

  async #persist(): Promise<void> {
    if (!this.#file || !this.#persistEnabled) return;
    try {
      await mkdir(path.dirname(this.#file), { recursive: true });
      await writeAtomic(this.#file, new TextEncoder().encode(JSON.stringify(this.#folders)));
    } catch {
      // Best effort: the grants still hold for this run.
    }
  }
}
