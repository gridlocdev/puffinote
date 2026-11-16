import type { NotebookStore } from '../platform/notebookStore.ts';
import { baseName } from '../platform/paths.ts';
import {
  emptyTrash,
  hasNotebookTrash,
  listTrash,
  moveToTrash,
  restoreFromTrash,
  type TrashItem,
} from './trash.ts';

/** What the Trash needs of the notebook. */
export interface TrashHost {
  readonly store: NotebookStore;
  /** Whether the notebook closed; a closed notebook changes no files. */
  closed(): boolean;
  /** Items came back at `paths`: show them. */
  restored(paths: readonly string[]): Promise<void>;
  /** Put a message in the status bar, with a button. */
  offer(text: string, label: string, run: () => void): void;
  notify(kind: 'info' | 'error', text: string): void;
  report(e: unknown): void;
}

/**
 * Where deleted files go (PRD §6.1): the notebook's own Trash on the stores without a system Trash (browser,
 * NAS), the system's on the desktop.
 */
export class NotebookTrash {
  readonly #host: TrashHost;

  constructor(host: TrashHost) {
    this.#host = host;
  }

  /** Whether deleted files go to the notebook's own Trash rather than the system's. */
  get available(): boolean {
    return hasNotebookTrash(this.#host.store);
  }

  /**
   * Delete `path`, a `kind`, to the notebook's Trash or the system's. Returns the item in the notebook's
   * Trash, or null when it went to the system's. Throws when it failed.
   */
  async take(path: string, kind: 'file' | 'dir'): Promise<TrashItem | null> {
    const { store } = this.#host;
    if (this.available) return moveToTrash(store, path, kind);
    await store.remove(path);
    return null;
  }

  /** The status bar says what went to the Trash, with Undo. */
  offerUndo(items: readonly TrashItem[], text: string): void {
    this.#host.offer(text, 'Undo', () => {
      this.restore(items).catch((e: unknown) => this.#host.report(e));
    });
  }

  /** What is in the notebook's Trash, newest first (none on the desktop, whose files go to the system Trash). */
  async items(): Promise<TrashItem[]> {
    if (!this.available) return [];
    return listTrash(this.#host.store);
  }

  /**
   * Put trashed items back where they were, under a free name when the old one is taken (never overwriting).
   * Returns the paths they went to.
   */
  async restore(items: readonly TrashItem[]): Promise<string[]> {
    const host = this.#host;
    if (host.closed()) return [];
    const out: string[] = [];
    for (const item of items) {
      try {
        out.push(await restoreFromTrash(host.store, item));
      } catch (e) {
        host.report(e);
      }
    }
    if (out.length === 0) return out;
    await host.restored(out);
    const renamed = items.length === 1 && out[0] !== items[0]!.path;
    if (out.length === 1) {
      host.notify(
        'info',
        renamed
          ? `Restored “${baseName(items[0]!.path)}” as “${baseName(out[0]!)}”: the name was taken.`
          : `Restored “${baseName(out[0]!)}”.`,
      );
    } else host.notify('info', `Restored ${out.length} items.`);
    return out;
  }

  /** Delete everything in the notebook's Trash for good (the shell confirms first). */
  async empty(): Promise<boolean> {
    const host = this.#host;
    if (host.closed() || !this.available) return false;
    try {
      await emptyTrash(host.store);
    } catch (e) {
      host.report(e);
      return false;
    }
    host.notify('info', 'The notebook’s Trash is empty.');
    return true;
  }
}
