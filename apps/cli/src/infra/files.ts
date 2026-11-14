import { randomBytes } from 'node:crypto';
import {
  accessSync,
  closeSync,
  constants as fsConstants,
  existsSync,
  fchmodSync,
  fstatSync,
  ftruncateSync,
  fsyncSync,
  openSync,
  readdirSync,
  readFileSync,
  realpathSync,
  renameSync,
  rmSync,
  statSync,
  writeFileSync,
  writeSync,
} from 'node:fs';
import path from 'node:path';
import { TextDocument, readText } from '@puffinote/text';
import { CliError } from './errors.ts';
import type { Console } from './io.ts';

/** A note's text: from a file, or from stdin when the path is `-`. */
export function loadDocument(con: Console, file: string): TextDocument {
  if (file === '-') return TextDocument.fromString(readText(con.io.readStdin()).text);
  if (!existsSync(file) || !statSync(file).isFile()) throw new CliError(`File not found: ${file}`);
  return TextDocument.fromString(readText(readFileSync(file)).text);
}

/** The HTML title for a note: its file name without the extension, or "stdin". */
export function titleFor(file: string): string {
  return file === '-' ? 'stdin' : path.parse(file).name;
}

/** Do two paths name one file? Compared by device and inode, so symlinks, hard links and case don't hide it. */
export function sameFile(a: string, b: string): boolean {
  if (a === '-' || b === '-' || !existsSync(a) || !existsSync(b)) return false;
  const x = statSync(a);
  const y = statSync(b);
  return x.dev === y.dev && x.ino === y.ino;
}

/**
 * Refuse an `html -o` that would destroy a note: writing over the input itself (never, even with
 * `--force`), or writing to a name that isn't `.html`/`.htm` (only with `--force`).
 */
export function checkHtmlOutput(input: string, output: string, force: boolean): void {
  if (output === '-') return;
  if (sameFile(input, output))
    throw new CliError(`${output} is the same file as the note; choose another output.`);
  const ext = path.extname(output).toLowerCase();
  if (!force && ext !== '.html' && ext !== '.htm')
    throw new CliError(`${output} doesn't end in .html or .htm; pass --force to write it anyway.`);
}

/**
 * Write `text` to `output`, or to stdout when it is `-`. A file is replaced in one step (a temp file
 * renamed over it, the same `.name.<hex>.tmp` shape the desktop app's watcher ignores), so a reader
 * never sees half of it. A hard-linked file is written in place instead (`overwriteInPlaceSync`).
 */
export function saveText(con: Console, text: string, output: string): void {
  if (output === '-') {
    con.out(text);
    return;
  }
  // Follow a symlink, so the link stays and the file it names is replaced.
  const target = existsSync(output) ? realpathSync(output) : path.resolve(output);
  const existing = existsSync(target) ? statSync(target) : null;
  // A rename would replace a read-only file as long as its folder is writable: refuse instead.
  if (existing && isReadOnly(target, existing.mode)) throw new CliError(`${output} is read-only.`);
  const bytes = Buffer.from(text);
  if (existing && existing.nlink > 1) {
    // A rename would split the hard link; write through it instead (not atomic, but keeps the link).
    const fd = openSync(target, 'r+');
    try {
      overwriteInPlaceSync(
        {
          write: (b, o, l, p) => writeSync(fd, b, o, l, p),
          truncate: (l) => ftruncateSync(fd, l),
        },
        bytes,
        fstatSync(fd).size,
      );
      fsyncSync(fd);
    } finally {
      closeSync(fd);
    }
    return;
  }
  const mode = existing ? existing.mode & 0o7777 : 0o644;
  const temp = path.join(
    path.dirname(target),
    `.${path.basename(target)}.${randomBytes(8).toString('hex')}.tmp`,
  );
  try {
    const fd = openSync(temp, 'wx', mode);
    try {
      fchmodSync(fd, mode); // open() applies the umask; the replacement keeps the original bits
      writeFileSync(fd, bytes);
      fsyncSync(fd);
    } finally {
      closeSync(fd);
    }
    renameSync(temp, target);
  } catch (e) {
    rmSync(temp, { force: true });
    throw e;
  }
}

