import {
  ConflictError,
  NotFoundError,
  type Entry,
  type FileData,
  type NotebookStore,
  type WriteOptions,
} from './notebookStore.ts';
import { baseName, parentOf } from './paths.ts';

interface PermissionHandle {
  queryPermission?(d: { mode: 'read' | 'readwrite' }): Promise<PermissionState>;
  requestPermission?(d: { mode: 'read' | 'readwrite' }): Promise<PermissionState>;
}

interface PickerWindow {
  showDirectoryPicker?: (opts?: {
    mode?: 'read' | 'readwrite';
    id?: string;
  }) => Promise<FileSystemDirectoryHandle>;
}

/** True when this browser can open a folder from disk (Chromium's File System Access API). */
export function canPickFolder(): boolean {
  return typeof (globalThis as PickerWindow).showDirectoryPicker === 'function';
}

/** Ask the user for a folder to open as the notebook. Null when cancelled. */
export async function pickFolder(): Promise<FileSystemDirectoryHandle | null> {
  const picker = (globalThis as PickerWindow).showDirectoryPicker;
  if (!picker) return null;
  try {
    return await picker({ mode: 'readwrite', id: 'puffinote-notebook' });
  } catch (e) {
    if (e instanceof DOMException && e.name === 'AbortError') return null;
    throw e;
  }
}

/**
 * The browser's private notebook folder (the origin-private file system): every modern browser has one, so a
 * browser that can't open a folder from disk still gets a notebook that survives a reload.
 */
export async function privateFolder(): Promise<FileSystemDirectoryHandle | null> {
  try {
    const root = await navigator.storage.getDirectory();
    return await root.getDirectoryHandle('notebook', { create: true });
  } catch {
    return null;
  }
}

/** Make sure we may write to `handle`, asking only when `ask` (a prompt needs a user gesture). */
export async function ensurePermission(handle: FileSystemHandle, ask: boolean): Promise<boolean> {
  const h = handle as FileSystemHandle & PermissionHandle;
  if (!h.queryPermission) return true;
  if ((await h.queryPermission({ mode: 'readwrite' })) === 'granted') return true;
  return ask && (await h.requestPermission?.({ mode: 'readwrite' })) === 'granted';
}

/**
 * A notebook in a browser folder handle: one the user picked, or the private folder. Versions are the file's
 * lastModified, re-checked before each write. There is no change notification, so the shell re-reads the tree
 * when the window regains focus.
 */
export class BrowserStore implements NotebookStore {
  readonly kind = 'browser' as const;
  readonly name: string;
  readonly capabilities = { watch: false, reveal: false, openExternal: false };
  readonly root: FileSystemDirectoryHandle;
  /** The private folder, which has no name the user chose. */
  readonly isPrivate: boolean;

  constructor(root: FileSystemDirectoryHandle, isPrivate = false) {
    this.root = root;
    this.isPrivate = isPrivate;
    this.name = isPrivate ? 'Browser notebook' : root.name;
  }

  async list(dir: string): Promise<Entry[]> {
    const handle = await this.dir(dir, false);
    const out: Entry[] = [];
    for await (const [name, h] of handle.entries()) {
      const path = dir ? `${dir}/${name}` : name;
      if (h.kind === 'directory') out.push({ path, name, kind: 'dir', size: 0, modified: 0 });
      else {
        const f = await (h as FileSystemFileHandle).getFile();
        out.push({ path, name, kind: 'file', size: f.size, modified: f.lastModified });
      }
    }
    return out;
  }

  async read(path: string): Promise<FileData> {
    const f = await (await this.file(path, false)).getFile();
    return { bytes: new Uint8Array(await f.arrayBuffer()), version: String(f.lastModified) };
  }

  async write(path: string, bytes: Uint8Array, opts: WriteOptions = {}): Promise<string | null> {
    const dir = await this.dir(parentOf(path), true);
    const name = baseName(path);
    const handle = await dir.getFileHandle(name).catch(() => null);
    if (handle && opts.create) throw new ConflictError('exists', `“${name}” already exists.`);
    if (!handle && !opts.force && opts.expected != null)
      throw new ConflictError('deleted', 'The file was deleted since it was opened.');
    if (handle && !opts.force && opts.expected != null) {
      const current = String((await handle.getFile()).lastModified);
      if (current !== opts.expected)
        throw new ConflictError('changed', 'The file changed since it was opened.');
    }
    const target = handle ?? (await dir.getFileHandle(name, { create: true }));
    const w = await target.createWritable();
    await w.write(bytes as Uint8Array<ArrayBuffer>);
    await w.close();
    return String((await target.getFile()).lastModified);
  }

