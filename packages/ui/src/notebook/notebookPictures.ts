import { signal, type Signal } from '@preact/signals';
import type { Entry, NotebookStore } from '../platform/notebookStore.ts';
import {
  baseName,
  isImagePath,
  isWithin,
  joinPath,
  mimeOf,
  pathKey,
  resolveRelative,
  uniqueName,
} from '../platform/paths.ts';
import {
  attachmentDir,
  attachmentName,
  imageMarkdown,
  imageTargets,
  isImageMime,
  PictureUsage,
} from './attachments.ts';
import type { Notice } from './notebook.ts';
import type { NotebookTrash } from './notebookTrash.ts';
import type { Settings } from './settings.ts';
import type { TrashItem } from './trash.ts';
import { allFiles, fileKind, findNode, loadTree, stateDir, type FileStat, type TreeNode } from './tree.ts';

/** What a notebook's pictures need of the notebook. */
export interface PicturesHost {
  readonly store: NotebookStore;
  /** The status line: the no-longer-used offer goes there, and comes down when a note shows them again. */
  readonly notice: Signal<Notice | null>;
  readonly trash: NotebookTrash;
  settings(): Settings;
  tree(): TreeNode;
  /** Whether the notebook closed; a closed notebook changes no files. */
  closed(): boolean;
  /** A note's text: its tab's, unsaved edits included, or what's on disk. Null when it can't be read. */
  noteText(path: string): Promise<string | null>;
  reloadTree(): Promise<void>;
  /**
   * Delete `path` and reconcile the notebook: false when it failed, the trash item when it went to the
   * notebook's Trash, null otherwise.
   */
  deleteEntry(path: string): Promise<TrashItem | null | false>;
  notify(kind: Notice['kind'], text: string): void;
  report(e: unknown): void;
}

/**
 * A notebook's pictures (PRD §10.6): reading them for the editor, saving pasted ones, and finding and
 * deleting the ones no note shows.
 */
export class NotebookPictures {
  /** Bumps when a picture a note shows has loaded, so the editor redraws it. */
  readonly loaded = signal(0);
  readonly #host: PicturesHost;
  readonly #images = new Map<string, string | null>();
  /** The read in flight for each picture being loaded; a read whose token was replaced is dropped. */
  readonly #imageReads = new Map<string, object>();
  /** The no-longer-used offer, while it is the notice, taken down when a save shows one of its pictures again. */
  #offer: { readonly notice: Notice; readonly paths: readonly string[] } | null = null;

  constructor(host: PicturesHost) {
    this.#host = host;
  }

  /** Let go of the pictures read into memory. */
  dispose(): void {
    for (const url of this.#images.values()) if (url?.startsWith('blob:')) URL.revokeObjectURL(url);
  }

  /** Drop what was read for pictures whose size or time changed between two listings, or that went away. */
  forgetChanged(before: ReadonlyMap<string, FileStat>, after: ReadonlyMap<string, FileStat>): void {
    this.forget(
      [...this.#images.keys()].filter((p) => {
        const a = before.get(p);
        const b = after.get(p);
        return a?.size !== b?.size || a?.modified !== b?.modified;
      }),
    );
  }

  /**
   * After a save of the note at `path` from `before` to `text`: take down the no-longer-used offer when the
   * note shows one of its pictures again, and make the offer for pictures the note stopped showing.
   */
  saved(path: string, before: string, text: string): void {
    this.#withdrawOfferShownBy(text, path);
    if (!before.includes('](')) return;
    const key = (p: string) => pathKey(p, this.#ignoresCase);
    const now = new Set([...imageTargets(text, path)].map(key));
    const dropped = [...imageTargets(before, path)].filter((p) => !now.has(key(p)));
    if (dropped.length > 0) void this.offerUnused(dropped);
  }

  /**
   * The URL for an image a note at `notePath` refers to: web and data URLs as they are, notebook files
   * straight from the store, or read once into a blob URL (the editor redraws when it arrives).
   */
  resolve(notePath: string, ref: string): string {
    if (/^(https?:|data:|blob:)/i.test(ref)) return ref;
    const path = resolveRelative(notePath, ref);
    if (path === null) return '';
    const direct = this.#host.store.fileUrl(path);
    if (direct) return direct;
    const cached = this.#images.get(path);
    if (cached !== undefined) return cached ?? '';
    this.#images.set(path, null);
    const token = {};
    this.#imageReads.set(path, token);
    const current = () => this.#imageReads.get(path) === token && !this.#host.closed();
    void this.#host.store
      .read(path)
      .then(({ bytes }) => {
        if (!current()) return;
        this.#imageReads.delete(path);
        this.#images.set(
          path,
          URL.createObjectURL(new Blob([bytes as Uint8Array<ArrayBuffer>], { type: mimeOf(path) })),
        );
        this.loaded.value++;
      })
      .catch(() => {
        // Not kept: the picture may turn up (a sync, a NAS back online), and the next redraw asks again.
        if (!current()) return;
        this.#imageReads.delete(path);
        this.#images.delete(path);
      });
    return '';
  }

