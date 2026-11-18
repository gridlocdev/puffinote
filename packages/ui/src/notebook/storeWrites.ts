import {
  ConflictError,
  NotFoundError,
  type FileData,
  type NotebookStore,
} from '../platform/notebookStore.ts';
import { stateDir } from './tree.ts';

/** Volatile state stays out of git; the notebook's settings travel with it (PRD §5). */
const gitignore = '# Puffinote: machine-local state\nsession.json\n';

/**
 * Write `bytes` over a file last read at version `expected`. A store that gave no version (a server without
 * ETags) can't refuse a stale write itself, so the file is read again first and must still hold what was
 * read (`unchanged`); otherwise this throws the ConflictError a versioned store would (PRD §5). `closed` says
 * whether the notebook closed meanwhile; a closed notebook writes nothing.
 */
export async function writeOver(
  store: NotebookStore,
  path: string,
  bytes: Uint8Array,
  expected: string | null,
  unchanged: (now: Uint8Array) => boolean,
  closed: () => boolean,
): Promise<string | null> {
  if (expected != null) return store.write(path, bytes, { expected });
  let now: FileData;
  try {
    now = await store.read(path);
  } catch (e) {
    if (e instanceof NotFoundError)
      throw new ConflictError('deleted', 'The file was deleted since it was opened.');
    throw e;
  }
  if (!unchanged(now.bytes)) throw new ConflictError('changed', 'The file changed since it was opened.');
  if (closed()) throw new Error('The notebook was closed.');
  return store.write(path, bytes, { expected: now.version });
}

/** Make the notebook's state folder, with the `.gitignore` that keeps the session out of git. */
export async function ensureStateDir(store: NotebookStore): Promise<void> {
  try {
    await store.write(`${stateDir}/.gitignore`, new TextEncoder().encode(gitignore), { create: true });
  } catch (e) {
    if (!(e instanceof ConflictError)) throw e;
  }
}

export function sameBytes(a: Uint8Array, b: Uint8Array): boolean {
  return a.length === b.length && a.every((x, i) => x === b[i]);
}

/** The value `text` holds, or null when it isn't JSON. */
export function parseJson(text: string): unknown {
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return null;
  }
}

export function encodeJson(value: unknown): Uint8Array {
  return new TextEncoder().encode(JSON.stringify(value, null, 2) + '\n');
}
