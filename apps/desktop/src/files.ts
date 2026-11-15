// File-system helpers for the main process. No Electron imports, so the tests can run them in Node.
import {
  FileError,
  FileLocks,
  isInside,
  listFolder,
  moveEntry as moveShared,
  TEMP_FILE,
  writeAtomic,
} from '@puffinote/node-files';
import { statSync, watch, type FSWatcher } from 'node:fs';
import { mkdir, open, readFile, readlink, realpath, stat } from 'node:fs/promises';
import path from 'node:path';
import { CONFLICT_CHANGED, CONFLICT_EXISTS, NOT_FOUND, type NativeEntry } from './bridge.ts';

/** Only the app's own pages may use the bridge (not a navigated-away or injected frame). */
export function isAppUrl(url: string | undefined | null): boolean {
  return typeof url === 'string' && url.startsWith('app://puffinote/');
}

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
 * A file's version for conflict checks: its mtime, size and inode. The mtime alone can't tell a write from
 * another app on a disk with coarse times (one second, or two on FAT), and an atomic save there brings a new
 * inode even when the size and time match.
 */
export function fileVersion(s: { mtimeMs: number; size: number; ino: number }): string {
  return `${s.mtimeMs}:${s.size}:${s.ino}`;
}

/** Whether two `fileVersion`s are the same file state: the size and inode match, the mtime within 1 ms. */
export function sameVersion(a: string, b: string): boolean {
  const [am, as, ai] = a.split(':');
  const [bm, bs, bi] = b.split(':');
  return as === bs && ai === bi && Math.abs(Number(am) - Number(bm)) <= 1;
}

/** One save, move or other change per file at a time (`FileLocks`). */
const locks = new FileLocks();

/** Run `fn` once every earlier save or move of the same file has settled. */
export function withFileLock<T>(p: string, fn: () => Promise<T>): Promise<T> {
  return locks.run(p, fn);
}

/** What a save expects of the file it replaces, and which paths it may write. */
export interface SaveOptions {
  /** The version the renderer read; the save is refused if the file isn't at it any more (unless `force`). */
  readonly expected: string | null;
  readonly force: boolean;
  /** A new note: refused if the file exists. */
  readonly create: boolean;
  /** Is the path still inside a granted folder? Asked again once the save has its turn. */
  readonly allows: (p: string) => Promise<boolean>;
}

/**
 * Save a notebook file atomically, one save per file at a time, and return the version of what it wrote.
 * Refuses if the file isn't at version `expected` any more (unless `force`), or exists when `create` is set.
 * The grant is checked again once the save holds the file's lock, and again after making its folders: while
 * it waited, a folder on the path may have been swapped for a link out of the notebook.
 */
export function saveFile(p: string, bytes: Uint8Array, opts: SaveOptions): Promise<string> {
  const { expected, force, create, allows } = opts;
  return locks.run(p, async () => {
    if (!(await allows(p))) throw new Error('Forbidden');
    const s = await stat(p).catch(() => null);
    if (create && s) throw new Error(`${CONFLICT_EXISTS}: ${path.basename(p)}`);
    if (!force && expected !== null && (!s || !sameVersion(fileVersion(s), expected)))
      throw new Error(`${CONFLICT_CHANGED}: the file changed on disk`);
    await mkdir(path.dirname(p), { recursive: true });
    if (!(await allows(p))) throw new Error('Forbidden');
    return fileVersion(await writeAtomic(p, bytes));
  });
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

/**
 * The entries of a notebook folder for the renderer, `dir` itself being one `allows` accepts. A symlink is
 * followed only while it stays in the notebook (`listFolder` has the rest).
 */
export async function listDir(dir: string, allows: (p: string) => Promise<boolean>): Promise<NativeEntry[]> {
  if (!(await allows(dir))) throw new Error('Forbidden');
  const found = await listFolder(dir, allows);
  // The plain entries trust that the folder is allowed: check it again now they are read, in case it was
  // swapped for a link out meanwhile.
  if (!(await allows(dir))) throw new Error('Forbidden');
  return found;
}

/**
 * Rename or move a file or folder inside a notebook. A notebook folder itself (`isRoot`) never moves: the
 * renderer only moves things within one. The refusals become the markers the renderer understands.
 */
export async function moveEntry(src: string, dest: string, isRoot: (p: string) => boolean): Promise<void> {
  if (isRoot(src) || isRoot(dest)) throw new Error('Forbidden');
  try {
    await moveShared(src, dest, locks);
  } catch (e) {
    if (!(e instanceof FileError)) throw e;
    if (e.code === 'not-found') throw new Error(`${NOT_FOUND}: ${path.basename(src)}`, { cause: e });
    if (e.code === 'exists') throw new Error(`${CONFLICT_EXISTS}: ${path.basename(dest)}`, { cause: e });
    throw new Error(e.message, { cause: e });
  }
}

/**
 * A file's bytes and the mtime and version of exactly those bytes (one handle, so no write can come between).
 */
export async function readWithMtime(
  file: string,
): Promise<{ bytes: Uint8Array; modified: number; version: string }> {
  const h = await open(file, 'r');
  try {
    const s = await h.stat();
    const bytes = await h.readFile();
    return { bytes: new Uint8Array(bytes), modified: s.mtimeMs, version: fileVersion(s) };
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
    const root = this.#folders.find((f) => isInside(f, abs));
    if (!root) return false;
    const realRoot = await realpath(root).catch(() => root);
    // Walk up to the nearest part that exists, but follow a dangling symlink on the way: a save through
    // it (writeAtomic) lands where it points, so that is where the check must look.
    let probe = abs;
    const tail: string[] = [];
    for (let hops = 0; ;) {
      const real = await realpath(probe).catch(() => null);
      if (real !== null) return isInside(realRoot, path.join(real, ...tail));
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

/** The file name to suggest for a PDF: the name the page asked for, without folders, ending in `.pdf`. */
export function pdfFileName(name: unknown): string {
  const base = typeof name === 'string' ? path.basename(name.replace(/\\/g, '/')).trim() : '';
  if (!base || base === '.pdf') return 'Note.pdf';
  return /\.pdf$/i.test(base) ? base : `${base}.pdf`;
}
