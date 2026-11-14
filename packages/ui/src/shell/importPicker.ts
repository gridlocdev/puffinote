import { importFileOf, type ImportFile } from '../notebook/importFiles.ts';

/**
 * Ask for files to import with the browser's file dialog (PRD §5): a whole folder (`webkitdirectory`, read-only;
 * each file keeps its path under the folder) or separate files. Resolves to null when the dialog is cancelled.
 * Must run while the page has a user gesture, or the browser shows no dialog.
 */
export function pickImportFiles(folder: boolean): Promise<ImportFile[] | null> {
  return new Promise((resolve) => {
    const input = document.createElement('input');
    input.type = 'file';
    input.multiple = true;
    input.webkitdirectory = folder;
    input.hidden = true;
    const done = (files: ImportFile[] | null) => {
      input.remove();
      resolve(files);
    };
    input.addEventListener('change', () => done([...(input.files ?? [])].map((f) => importFileOf(f))), {
      once: true,
    });
    input.addEventListener('cancel', () => done(null), { once: true });
    document.body.append(input);
    input.click();
  });
}
