import { randomBytes } from 'node:crypto';
import {
  accessSync,
  closeSync,
  constants as fsConstants,
  fchmodSync,
  fstatSync,
  fsyncSync,
  ftruncateSync,
  openSync,
  renameSync,
  rmSync,
  statSync,
  writeFileSync,
  writeSync,
  type Stats,
} from 'node:fs';
import { access, chown, open, rename, stat, unlink } from 'node:fs/promises';
import path from 'node:path';
import { FileError } from './errors.ts';
import { realTarget } from './paths.ts';

/** How many random bytes name a temp file (as hex, twice as many digits). */
const TEMP_RANDOM_BYTES = 8;

/**
 * Our own in-flight save files: `.<name>.<hex>.tmp`, beside the file they replace. Never listed, and never
 * reported as changes. Earlier NAS servers used 12 hex digits, so a leftover from one still matches.
 */
export const TEMP_FILE = /^\..+\.[0-9a-f]{12}(?:[0-9a-f]{4})?\.tmp$/;

/** A fresh temp file name beside `file`, in the `TEMP_FILE` shape. */
export function tempPath(file: string): string {
  const name = `.${path.basename(file)}.${randomBytes(TEMP_RANDOM_BYTES).toString('hex')}.tmp`;
  return path.join(path.dirname(file), name);
}

/** What `overwriteInPlace` needs of an open file. */
export interface InPlaceHandle {
  write(buf: Uint8Array, offset: number, length: number, position: number): Promise<{ bytesWritten: number }>;
  truncate(len: number): Promise<void>;
}

/** The synchronous `InPlaceHandle`, for the CLI. */
export interface InPlaceHandleSync {
  write(buf: Uint8Array, offset: number, length: number, position: number): number;
  truncate(len: number): void;
}

/**
 * Atomic save of a path, following a symlink so the link stays and its target is replaced (a dangling link
 * is written where it points). See `replaceFile`.
 */
export async function writeAtomic(p: string, bytes: Uint8Array): Promise<Stats> {
  return replaceFile(await realTarget(p), bytes);
}

/**
 * Atomic save of exactly `file` (a symlink there is replaced, not followed): a temp file beside it, fsync,
 * rename, then fsync the folder. The replacement keeps the file's mode (and owner, where permitted). A
 * read-only file is refused, since the rename would replace it as long as its folder is writable. A
 * hard-linked file is written in place instead (`overwriteInPlace`), since a rename would split the link.
 * Resolves to the written file's stat, taken from its own handle, so a write by another app after the rename
 * can't pass for it.
 */
export async function replaceFile(file: string, bytes: Uint8Array): Promise<Stats> {
  const target = await stat(file).catch(() => null);
  if (target && !target.isFile()) throw new FileError('not-a-file', `“${path.basename(file)}” is not a file`);
  if (target && (await isReadOnly(file, target.mode)))
    throw new FileError('read-only', `“${path.basename(file)}” is read-only`);
  if (target && target.nlink > 1) {
    const h = await open(file, 'r+');
    try {
      await overwriteInPlace(h, bytes, (await h.stat()).size);
      await h.sync();
      return await h.stat();
    } finally {
      await h.close();
    }
  }
  const mode = target ? target.mode & 0o7777 : 0o644;
  const tmp = tempPath(file);
  let created = false;
  let written: Stats;
  try {
    const h = await open(tmp, 'wx', mode);
    created = true;
    try {
      await h.chmod(mode); // open() applies the umask
      await h.writeFile(bytes);
      await h.sync();
      // The rename keeps the inode, size and mtime, so this is the saved file's version.
      written = await h.stat();
    } finally {
      await h.close();
    }
    if (target) await chown(tmp, target.uid, target.gid).catch(() => {}); // only root may give files away
    await rename(tmp, file);
  } catch (e) {
    if (created) await unlink(tmp).catch(() => {});
    throw e;
  }
  await syncDir(path.dirname(file));
  return written;
}

/** `replaceFile` for synchronous callers (the CLI). It doesn't change the owner. */
export function replaceFileSync(file: string, bytes: Uint8Array): void {
  let target: Stats | null;
  try {
    target = statSync(file);
  } catch {
    target = null;
  }
  if (target && !target.isFile()) throw new FileError('not-a-file', `“${path.basename(file)}” is not a file`);
  if (target && isReadOnlySync(file, target.mode))
    throw new FileError('read-only', `“${path.basename(file)}” is read-only`);
  if (target && target.nlink > 1) {
    const fd = openSync(file, 'r+');
    try {
      overwriteInPlaceSync(
        { write: (b, o, l, p) => writeSync(fd, b, o, l, p), truncate: (l) => ftruncateSync(fd, l) },
        bytes,
        fstatSync(fd).size,
      );
      fsyncSync(fd);
    } finally {
      closeSync(fd);
    }
    return;
  }
  const mode = target ? target.mode & 0o7777 : 0o644;
  const tmp = tempPath(file);
  try {
    const fd = openSync(tmp, 'wx', mode);
    try {
      fchmodSync(fd, mode); // open() applies the umask
      writeFileSync(fd, bytes);
      fsyncSync(fd);
    } finally {
      closeSync(fd);
    }
    renameSync(tmp, file);
  } catch (e) {
    rmSync(tmp, { force: true });
    throw e;
  }
}

/**
 * Write `bytes` over a file in place, for a hard-linked file that a rename would split from its other names.
 * It can't be atomic, but it never empties the file: the part past the old end is written first (and cut
 * back off if the disk is full, leaving the old file as it was), then the old length is overwritten from
 * offset 0, which needs no new space, and only then is a longer old tail cut off.
 */
export async function overwriteInPlace(h: InPlaceHandle, bytes: Uint8Array, oldSize: number): Promise<void> {
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

/** `overwriteInPlace` for synchronous callers (the CLI). */
export function overwriteInPlaceSync(h: InPlaceHandleSync, bytes: Uint8Array, oldSize: number): void {
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
async function isReadOnly(file: string, mode: number): Promise<boolean> {
  if ((mode & 0o222) === 0) return true;
  return access(file, fsConstants.W_OK).then(
    () => false,
    () => true,
  );
}

function isReadOnlySync(file: string, mode: number): boolean {
  if ((mode & 0o222) === 0) return true;
  try {
    accessSync(file, fsConstants.W_OK);
    return false;
  } catch {
    return true;
  }
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