  /** Drop what was read for pictures at or inside `paths`, so the editor reads them again. */
  forget(paths: readonly string[]): void {
    if (paths.length === 0) return;
    let dropped = false;
    for (const path of [...this.#images.keys()]) {
      if (!paths.some((p) => isWithin(path, p))) continue;
      const url = this.#images.get(path);
      if (url?.startsWith('blob:')) URL.revokeObjectURL(url);
      this.#images.delete(path);
      this.#imageReads.delete(path);
      dropped = true;
    }
    if (dropped) this.loaded.value++;
  }

  /**
   * Save pasted or dropped pictures for the note at `notePath` in its picture folder (PRD §10.6) and return
   * the Markdown that shows each. Files that aren't pictures are skipped.
   */
  async add(
    notePath: string,
    files: readonly { name: string; type: string; bytes: Uint8Array }[],
  ): Promise<string[]> {
    const dir = attachmentDir(this.#host.settings(), notePath);
    let entries: Entry[];
    try {
      entries = await this.#host.store.list(dir);
    } catch {
      entries = [];
    }
    const taken = new Set(entries.map((e) => e.name.toLowerCase()));
    const links: string[] = [];
    for (const f of files) {
      if (!isImageMime(f.type)) continue;
      // The same picture pasted again (Copy Picture from another note) links to the file that's already there.
      const same = await this.#samePicture(dir, entries, f.bytes);
      if (same !== null) {
        links.push(imageMarkdown(notePath, same));
        continue;
      }
      let name = attachmentName(f.name, f.type, new Date());
      // Disks that ignore case would refuse "Photo.png" beside "photo.png".
      name = uniqueName(name, { has: (n: string) => taken.has(n.toLowerCase()) } as ReadonlySet<string>);
      taken.add(name.toLowerCase());
      const path = joinPath(dir, name);
      try {
        await this.#host.store.write(path, f.bytes, { create: true });
      } catch (e) {
        this.#host.report(e);
        continue;
      }
      links.push(imageMarkdown(notePath, path));
    }
    if (links.length > 0) await this.#host.reloadTree();
    return links;
  }

  /**
   * Every note in the notebook, hidden folders included whatever Show Hidden Files says: a picture a hidden
   * note shows is still used.
   */
  async #allNotes(): Promise<string[]> {
    const tree = await loadTree(this.#host.store, true);
    return allFiles(tree)
      .map((f) => f.path)
      .filter((p) => fileKind(p) === 'note');
  }

  /** A note's text, like `noteText`, but a note that can't be read is an error: nothing can be said about it. */
  async #readNote(path: string): Promise<string> {
    const text = await this.#host.noteText(path);
    if (text === null)
      throw new Error(`Puffinote couldn’t read “${path}”, so it can’t tell which pictures are used.`);
    return text;
  }

  /** Whether picture names compare without case: when the store says so, or can't tell. */
  get #ignoresCase(): boolean {
    return this.#host.store.capabilities.ignoresCase !== false;
  }

