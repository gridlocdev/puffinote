import {
  ConflictError,
  NotFoundError,
  type Entry,
  type FileData,
  type NotebookStore,
  type WriteOptions,
} from './notebookStore.ts';
import { baseName } from './paths.ts';

/** A directory entry from the main process, with an absolute path. */
export interface NativeEntry {
  readonly path: string;
  readonly name: string;
  readonly kind: 'file' | 'dir';
  readonly size: number;
  readonly modified: number;
}

export interface NativeMenuItem {
  readonly label?: string;
  readonly command?: string;
  readonly accelerator?: string;
  readonly enabled?: boolean;
  readonly checked?: boolean;
  readonly role?: string;
  readonly type?: 'normal' | 'separator' | 'checkbox' | 'submenu';
  readonly submenu?: readonly NativeMenuItem[];
}

/**
 * The Electron bridge (the desktop preload exposes it as `window.puffinoteNative`). Paths are absolute; the
 * NativeStore maps them to notebook paths. Errors cross IPC as messages; conflicts start with CONFLICT_*.
 */
export interface PuffinoteNative {
  readonly platform: string;
  pickFolder(): Promise<string | null>;
  list(dir: string): Promise<NativeEntry[]>;
  read(path: string): Promise<{ bytes: Uint8Array; modified: number }>;
  write(
    path: string,
    bytes: Uint8Array,
    expected: number | null,
    force: boolean,
    create: boolean,
  ): Promise<number>;
  mkdir(path: string): Promise<void>;
  move(from: string, to: string): Promise<void>;
  /** Moves to the OS trash. */
  trash(path: string): Promise<void>;
  reveal(path: string): Promise<void>;
  openExternal(path: string): Promise<void>;
  /** A URL the renderer can load the file from (a custom protocol in the desktop app). */
  fileUrl(path: string): string;
  /** Watch the notebook folder; `null` stops watching. */
  watch(root: string | null): void;
  onChanged(cb: (paths: readonly string[]) => void): () => void;
  setTitle(title: string, dirty: boolean): void;
  setMenu(menu: readonly NativeMenuItem[]): void;
  onCommand(cb: (id: string) => void): () => void;
  onCloseRequested(cb: () => void): () => void;
  confirmClose(): void;
  cancelClose(): void;
}

export const CONFLICT_CHANGED = 'PUFFINOTE_CONFLICT_CHANGED';
export const CONFLICT_EXISTS = 'PUFFINOTE_CONFLICT_EXISTS';
export const NOT_FOUND = 'PUFFINOTE_NOT_FOUND';

/** The Electron bridge, or null in a plain browser. */
export function nativeBridge(): PuffinoteNative | null {
  return (globalThis as { puffinoteNative?: PuffinoteNative }).puffinoteNative ?? null;
}

/** A notebook folder on disk, through Electron's main process. Versions are mtimes. */
export class NativeStore implements NotebookStore {
  readonly kind = 'native' as const;
  readonly name: string;
  readonly capabilities = { watch: true, reveal: true, openExternal: true };
  readonly bridge: PuffinoteNative;
  /** The notebook folder's absolute path, with `/` separators. */
  readonly root: string;

  constructor(bridge: PuffinoteNative, root: string) {
    this.bridge = bridge;
    this.root = root.replace(/[\\/]+$/, '');
    this.name = baseName(this.root.replace(/\\/g, '/'));
  }

  async list(dir: string): Promise<Entry[]> {
    const entries = await this.call(() => this.bridge.list(this.abs(dir)));
    return entries.map((e) => ({ ...e, path: dir ? `${dir}/${e.name}` : e.name }));
  }

  async read(path: string): Promise<FileData> {
    const f = await this.call(() => this.bridge.read(this.abs(path)));
    return { bytes: f.bytes, version: String(f.modified) };
  }

  async write(path: string, bytes: Uint8Array, opts: WriteOptions = {}): Promise<string | null> {
    const expected = opts.expected == null ? null : Number(opts.expected);
    const m = await this.call(() =>
      this.bridge.write(this.abs(path), bytes, expected, opts.force ?? false, opts.create ?? false),
    );
    return String(m);
  }

  mkdir(path: string): Promise<void> {
    return this.call(() => this.bridge.mkdir(this.abs(path)));
  }

  move(from: string, to: string): Promise<void> {
    return this.call(() => this.bridge.move(this.abs(from), this.abs(to)));
  }

  remove(path: string): Promise<void> {
    return this.call(() => this.bridge.trash(this.abs(path)));
  }

  fileUrl(path: string): string | null {
    return this.bridge.fileUrl(this.abs(path));
  }

  watch(onChange: (paths: readonly string[]) => void): () => void {
    this.bridge.watch(this.root);
    const off = this.bridge.onChanged((paths) => {
      const prefix = this.root + '/';
      onChange(
        paths
          .map((p) => p.replace(/\\/g, '/'))
          .filter((p) => p.startsWith(prefix))
          .map((p) => p.slice(prefix.length)),
      );
    });
    return () => {
      off();
      this.bridge.watch(null);
    };
  }

  reveal(path: string): Promise<void> {
    return this.bridge.reveal(this.abs(path));
  }

  openExternal(path: string): Promise<void> {
    return this.bridge.openExternal(this.abs(path));
  }

  private abs(path: string): string {
    return path ? `${this.root}/${path}` : this.root;
  }

  private async call<T>(fn: () => Promise<T>): Promise<T> {
    try {
      return await fn();
    } catch (e) {
      const message = e instanceof Error ? e.message : String(e);
      if (message.includes(CONFLICT_CHANGED)) {
        throw new ConflictError('changed', 'The file changed on disk since it was opened.');
      }
      if (message.includes(CONFLICT_EXISTS))
        throw new ConflictError('exists', 'That name is already taken there.');
      if (message.includes(NOT_FOUND)) throw new NotFoundError(message.replace(/.*NOT_FOUND:?\s*/, ''));
      throw e;
    }
  }
}
