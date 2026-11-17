import type { Tab } from './tab.ts';
import { archiveName, notebookArchive } from './archive.ts';
import { clipboardHtml, noteHtml, type PicturePath } from './export.ts';
import { fileCount, importName, isImportJunk, type ImportFile, type ImportResult } from './importFiles.ts';
import { ConflictError, type Entry, type NotebookStore } from '../platform/notebookStore.ts';
import { baseName, joinPath, stemOf, uniqueName } from '../platform/paths.ts';

/** What import and export need of the notebook and the host. */
export interface TransferHost {
  readonly store: NotebookStore;
  /** Hand a file to the user (absent where the host can't). */
  readonly download?: (name: string, bytes: Uint8Array, mime: string) => void;
  /** Print standalone HTML, or save it as a PDF (absent where the host can't). */
  readonly print?: (html: string, name: string) => Promise<void>;
  name(): string;
  disposed(): boolean;
  saveAll(): Promise<boolean>;
  /** Files landed in `dir`: show them (`roots` are the top-level paths made). */
  imported(dir: string, roots: readonly string[]): Promise<void>;
  notify(kind: 'info' | 'error', text: string): void;
  report(e: unknown): void;
  /** The notebook file a picture link or `[[name]]` embed in a note names, or null (PRD §10.6). */
  picturePath: PicturePath;
}

/** Files in and out of a notebook: Import Notes…, Download Notebook, Export as HTML, Print (PRD §5, §10.5). */
export class NotebookTransfer {
  readonly #host: TransferHost;

  constructor(host: TransferHost) {
    this.#host = host;
  }

  /** Whether this notebook can hand files to the user as downloads (Download Notebook, PRD §5). */
  get canDownload(): boolean {
    return !!this.#host.download;
  }