  /**
   * Every picture some note (other than those in `except`) links to, by notebook path. Throws when a note can't be read, so a picture it
   * shows is never called unused.
   */
  async #linked(except: ReadonlySet<string> = new Set()): Promise<PictureUsage> {
    const out = new PictureUsage(this.#ignoresCase);
    for (const path of await this.#allNotes()) {
      if (!except.has(path)) out.add(await this.#readNote(path), path);
    }
    return out;
  }

  /** Whether `text`, the note at `notePath`, shows the picture at `picture` in any of the ways that count. */
  shows(text: string, notePath: string, picture: string): boolean {
    const usage = new PictureUsage(this.#ignoresCase);
    usage.add(text, notePath);
    return usage.has(picture);
  }

  /** The picture in `dir` (listed as `entries`) whose bytes are `bytes`, or null. */
  async #samePicture(dir: string, entries: readonly Entry[], bytes: Uint8Array): Promise<string | null> {
    for (const e of entries) {
      if (e.kind !== 'file' || e.size !== bytes.length || !isImagePath(e.name)) continue;
      const path = e.path;
      try {
        const other = (await this.#host.store.read(path)).bytes;
        if (other.length === bytes.length && other.every((b, i) => b === bytes[i])) return path;
      } catch {
        // Unreadable: not a match.
      }
    }
    return null;
  }

  /** Put a deleted picture's file back (undoing Delete Picture), and redraw the notes that show it. */
  async restore(path: string, bytes: Uint8Array): Promise<void> {
    if (this.#host.closed()) return;
    try {
      await this.#host.store.write(path, bytes, { create: true });
    } catch (e) {
      this.#host.report(e);
      return;
    }
    this.forget([path]);
    this.loaded.value++;
    await this.#host.reloadTree();
  }

  /**
   * The notes other than `except` that link to the picture at `path`, in tree order, hidden ones included.
   * Throws when a note can't be read.
   */
  async notesShowing(path: string, except: string): Promise<string[]> {
    const out: string[] = [];
    for (const note of await this.#allNotes()) {
      if (note === except) continue;
      const text = await this.#readNote(note);
      if (this.shows(text, note, path)) out.push(note);
    }
    return out;
  }

  /**
   * The notes outside `path` (a file or folder) that show a picture at or inside it, in tree order: what
   * deleting `path` leaves with a missing picture. Throws when a note can't be read.
   */
  async notesShowingPicturesIn(path: string): Promise<string[]> {
    const tree = await loadTree(this.#host.store, true);
    const node = findNode(tree, path);
    const files = !node ? [] : node.kind === 'file' ? [node.path] : allFiles(node).map((f) => f.path);
    const pictures = files.filter((p) => fileKind(p) === 'image');
    if (pictures.length === 0) return [];
    const out: string[] = [];
    for (const f of allFiles(tree)) {
      if (fileKind(f.path) !== 'note' || isWithin(f.path, path)) continue;
      const usage = new PictureUsage(this.#ignoresCase);
      usage.add(await this.#readNote(f.path), f.path);
      if (pictures.some((p) => usage.has(p))) out.push(f.path);
    }
    return out;
  }

  /**
   * The notebook's pictures that no note links to (Find Unused Pictures…), in tree order. Throws when a note
   * can't be read.
   */
  async unused(): Promise<string[]> {
    const linked = await this.#linked();
    return allFiles(this.#host.tree())
      .map((f) => f.path)
      .filter((p) => fileKind(p) === 'image' && !isWithin(p, stateDir) && !linked.has(p));
  }

  /**
   * After the links to `paths` went away, offer to delete those no note links to any more (the "When a
   * picture is no longer used" setting).
   */
  async offerUnused(paths: readonly string[]): Promise<void> {
    if (this.#host.settings().unusedImages !== 'ask') return;
    const present = paths.filter((p) => findNode(this.#host.tree(), p)?.kind === 'file');
    if (present.length === 0) return;
    let linked: PictureUsage;
    try {
      linked = await this.#linked();
    } catch {
      // A note that can't be read may show them: say nothing rather than offer to delete a used picture.
      return;
    }
    const unused = present.filter((p) => !linked.has(p));
    if (unused.length === 0) return;
    const notice: Notice = {
      kind: 'info',
      text:
        unused.length === 1
          ? `No note shows “${baseName(unused[0]!)}” any more.`
          : `No note shows ${unused.length} of these pictures any more.`,
      action: {
        label: 'Move to Trash',
        run: () => void this.removeUnused(unused),
      },
    };
    this.#offer = { notice, paths: unused };
    this.#host.notice.value = notice;
  }

  #withdrawOfferShownBy(text: string, notePath: string): void {
    const offer = this.#offer;
    if (!offer) return;
    if (this.#host.notice.peek() !== offer.notice) {
      this.#offer = null;
      return;
    }
    if (offer.paths.some((p) => this.shows(text, notePath, p))) {
      this.#offer = null;
      this.#host.notice.value = null;
    }
  }

  /**
   * Delete those of `paths` no note shows now, leaving out the notes in `ignoring` (the no-longer-used offer,
   * Find Unused Pictures, Delete Picture's Redo). A picture a note shows again by now is kept, and so is every
   * one when a note can't be read.
   */
  async removeUnused(paths: readonly string[], ignoring: ReadonlySet<string> = new Set()): Promise<void> {
    if (this.#host.closed()) return;
    let linked: PictureUsage;
    try {
      linked = await this.#linked(ignoring);
    } catch (e) {
      this.#host.report(e);
      return;
    }
    const unused = paths.filter((p) => !linked.has(p));
    const kept = paths.length - unused.length;
    const { done, items } = await this.#deleteEach(unused);
    if (kept > 0) {
      const which = kept === 1 ? `“${baseName(paths.find((p) => linked.has(p))!)}”` : `${kept} pictures`;
      this.#host.notify('info', `Kept ${which}: a note shows ${kept === 1 ? 'it' : 'them'} again.`);
    } else this.#sayDeleted(done, items);
  }

  /** Delete each of `paths`, whoever shows them (Delete Picture's Delete File). */
  async removeAll(paths: readonly string[]): Promise<void> {
    if (this.#host.closed()) return;
    const { done, items } = await this.#deleteEach(paths);
    this.#sayDeleted(done, items);
  }

  async #deleteEach(paths: readonly string[]): Promise<{ done: number; items: TrashItem[] }> {
    let done = 0;
    const items: TrashItem[] = [];
    for (const p of paths) {
      const result = await this.#host.deleteEntry(p);
      if (result === false) continue;
      done++;
      if (result) items.push(result);
    }
    return { done, items };
  }

  #sayDeleted(done: number, items: readonly TrashItem[]): void {
    if (done === 0) return;
    const what = `${done} ${done === 1 ? 'picture' : 'pictures'}`;
    if (items.length > 0) this.#host.trash.offerUndo(items, `Moved ${what} to the notebook’s Trash.`);
    else this.#host.notify('info', `Moved ${what} to the Trash.`);
  }
}
