// A stand-in for the desktop app's Electron bridge: the same calls and error markers as its main process,
// on a real folder through the shared Node file code, so NativeStore can run the store contract.
import { FileError, FileLocks, listFolder, moveEntry, writeAtomic } from '@puffinote/node-files';
import { mkdir, open, rm, stat } from 'node:fs/promises';
import path from 'node:path';
import {
  CONFLICT_CHANGED,
  CONFLICT_EXISTS,
  NOT_FOUND,
  type PuffinoteNative,
} from '../src/platform/nativeStore.ts';

const version = (s: { mtimeMs: number; size: number; ino: number }) => `${s.mtimeMs}:${s.size}:${s.ino}`;

function notFound(e: unknown, p: string): never {
  if ((e as NodeJS.ErrnoException)?.code === 'ENOENT') throw new Error(`${NOT_FOUND}: ${path.basename(p)}`);
  throw e;
}

export function fakeNativeBridge(platform = 'linux'): PuffinoteNative {
  const locks = new FileLocks();
  const unused = () => Promise.reject(new Error('Not in the fake bridge'));
  const bridge: Partial<PuffinoteNative> = {
    platform,
    pickFolder: () => Promise.resolve(null),
    list: (dir) => listFolder(dir, async () => true).catch((e: unknown) => notFound(e, dir)),
    read: async (file) => {
      const h = await open(file, 'r').catch((e: unknown) => notFound(e, file));
      try {
        const s = await h.stat();
        return { bytes: new Uint8Array(await h.readFile()), modified: s.mtimeMs, version: version(s) };
      } finally {
        await h.close();
      }
    },
    write: (p, bytes, expected, force, create) =>
      locks.run(p, async () => {
        const s = await stat(p).catch(() => null);
        if (create && s) throw new Error(`${CONFLICT_EXISTS}: ${path.basename(p)}`);
        if (!force && expected !== null && (!s || version(s) !== expected))
          throw new Error(`${CONFLICT_CHANGED}: the file changed on disk`);
        await mkdir(path.dirname(p), { recursive: true });
        return version(await writeAtomic(p, bytes));
      }),
    mkdir: async (dir) => {
      const s = await stat(dir).catch(() => null);
      if (s && !s.isDirectory()) throw new Error(`${CONFLICT_EXISTS}: ${path.basename(dir)}`);
      await mkdir(dir, { recursive: true });
    },
    move: async (src, dest) => {
      try {
        await moveEntry(src, dest, locks);
      } catch (e) {
        if (!(e instanceof FileError)) throw e;
        if (e.code === 'not-found') throw new Error(`${NOT_FOUND}: ${path.basename(src)}`, { cause: e });
        if (e.code === 'exists') throw new Error(`${CONFLICT_EXISTS}: ${path.basename(dest)}`, { cause: e });
        throw e;
      }
    },
    // The system Trash: gone from the folder.
    trash: (p) => rm(p, { recursive: true, force: true }),
    reveal: unused,
    openExternal: unused,
    savePdf: unused,
    fileUrl: () => '',
    watch: () => undefined,
    onChanged: () => () => undefined,
  };
  return bridge as PuffinoteNative;
}
