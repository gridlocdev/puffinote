import { ConflictError, NotFoundError, type NotebookStore } from '../platform/notebookStore.ts';
import { baseName, extOf, joinPath, parentOf } from '../platform/paths.ts';
import { stateDir } from './tree.ts';

/**
 * The notebook's own Trash (PRD §6.1), for the stores with no system Trash (the browser and the NAS). A deleted
 * file or folder moves to `.puffinote/trash/<id>/<its path>`, and `.puffinote/trash/<id>.json` says where it
 * was and when it went. `.puffinote` is never listed, so the tree, search and Find Unused Pictures don't see
 * it. Empty Trash is the only permanent delete.
 */
export const trashDir = `${stateDir}/trash`;

export interface TrashItem {
  readonly id: string;
  /** Where it was in the notebook. */
  readonly path: string;
  readonly kind: 'file' | 'dir';
  /** When it was deleted, in milliseconds since the epoch. */
  readonly deleted: number;
}

/** The desktop app moves to the system Trash; every other store keeps its Trash in the notebook. */
export function hasNotebookTrash(store: NotebookStore): boolean {
  return store.kind !== 'native';
}

const encoder = new TextEncoder();

/** Move `path` into the notebook's Trash. Throws if it can't, and leaves `path` where it was. */
export async function moveToTrash(
  store: NotebookStore,
  path: string,
  kind: 'file' | 'dir',
  now = Date.now(),
): Promise<TrashItem> {
  // Keeps the Trash out of git whatever the notebook's own .gitignore says.
  await store.write(`${trashDir}/.gitignore`, encoder.encode('*\n'), { create: true }).catch((e: unknown) => {
    if (!(e instanceof ConflictError)) throw e;
  });
  // The manifest is written first, with create, so two deletes in one millisecond can't share an id.
  for (let n = 0; ; n++) {
    const id = n === 0 ? String(now) : `${now}-${n}`;
    const item: TrashItem = { id, path, kind, deleted: now };
    try {
      await store.write(`${trashDir}/${id}.json`, encoder.encode(JSON.stringify(item)), { create: true });
    } catch (e) {
      if (e instanceof ConflictError && e.reason === 'exists' && n < 100) continue;
      throw e;
    }
    try {
      await store.move(path, `${trashDir}/${id}/${path}`);
    } catch (e) {
      await store.remove(`${trashDir}/${id}.json`).catch(() => undefined);
      await store.remove(`${trashDir}/${id}`).catch(() => undefined);
      throw e;
    }
    return item;
  }
}

/** What is in the Trash, newest first. A manifest whose item is missing is left out. */
export async function listTrash(store: NotebookStore): Promise<TrashItem[]> {
  const entries = await store.list(trashDir).catch((e: unknown) => {
    if (e instanceof NotFoundError) return [];
    throw e;
  });
  const dirs = new Set(entries.filter((e) => e.kind === 'dir').map((e) => e.name));
  const items: TrashItem[] = [];
  for (const e of entries) {
    if (e.kind !== 'file' || extOf(e.name) !== '.json') continue;
    const id = e.name.slice(0, -'.json'.length);
    if (!dirs.has(id)) continue;
    const item = await readManifest(store, id);
    if (item) items.push(item);
  }
  return items.sort((a, b) => b.deleted - a.deleted || b.id.localeCompare(a.id));
}

async function readManifest(store: NotebookStore, id: string): Promise<TrashItem | null> {
  try {
    const raw = JSON.parse(
      new TextDecoder().decode((await store.read(`${trashDir}/${id}.json`)).bytes),
    ) as Partial<TrashItem> | null;
    if (typeof raw?.path !== 'string' || raw.path === '' || raw.path.split('/').includes('..')) return null;
    return {
      id,
      path: raw.path,
      kind: raw.kind === 'dir' ? 'dir' : 'file',
      deleted: typeof raw.deleted === 'number' ? raw.deleted : 0,
    };
  } catch {
    return null;
  }
}

/**
 * Put `item` back where it was, or beside it under a free name (`name 2.md`) when something has that name
 * now; it never overwrites. Returns the path it went to.
 */
export async function restoreFromTrash(store: NotebookStore, item: TrashItem): Promise<string> {
  const from = `${trashDir}/${item.id}/${item.path}`;
  const dir = parentOf(item.path);
  const target = await moveToFreeName(store, from, dir, baseName(item.path));
  await store.remove(`${trashDir}/${item.id}`).catch(() => undefined);
  await store.remove(`${trashDir}/${item.id}.json`).catch(() => undefined);
  return target;
}

async function moveToFreeName(
  store: NotebookStore,
  from: string,
  dir: string,
  name: string,
): Promise<string> {
  for (let attempt = 0; ; attempt++) {
    const target = joinPath(dir, await freeName(store, dir, name));
    try {
      await store.move(from, target);
      return target;
    } catch (e) {
      // Taken between the look and the move: look again.
      if (!(e instanceof ConflictError && e.reason === 'exists' && attempt < 5)) throw e;
    }
  }
}

/** `name` if nothing in `dir` has it (ignoring case where the disk might), else `stem 2.ext`, `stem 3.ext`, … */
async function freeName(store: NotebookStore, dir: string, name: string): Promise<string> {
  const entries = await store.list(dir).catch(() => []);
  const fold = store.capabilities.ignoresCase !== false;
  const key = (s: string) => (fold ? s.normalize('NFC').toLowerCase() : s.normalize('NFC'));
  const taken = new Set(entries.map((e) => key(e.name)));
  const ext = extOf(name);
  const stem = ext ? name.slice(0, -ext.length) : name;
  for (let n = 1; ; n++) {
    const candidate = n === 1 ? name : `${stem} ${n}${ext}`;
    if (!taken.has(key(candidate))) return candidate;
  }
}

/** Delete everything in the Trash for good. */
export async function emptyTrash(store: NotebookStore): Promise<void> {
  await store.remove(trashDir).catch((e: unknown) => {
    if (!(e instanceof NotFoundError)) throw e;
  });
}