  async mkdir(path: string): Promise<void> {
    await this.dir(path, true);
  }

  async move(from: string, to: string): Promise<void> {
    if (from === to) return;
    const source = await this.entry(from);
    const target = await this.entry(to).catch(() => null);
    if (target) {
      if (!(await this.sameEntry(source, target, from, to)))
        throw new ConflictError('exists', `“${baseName(to)}” already exists there.`);
      // The same file under another spelling: a case-only rename on a case-insensitive disk. Copying it onto
      // itself and deleting the original would delete it, so it goes by a free temporary name instead.
      const temp = await this.freeName(from);
      await this.copyThenRemove(from, temp);
      try {
        await this.copyThenRemove(temp, to);
      } catch (e) {
        await this.copyThenRemove(temp, from).catch(() => undefined);
        throw e;
      }
      return;
    }
    await this.copyThenRemove(from, to);
  }

  async remove(path: string): Promise<void> {
    const parent = await this.dir(parentOf(path), false);
    await parent.removeEntry(baseName(path), { recursive: true });
  }

  fileUrl(): string | null {
    return null;
  }

  /**
   * Handle.move() isn't everywhere and doesn't move folders in every engine: copy, then delete. `to` must be
   * free. A copy that fails is cleaned up and the original stays; the original goes only after a full copy.
   */
  private async copyThenRemove(from: string, to: string): Promise<void> {
    try {
      await this.copy(from, to);
    } catch (e) {
      await this.remove(to).catch(() => undefined);
      throw e;
    }
    await this.remove(from);
  }

  private async sameEntry(a: FileSystemHandle, b: FileSystemHandle, aPath: string, bPath: string) {
    if (typeof a.isSameEntry === 'function') return a.isSameEntry(b);
    // No isSameEntry: the disk found `bPath` under `aPath`'s name, so it is the same entry when only the case
    // differs.
    return (
      parentOf(aPath) === parentOf(bPath) && baseName(aPath).toLowerCase() === baseName(bPath).toLowerCase()
    );
  }

  /** A hidden name beside `path` that nothing has. */
  private async freeName(path: string): Promise<string> {
    const dir = parentOf(path);
    for (let i = 0; ; i++) {
      const name = `.${baseName(path)}.moving${i === 0 ? '' : `-${i}`}`;
      const candidate = dir ? `${dir}/${name}` : name;
      if (!(await this.entry(candidate).catch(() => null))) return candidate;
    }
  }

  /** The file or folder at `path`, as the disk resolves the name (case-insensitively on some disks). */
  private async entry(path: string): Promise<FileSystemHandle> {
    const parent = await this.dir(parentOf(path), false);
    const name = baseName(path);
    try {
      return await parent.getFileHandle(name);
    } catch {
      try {
        return await parent.getDirectoryHandle(name);
      } catch {
        throw new NotFoundError(path);
      }
    }
  }

  private async copy(from: string, to: string): Promise<void> {
    const parent = await this.dir(parentOf(from), false);
    const name = baseName(from);
    const isDir = await parent.getDirectoryHandle(name).then(
      () => true,
      () => false,
    );
    if (!isDir) {
      const { bytes } = await this.read(from);
      await this.write(to, bytes, { force: true });
      return;
    }
    await this.mkdir(to);
    for (const e of await this.list(from)) await this.copy(e.path, `${to}/${e.name}`);
  }

  private async dir(path: string, create: boolean): Promise<FileSystemDirectoryHandle> {
    let h = this.root;
    for (const seg of path.split('/').filter(Boolean)) {
      try {
        h = await h.getDirectoryHandle(seg, { create });
      } catch {
        throw new NotFoundError(path);
      }
    }
    return h;
  }

  private async file(path: string, create: boolean): Promise<FileSystemFileHandle> {
    const dir = await this.dir(parentOf(path), create);
    try {
      return await dir.getFileHandle(baseName(path), { create });
    } catch {
      throw new NotFoundError(path);
    }
  }
}