/**
 * Write `bytes` over a file in place, for a hard-linked file that a rename would split from its other names.
 * It can't be atomic, but it never empties the file: the part past the old end is written first (and cut
 * back off if the disk is full, leaving the old file as it was), then the old length is overwritten from
 * offset 0, which needs no new space, and only then is a longer old tail cut off.
 */
export function overwriteInPlaceSync(
  h: {
    write(buf: Uint8Array, offset: number, length: number, position: number): number;
    truncate(len: number): void;
  },
  bytes: Uint8Array,
  oldSize: number,
): void {
  const writeRange = (from: number, to: number): void => {
    for (let at = from; at < to;) {
      const written = h.write(bytes, at, to - at, at);
      if (written <= 0) throw new Error('Short write');
      at += written;
    }
  };
  if (bytes.length > oldSize) {
    try {
      writeRange(oldSize, bytes.length);
    } catch (e) {
      try {
        h.truncate(oldSize);
      } catch {
        // The error that matters is the write's.
      }
      throw e;
    }
  }
  writeRange(0, Math.min(oldSize, bytes.length));
  if (bytes.length < oldSize) h.truncate(bytes.length);
}

/** No write bit at all (root could still write it), or not writable by this process. */
function isReadOnly(file: string, mode: number): boolean {
  if ((mode & 0o222) === 0) return true;
  try {
    accessSync(file, fsConstants.W_OK);
    return false;
  } catch {
    return true;
  }
}

/** A notebook argument as an absolute folder path. */
export function resolveNotebook(folder: string): string {
  const full = path.resolve(folder);
  if (!existsSync(full) || !statSync(full).isDirectory()) throw new CliError(`Notebook not found: ${folder}`);
  return full;
}

/** One entry of a notebook walk. */
export interface NotebookEntry {
  readonly path: string;
  readonly name: string;
  readonly depth: number;
  readonly isDirectory: boolean;
}

/**
 * Every entry under `root` in the sidebar's order: a folder's contents right after it, folders
 * before files, each case-insensitively alphabetical. Dotfiles (and with them `.puffinote/`) are
 * skipped. Symlinks are followed, but a folder already on the walk is not entered twice, so a link
 * cycle ends. An unreadable folder contributes nothing instead of stopping the walk.
 */
export function walkNotebook(root: string): NotebookEntry[] {
  const out: NotebookEntry[] = [];
  const seen = new Set<string>();
  const visit = (dir: string, depth: number): void => {
    let real: string;
    let names: string[];
    try {
      real = realpathSync(dir);
      names = readdirSync(dir);
    } catch {
      return;
    }
    if (seen.has(real)) return;
    seen.add(real);
    const entries: { name: string; full: string; isDirectory: boolean }[] = [];
    for (const name of names) {
      if (name.startsWith('.')) continue;
      const full = path.join(dir, name);
      let isDirectory: boolean;
      try {
        isDirectory = statSync(full).isDirectory();
      } catch {
        continue; // a dangling link
      }
      entries.push({ name, full, isDirectory });
    }
    entries.sort((a, b) =>
      a.isDirectory !== b.isDirectory ? (a.isDirectory ? -1 : 1) : compareNames(a.name, b.name),
    );
    for (const e of entries) {
      out.push({ path: e.full, name: e.name, depth, isDirectory: e.isDirectory });
      if (e.isDirectory) visit(e.full, depth + 1);
    }
  };
  visit(root, 0);
  return out;
}

const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

/** The sidebar's order (case-insensitive, numbers by value), then ordinal so the order is total. */
export function compareNames(a: string, b: string): number {
  return collator.compare(a, b) || (a < b ? -1 : a > b ? 1 : 0);
}
