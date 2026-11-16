import { after } from '../schedule.ts';
import {
  ConflictError,
  NotFoundError,
  type Entry,
  type FileData,
  type NotebookStore,
  type StoreCapabilities,
  type WriteOptions,
} from './notebookStore.ts';
import { baseName, parentOf } from './paths.ts';

/**
 * The browser's private notebook folder (the origin-private file system): every modern browser has one, so the
 * web app's notebook survives a reload without touching the disk.
 */
export async function privateFolder(timeoutMs = 5000): Promise<FileSystemDirectoryHandle | null> {
  // A browser with site storage blocked can leave this hanging rather than refuse; give up after a while.
  const timeout = new Promise<null>((resolve) => after(timeoutMs, () => resolve(null)));
  const folder = (async () => {
    const root = await navigator.storage.getDirectory();
    return await root.getDirectoryHandle('notebook', { create: true });
  })().catch(() => null);
  return Promise.race([folder, timeout]);
}

/**
 * Ask the browser to keep this site's storage under storage pressure instead of evicting it (PRD §3), so the
 * private notebook isn't cleared to make room. Resolves to whether the storage is persistent; never throws.
 */
export async function persistStorage(): Promise<boolean> {
  try {
    const storage = typeof navigator === 'undefined' ? undefined : navigator.storage;
    if (typeof storage?.persist !== 'function') return false;
    if (typeof storage.persisted === 'function' && (await storage.persisted())) return true;
    return await storage.persist();
  } catch {
    return false;
  }
}

/**
 * A notebook in a browser folder handle (the private folder; tests pass in-memory ones). Versions are the file's
 * lastModified, re-checked before each write. There is no change notification, so the shell re-reads the tree
 * when the window regains focus.
 */
export class BrowserStore implements NotebookStore {
  readonly kind = 'browser' as const;
  readonly name: string;
  /** A picked folder's disk may or may not ignore case, so `ignoresCase` is left unknown for it. */
  readonly capabilities: StoreCapabilities;
  readonly root: FileSystemDirectoryHandle;
  /** The private folder, which has no name the user chose. */
  readonly isPrivate: boolean;

  constructor(root: FileSystemDirectoryHandle, isPrivate = false) {
    this.root = root;
    this.isPrivate = isPrivate;
    this.name = isPrivate ? 'Browser notebook' : root.name;
    this.capabilities = {
      watch: false,
      reveal: false,
      openExternal: false,
      ...(isPrivate ? { ignoresCase: false } : {}),
    };
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
    // A write that expects the file to be there doesn't make its folders: a folder deleted since the read
    // would come back before the check below could refuse.
    const conditional = !opts.force && opts.expected != null;
    const dir = await this.dir(parentOf(path), !conditional).catch((e: unknown) => {
      if (conditional && e instanceof NotFoundError)
        throw new ConflictError('deleted', 'The file was deleted since it was opened.');
      throw e;
    });
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
