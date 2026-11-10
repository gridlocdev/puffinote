/**
 * Folder handles the user picked, kept in IndexedDB so a reload can offer them again (a handle can't go in
 * localStorage). The browser still asks for permission once per visit.
 */
const DB = 'puffinote';
const STORE = 'handles';

function db(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB, 1);
    req.onupgradeneeded = () => req.result.createObjectStore(STORE);
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error ?? new Error('IndexedDB unavailable'));
  });
}

async function run<T>(mode: IDBTransactionMode, fn: (s: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const d = await db();
  try {
    return await new Promise<T>((resolve, reject) => {
      const req = fn(d.transaction(STORE, mode).objectStore(STORE));
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error ?? new Error('IndexedDB request failed'));
    });
  } finally {
    d.close();
  }
}

export async function getHandle(id: string): Promise<FileSystemDirectoryHandle | null> {
  try {
    return ((await run('readonly', (s) => s.get(id))) as FileSystemDirectoryHandle | undefined) ?? null;
  } catch {
    return null;
  }
}

export async function putHandle(id: string, handle: FileSystemDirectoryHandle): Promise<void> {
  try {
    await run('readwrite', (s) => s.put(handle, id));
  } catch {
    // Private browsing without IndexedDB: the folder just won't be offered after a reload.
  }
}

export async function deleteHandle(id: string): Promise<void> {
  try {
    await run('readwrite', (s) => s.delete(id));
  } catch {
    // Nothing stored, nothing to forget.
  }
}

/** The stored handle for the same folder as `handle`, if there is one. */
export async function findHandle(
  ids: readonly string[],
  handle: FileSystemDirectoryHandle,
): Promise<string | null> {
  for (const id of ids) {
    const h = await getHandle(id);
    if (h && (await h.isSameEntry(handle).catch(() => false))) return id;
  }
  return null;
}