  /**
   * Download Notebook (PRD §5): save the notes with unsaved changes, then hand the whole notebook to the user
   * as one zip named after it. A note that can't be saved stops it, so the archive never misses an edit.
   */
  async downloadArchive(): Promise<boolean> {
    const download = this.#host.download;
    if (!download || this.#host.disposed()) return false;
    if (!(await this.#host.saveAll())) {
      this.#host.notify(
        'error',
        'Save or revert the notes with unsaved changes, then download the notebook again.',
      );
      return false;
    }
    try {
      download(
        `${archiveName(this.#host.name())}.zip`,
        await notebookArchive(this.#host.store),
        'application/zip',
      );
      return true;
    } catch (e) {
      this.#host.report(e);
      return false;
    }
  }

  /**
   * Copy files from outside into folder `dir` (Import Notes… and drops from the desktop, PRD §5). Nothing is
   * overwritten: a top-level file or folder whose name is taken there gets a number ("Trip 2", "a 2.md"), and
   * a folder is never merged into one that exists. OS clutter and `.git` are skipped. Files are read and
   * written one at a time; one that fails is counted and the rest go on. The status bar says what happened.
   */
  async importFiles(dir: string, files: readonly ImportFile[]): Promise<ImportResult> {
    let imported = 0;
    let failed = 0;
    const roots: string[] = [];
    const folders = new Set<string>();
    if (this.#host.disposed()) return { imported, failed, roots };
    // Names in use per folder, without case: a disk that ignores case would refuse "A.md" beside "a.md".
    const takenIn = new Map<string, Set<string>>();
    const taken = async (d: string): Promise<Set<string>> => {
      let set = takenIn.get(d);
      if (!set) {
        const entries = await this.#host.store.list(d).catch(() => [] as Entry[]);
        set = new Set(entries.map((e) => e.name.toLowerCase()));
        takenIn.set(d, set);
      }
      return set;
    };
    const free = (name: string, set: Set<string>): string => {
      const n = uniqueName(name, { has: (x: string) => set.has(x.toLowerCase()) } as ReadonlySet<string>);
      set.add(n.toLowerCase());
      return n;
    };
    // Each top-level item's name in the notebook, decided by its first file.
    const tops = new Map<string, string>();
    for (const f of files) {
      if (this.#host.disposed()) break;
      if (isImportJunk(f.path)) continue;
      const raw = f.path.split('/').filter(Boolean);
      if (raw.length === 0) continue;
      const parts = raw.map(importName);
      if (!parts.every((p): p is string => p !== null)) {
        failed++;
        continue;
      }
      let topName = tops.get(raw[0]!);
      if (topName === undefined) {
        topName = free(parts[0]!, await taken(dir));
        tops.set(raw[0]!, topName);
      }
      const folder = joinPath(dir, [topName, ...parts.slice(1, -1)].join('/'));
      const into = parts.length === 1 ? dir : folder;
      try {
        const bytes = await f.read();
        const set = await taken(into);
        let name = parts.length === 1 ? topName : free(parts[parts.length - 1]!, set);
        for (;;) {
          try {
            await this.#host.store.write(joinPath(into, name), bytes, { create: true });
            break;
          } catch (e) {
            // Taken after all (the listing was stale): the next free name.
            if (!(e instanceof ConflictError && e.reason === 'exists')) throw e;
            name = free(name, set);
          }
        }
        imported++;
        const root = joinPath(dir, parts.length === 1 ? name : topName);
        if (!roots.includes(root)) roots.push(root);
        if (parts.length > 1) folders.add(root);
      } catch {
        failed++;
      }
    }
    if (imported > 0) await this.#host.imported(dir, roots);
    const only = roots.length === 1 && folders.has(roots[0]!) ? roots[0]! : null;
    const into = only ? ` into “${baseName(only)}”` : dir ? ` into “${baseName(dir)}”` : '';
    const lost = failed > 0 ? ` ${fileCount(failed)} couldn’t be copied.` : '';
    if (imported > 0)
      this.#host.notify(failed > 0 ? 'error' : 'info', `Imported ${fileCount(imported)}${into}.${lost}`);
    else if (failed > 0) this.#host.notify('error', `Nothing was imported.${lost}`);
    else this.#host.notify('info', 'There was nothing to import.');
    return { imported, failed, roots };
  }

  /**
   * Export the note in `tab` as one standalone HTML file (PRD §10.5), its unsaved text included, handed to
   * the user as a download.
   */
  async exportHtml(tab: Tab): Promise<void> {
    if (!tab.state) return;
    const download = this.#host.download;
    if (!download) return this.#host.notify('info', 'Puffinote can’t save a file here.');
    try {
      const out = await noteHtml(this.#host.store, tab.path.peek(), tab.state.text, this.#host.picturePath);
      download(out.name, new TextEncoder().encode(out.html), 'text/html');
      this.noteSkipped(out.skipped, 'Exported');
    } catch (e) {
      this.#host.report(e);
    }
  }

  /** Whether this host can print a note or save it as a PDF. */
  get canPrint(): boolean {
    return this.#host.print !== undefined;
  }

  /**
   * Print the note in `tab`, or save it as a PDF (PRD §10.5): the same standalone HTML as Export as HTML,
   * laid out for paper by its print stylesheet.
   */
  async exportPdf(tab: Tab): Promise<void> {
    const print = this.#host.print;
    if (!tab.state || !print) return;
    try {
      const out = await noteHtml(this.#host.store, tab.path.peek(), tab.state.text, this.#host.picturePath);
      await print(out.html, `${stemOf(tab.path.peek())}.pdf`);
      this.noteSkipped(out.skipped, 'Exported');
    } catch (e) {
      this.#host.report(e);
    }
  }

  /**
   * The formatted copy of `text`, selected in the note at `notePath` (PRD §10.1): HTML with its pictures
   * embedded. Says in the status bar which pictures stayed links.
   */
  async copyHtml(notePath: string, text: string): Promise<string> {
    const out = await clipboardHtml(this.#host.store, notePath, text, this.#host.picturePath);
    this.noteSkipped(out.skipped, 'Copied');
    return out.html;
  }

  /** Say which pictures an export or a copy left as links instead of embedding them. */
  private noteSkipped(skipped: readonly string[], done: 'Exported' | 'Copied'): void {
    if (skipped.length === 0) return;
    const which = skipped.length === 1 ? `“${skipped[0]}”` : `${skipped.length} pictures`;
    this.#host.notify(
      'info',
      `${done} without ${which}: missing, too large or not a PNG, JPEG, GIF, WebP, AVIF or BMP, so ${skipped.length === 1 ? 'it stays a link' : 'they stay links'}.`,
    );
  }
}
