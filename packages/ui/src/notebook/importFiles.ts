/** A file to copy into the notebook (PRD §5), at its path relative to what was picked or dropped. */
export interface ImportFile {
  /** `/`-separated, starting with the picked or dropped item's own name: `Trip/photos/a.png`, or `a.md`. */
  readonly path: string;
  read(): Promise<Uint8Array>;
}

/** What an import did. */
export interface ImportResult {
  readonly imported: number;
  /** Files that couldn't be read or written. */
  readonly failed: number;
  /** Where the top-level items landed, in the notebook. */
  readonly roots: readonly string[];
}

/** Operating-system clutter and version-control folders an import leaves behind (PRD §5). */
const JUNK = new Set([
  '.DS_Store',
  '.Spotlight-V100',
  '.Trashes',
  '.fseventsd',
  '__MACOSX',
  'Thumbs.db',
  'desktop.ini',
  '.git',
]);

/** Whether an import skips `path`: OS clutter (`.DS_Store`, `._` resource forks, `Thumbs.db`) or `.git`. */
export function isImportJunk(path: string): boolean {
  return path.split('/').some((s) => JUNK.has(s) || s.startsWith('._'));
}

/**
 * A name from another disk as a notebook name: characters Puffinote refuses in names (\\ : * ? " < > |) become
 * `-`, and spaces at either end go. Null when nothing usable is left.
 */
export function importName(name: string): string | null {
  const clean = name.replace(/[\\:*?"<>|]/g, '-').trim();
  return clean === '' || clean === '.' || clean === '..' ? null : clean;
}

/** Count files for a message: "1 file", "3 files". */
export function fileCount(n: number): string {
  return `${n} ${n === 1 ? 'file' : 'files'}`;
}

/** Files from a file input or a drop, keyed by the path the browser gives them. */
export function importFileOf(file: Blob & { readonly name: string }, path?: string): ImportFile {
  const relative = (file as { webkitRelativePath?: string }).webkitRelativePath;
  return {
    path: path ?? (relative || file.name),
    read: async () => new Uint8Array(await file.arrayBuffer()),
  };
}
