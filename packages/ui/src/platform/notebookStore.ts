/**
 * Where a notebook lives (PRD §5: a notebook is one root folder). The shell only ever talks to a
 * NotebookStore; it never knows whether the folder is in memory, picked in the browser, on the NAS server, or
 * on disk through Electron. Paths are notebook-relative (see `paths.ts`).
 */
export type StoreKind = 'memory' | 'browser' | 'nas' | 'native';

export interface Entry {
  readonly path: string;
  readonly name: string;
  readonly kind: 'file' | 'dir';
  /** Bytes; 0 for a folder. */
  readonly size: number;
  /** Milliseconds since the epoch; 0 when the store can't tell. */
  readonly modified: number;
}

export interface FileData {
  readonly bytes: Uint8Array;
  /** Opaque version for conflict-on-save: an etag, an mtime, a counter. Null when the store can't tell. */
  readonly version: string | null;
}

export interface WriteOptions {
  /** The version the caller last read. A different one on disk throws ConflictError('changed'). */
  readonly expected?: string | null;
  /** Overwrite whatever is there, changed or not. */
  readonly force?: boolean;
  /** The file must not exist yet (new note); an existing one throws ConflictError('exists'). */
  readonly create?: boolean;
}

export interface StoreCapabilities {
  /** The store reports outside changes through `watch`; without it the shell re-reads on focus. */
  readonly watch: boolean;
  /** "Reveal in Finder" / the file manager. */
  readonly reveal: boolean;
  /** "Open in default app"; without it, a file nobody here can open downloads instead. */
  readonly openExternal: boolean;
}

export interface NotebookStore {
  readonly kind: StoreKind;
  /** The notebook's display name (its folder's name). */
  readonly name: string;
  readonly capabilities: StoreCapabilities;
  /** The entries directly in `dir` (`''` for the root). */
  list(dir: string): Promise<Entry[]>;
  read(path: string): Promise<FileData>;
  /** Write a file, creating its parent folders. Returns the new version. */
  write(path: string, bytes: Uint8Array, opts?: WriteOptions): Promise<string | null>;
  mkdir(path: string): Promise<void>;
  /** Rename or move a file or folder. Throws ConflictError('exists') if `to` is taken. */
  move(from: string, to: string): Promise<void>;
  /** Delete a file, or a folder with everything in it. Stores with a trash use it. */
  remove(path: string): Promise<void>;
  /** A URL the page can load the file from directly (an image in a note), or null when it has to be read. */
  fileUrl(path: string): string | null;
  /** Call `onChange` with the paths that changed outside the app; returns the unsubscribe. */
  watch?(onChange: (paths: readonly string[]) => void): () => void;
  reveal?(path: string): Promise<void>;
  openExternal?(path: string): Promise<void>;
}

/** The file changed, is locked, or already exists. A forced write overwrites a changed file. */
export class ConflictError extends Error {
  readonly reason: 'changed' | 'locked' | 'exists';

  constructor(reason: 'changed' | 'locked' | 'exists', message: string) {
    super(message);
    this.name = 'ConflictError';
    this.reason = reason;
  }
}

export class NotFoundError extends Error {
  constructor(path: string) {
    super(`“${path}” isn’t in the notebook any more.`);
    this.name = 'NotFoundError';
  }
}
