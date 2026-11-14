import { importFileOf, type ImportFile } from '../notebook/importFiles.ts';

/** The last file dialog's input and how to settle it, until its files arrive or another dialog replaces it. */
let pending: { input: HTMLInputElement; settle: (files: ImportFile[] | null) => void } | null = null;

/**
 * Ask for files to import with the browser's file dialog (PRD §5): a whole folder (`webkitdirectory`, read-only;
 * each file keeps its path under the folder) or separate files. Must run while the page has a user gesture, or
 * the browser shows no dialog. A cancelled dialog leaves the promise waiting until the next one replaces it
 * (it then resolves to null): the `cancel` event isn't relied on, because Firefox under test automation fires
 * it before the files arrive.
 */
export function pickImportFiles(folder: boolean): Promise<ImportFile[] | null> {
  pending?.settle(null);
  return new Promise((resolve) => {
    const input = document.createElement('input');
    input.type = 'file';
    input.multiple = true;
    input.webkitdirectory = folder;
    input.hidden = true;
    const settle = (files: ImportFile[] | null) => {
      input.remove();
      if (pending?.input === input) pending = null;
      resolve(files);
    };
    pending = { input, settle };
    input.addEventListener('change', () => settle([...(input.files ?? [])].map((f) => importFileOf(f))), {
      once: true,
    });
    document.body.append(input);
    input.click();
  });
}

/** Whether a drag carries files from outside the page (Finder, Explorer, the desktop). */
export function draggingFiles(e: DragEvent): boolean {
  return !!e.dataTransfer && [...e.dataTransfer.types].includes('Files');
}

/**
 * The files in a drop from the desktop (PRD §6.1), a dropped folder's included, each at its path under what was
 * dropped. Call it in the drop handler itself: the browser lets go of the drop's items once the handler returns.
 * Files are read only when imported.
 */
export function droppedFiles(dt: DataTransfer): Promise<ImportFile[]> {
  const entries = [...dt.items].filter((i) => i.kind === 'file').map((i) => i.webkitGetAsEntry?.() ?? null);
  // No entries API: the dropped files themselves (a folder can't be read that way).
  if (entries.length === 0 || entries.some((e) => e === null))
    return Promise.resolve([...dt.files].map((f) => importFileOf(f, f.name)));
  return filesOfEntries(entries as FileSystemEntry[]);
}

/** Every file in dropped entries, folders walked in full, each at its path under what was dropped. */
export async function filesOfEntries(entries: readonly FileSystemEntry[]): Promise<ImportFile[]> {
  const out: ImportFile[] = [];
  const walk = async (entry: FileSystemEntry): Promise<void> => {
    const path = entry.fullPath.replace(/^\/+/, '') || entry.name;
    if (entry.isFile) {
      const fileEntry = entry as FileSystemFileEntry;
      out.push({
        path,
        read: async () => {
          const file = await new Promise<File>((resolve, reject) => fileEntry.file(resolve, reject));
          return new Uint8Array(await file.arrayBuffer());
        },
      });
    } else if (entry.isDirectory) {
      const reader = (entry as FileSystemDirectoryEntry).createReader();
      // readEntries hands back a batch at a time (100 in Chromium) and an empty one at the end.
      for (;;) {
        const batch = await new Promise<FileSystemEntry[]>((resolve, reject) =>
          reader.readEntries(resolve, reject),
        );
        if (batch.length === 0) break;
        for (const child of batch) await walk(child);
      }
    }
  };
  for (const e of entries) await walk(e);
  return out;
}
