import { randomBytes } from 'node:crypto';
import {
  closeSync,
  existsSync,
  fchmodSync,
  fsyncSync,
  openSync,
  readdirSync,
  readFileSync,
  realpathSync,
  renameSync,
  rmSync,
  statSync,
  writeFileSync,
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

/**
 * Write `text` to `output`, or to stdout when it is `-`. A file is replaced in one step (a temp file
 * renamed over it, the same `.name.<hex>.tmp` shape the desktop app's watcher ignores), so a reader
 * never sees half of it.
 */
export function saveText(con: Console, text: string, output: string): void {
  if (output === '-') {
    con.out(text);
    return;
  }
  // Follow a symlink, so the link stays and the file it names is replaced.
  const target = existsSync(output) ? realpathSync(output) : path.resolve(output);
  const existing = existsSync(target) ? statSync(target) : null;
  const mode = existing ? existing.mode & 0o7777 : 0o644;
  const temp = path.join(
    path.dirname(target),
    `.${path.basename(target)}.${randomBytes(8).toString('hex')}.tmp`,
  );
  try {
    const fd = openSync(temp, 'wx', mode);
    try {
      fchmodSync(fd, mode); // open() applies the umask; the replacement keeps the original bits
      writeFileSync(fd, text);
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
