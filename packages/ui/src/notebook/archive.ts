import type { NotebookStore } from '../platform/notebookStore.ts';
import { joinPath } from '../platform/paths.ts';
import { zip, type ZipEntry } from '../platform/zip.ts';
import { trashDir } from './trash.ts';
import { cacheDir, stateDir } from './tree.ts';

/**
 * Whether Download Notebook leaves `path` out (PRD §5): git's own folder, the notebook's Trash (what was
 * deleted), the window state in `.puffinote/session.json`, which belongs to this machine, and the search index in
 * `.puffinote/cache/`, which is built again. The notebook's settings go with it.
 */
export function leftOutOfArchive(path: string): boolean {
  return (
    path === `${stateDir}/session.json` ||
    path === trashDir ||
    path === cacheDir ||
    path.startsWith(`${cacheDir}/`) ||
    path.startsWith(`${trashDir}/`) ||
    path.split('/').includes('.git')
  );
}

/** A notebook's name as a file name: no path separators or characters Windows refuses. */
export function archiveName(name: string): string {
  // eslint-disable-next-line no-control-regex
  const clean = name.replace(/[\\/:*?"<>|\u0000-\u001f]+/g, '-').replace(/^[\s.]+|[\s.]+$/g, '');
  return clean || 'Notebook';
}

/**
 * Every file and folder in the notebook, read through its store, as one zip archive with a top folder named
 * after the notebook (PRD §5). Files are read one at a time, so a NAS isn't asked for them all at once.
 */
export async function notebookArchive(store: NotebookStore): Promise<Uint8Array> {
  const top = archiveName(store.name);
  const entries: ZipEntry[] = [{ path: top }];
  const walk = async (dir: string): Promise<void> => {
    const listed = await store.list(dir);
    listed.sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
    for (const e of listed) {
      if (leftOutOfArchive(e.path)) continue;
      const path = joinPath(top, e.path);
      if (e.kind === 'dir') {
        entries.push({ path, modified: e.modified || undefined });
        await walk(e.path);
      } else {
        const { bytes } = await store.read(e.path);
        entries.push({ path, bytes, modified: e.modified || undefined });
      }
    }
  };
  await walk('');
  return zip(entries);
}
