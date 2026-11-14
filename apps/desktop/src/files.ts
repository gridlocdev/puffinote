// File-system helpers for the main process. No Electron imports, so the tests can run them in Node.
import { randomBytes } from 'node:crypto';
import { statSync, watch, type FSWatcher } from 'node:fs';
import {
  access,
  chown,
  constants as fsConstants,
  mkdir,
  open,
  readFile,
  readlink,
  realpath,
  rename,
  stat,
  unlink,
} from 'node:fs/promises';
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
 * target is written in place instead (`overwriteInPlace`), since a rename would split the link.
 */
export async function writeAtomic(p: string, bytes: Uint8Array): Promise<void> {
  const real = await realTarget(path.resolve(p));
  const target = await stat(real).catch(() => null);
  // A rename would replace a read-only file as long as its folder is writable: refuse instead.
  if (target && (await isReadOnly(real, target.mode))) throw new Error(`“${path.basename(p)}” is read-only`);
  if (target && target.nlink > 1) {
    const h = await open(real, 'r+');
    try {
      await overwriteInPlace(h, bytes, (await h.stat()).size);
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
  await syncDir(path.dirname(real));
}

/**
 * Write `bytes` over a file in place, for a hard-linked file that a rename would split from its other names.
 * It can't be atomic, but it never empties the file: the part past the old end is written first (and cut
 * back off if the disk is full, leaving the old file as it was), then the old length is overwritten from
 * offset 0, which needs no new space, and only then is a longer old tail cut off.
 */
export async function overwriteInPlace(
  h: {
    write(
      buf: Uint8Array,
      offset: number,
      length: number,
      position: number,
    ): Promise<{ bytesWritten: number }>;
    truncate(len: number): Promise<void>;
  },
  bytes: Uint8Array,
  oldSize: number,
): Promise<void> {
  const writeRange = async (from: number, to: number): Promise<void> => {
    for (let at = from; at < to;) {
      const { bytesWritten } = await h.write(bytes, at, to - at, at);
      if (bytesWritten <= 0) throw new Error('Short write');
      at += bytesWritten;
    }
  };
  if (bytes.length > oldSize) {
    try {
      await writeRange(oldSize, bytes.length);
    } catch (e) {
      await h.truncate(oldSize).catch(() => {});
      throw e;
    }
  }
  await writeRange(0, Math.min(oldSize, bytes.length));
  if (bytes.length < oldSize) await h.truncate(bytes.length);
}

/** No write bit at all (root could still write it), or not writable by this process. */
async function isReadOnly(file: string, mode: number): Promise<boolean> {
  if ((mode & 0o222) === 0) return true;
  return access(file, fsConstants.W_OK).then(
    () => false,
    () => true,
  );
}

/**
 * Make a rename in `dir` durable: without this, a power cut soon after a save can bring the old file back.
 * Windows can't open a folder, so this is best effort.
 */
export async function syncDir(dir: string): Promise<void> {
  const h = await open(dir, 'r').catch(() => null);
  if (!h) return;
  await h.sync().catch(() => {});
  await h.close();
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
  const key = lockKey(p);
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

function lockKey(p: string): string {
  const abs = path.resolve(p);
  return process.platform === 'linux' ? abs : abs.normalize('NFC').toLowerCase();
}

/** `withFileLock` over several files, taken in one order so two callers can't wait on each other. */
export function withFileLocks<T>(paths: readonly string[], fn: () => Promise<T>): Promise<T> {
  const byKey = new Map(paths.map((p) => [lockKey(p), p]));
  const sorted = [...byKey.keys()].sort().map((k) => byKey.get(k));
  const run = (i: number): Promise<T> => {
    const p = sorted[i];
    return p === undefined ? fn() : withFileLock(p, () => run(i + 1));
  };
  return run(0);
}

/**
 * Document types that "Open in Default App" hands to the OS. Anything else (an `.app` bundle, a `.command`
 * script, an `.exe`, a shortcut) could run code from a synced or shared notebook, so it is only revealed in the
 * file manager.
 */
const OPENABLE = new Set(
  (
    'pdf txt text md markdown rtf csv tsv json xml html htm log yaml yml toml ' +
    'png jpg jpeg gif webp avif svg bmp tif tiff heic heif ico ' +
    'mp3 m4a aac wav flac ogg opus mp4 m4v mov webm mkv avi ' +
    'doc docx xls xlsx ppt pptx odt ods odp odg pages numbers key epub'
  ).split(' '),
);

/**
 * May `p` be opened in its default app? Only a regular file whose name, and whose real name after following
 * symlinks, has a document extension. Folders are refused: on macOS an `.app` is one.
 */
export async function isOpenableDocument(p: string): Promise<boolean> {
  const real = await realpath(p).catch(() => null);
  if (real === null) return false;
  const s = await stat(real).catch(() => null);
  if (!s?.isFile()) return false;
  const ext = (name: string) => path.extname(name).slice(1).toLowerCase();
  return OPENABLE.has(ext(p)) && OPENABLE.has(ext(real));
}

/** A file's bytes and the mtime of exactly those bytes (one handle, so no write can come between). */
export async function readWithMtime(file: string): Promise<{ bytes: Uint8Array; modified: number }> {
  const h = await open(file, 'r');
  try {
    const s = await h.stat();
    const bytes = await h.readFile();
    return { bytes: new Uint8Array(bytes), modified: s.mtimeMs };
  } finally {
    await h.close();
  }
}

/**
 * Watch a notebook folder and everything in it. A burst of events becomes one call after `delayMs` with the
 * absolute paths that changed; our own temp files are left out. A steady stream of events (a sync tool, a
 * build writing into the folder) still reports every `maxWaitMs`. If the watcher fails (too many open files,
 * the folder renamed), it is started again after `retryMs` and the whole folder is reported as changed,
 * since events were missed.
 */
export function watchTree(
  root: string,
  onChange: (paths: string[]) => void,
  delayMs = 200,
  { maxWaitMs = 2000, retryMs = 2000 } = {},
): () => void {
  let timer: NodeJS.Timeout | null = null;
  let firstPending = 0;
  let retry: NodeJS.Timeout | null = null;
  let stopped = false;
  const pending = new Set<string>();
  let watcher: FSWatcher | null = null;

  const flush = (): void => {
    if (timer) clearTimeout(timer);
    timer = null;
    const paths = [...pending];
    pending.clear();
    if (paths.length > 0 && !stopped) onChange(paths);
  };
  const note = (p: string): void => {
    if (pending.size === 0) firstPending = Date.now();
    pending.add(p);
    if (timer) clearTimeout(timer);
    const wait = Math.max(0, Math.min(delayMs, firstPending + maxWaitMs - Date.now()));
    timer = setTimeout(flush, wait);
  };
  const start = (): void => {
    retry = null;
    if (stopped) return;
    try {
      watcher = watch(root, { recursive: true }, (_event, filename) => {
        const name = filename?.toString() ?? '';
        if (TEMP_FILE.test(path.basename(name))) return;
        note(name ? path.join(root, name) : root);
      });
      watcher.on('error', () => {
        watcher?.close();
        watcher = null;
        if (!stopped) retry = setTimeout(restart, retryMs);
      });
    } catch {
      watcher = null;
      retry = setTimeout(restart, retryMs);
    }
  };
  const restart = (): void => {
    start();
    if (watcher) note(root);
  };
  start();
  return () => {
    stopped = true;
    watcher?.close();
    watcher = null;
    if (timer) clearTimeout(timer);
    timer = null;
    if (retry) clearTimeout(retry);
    retry = null;
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
    // Walk up to the nearest part that exists, but follow a dangling symlink on the way: a save through
    // it (writeAtomic) lands where it points, so that is where the check must look.
    let probe = abs;
    const tail: string[] = [];
    for (let hops = 0; ;) {
      const real = await realpath(probe).catch(() => null);
      if (real !== null) return inside(realRoot, path.join(real, ...tail));
      const link = await readlink(probe).catch(() => null);
      if (link !== null) {
        if (++hops > 40) return false; // a loop
        probe = path.resolve(path.dirname(probe), link);
        continue;
      }
      const parent = path.dirname(probe);
      if (parent === probe) return false;
      tail.unshift(path.basename(probe));
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
