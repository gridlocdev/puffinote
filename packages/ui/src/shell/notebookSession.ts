import { signal } from '@preact/signals';
import type { Notebook } from '../notebook/notebook.ts';

/** What switching notebooks needs of the shell. */
export interface SessionHost {
  /**
   * Another notebook is coming in front: nothing opened over the old one may act on the new one (a question
   * still up, the picture preview, the find bar).
   */
  leaving(): void;
  /** A notebook was put in front (the same one again included): reset the window's view of it. */
  entered(): void;
  /** Ask about unsaved tabs before they go; false when the user cancels. */
  confirmDiscardAll(): Promise<boolean>;
}

/**
 * Which notebook the window shows, and the switch from one to the next: closing the old one, the view state
 * that belongs to one notebook (the Filter and Search boxes, the Library's popover), and opens that a later
 * one overtook.
 */
export class NotebookSession {
  readonly notebook = signal<Notebook | null>(null);
  /** The Filter and Search panels' boxes, kept while the user looks elsewhere. */
  readonly filterQuery = signal('');
  readonly searchQuery = signal('');
  /** The Library layout's popover under the path bar, if one is open. */
  readonly popover = signal<'outline' | 'recent' | null>(null);
  readonly #host: SessionHost;
  /** Bumps when an open of another notebook starts (see `adopt`). */
  #ticket = 0;

  constructor(host: SessionHost) {
    this.#host = host;
  }

  /** Put `nb` in front, closing the notebook that was there. */
  set(nb: Notebook | null): void {
    const old = this.notebook.peek();
    if (old !== nb) this.#host.leaving();
    if (old && old !== nb) old.dispose();
    this.#host.entered();
    this.filterQuery.value = '';
    this.searchQuery.value = '';
    this.popover.value = null;
    this.notebook.value = nb;
  }

  /** Say that opening another notebook has started; pass the ticket to `adopt` when it is read. */
  beginOpen(): number {
    return ++this.#ticket;
  }

  /**
   * Put `nb`, opened under `ticket`, in place of the notebook in front. Reading a notebook can be slow (the NAS),
   * so this asks about unsaved tabs again just before the switch: text typed meanwhile is never dropped without
   * asking. An open that a later one overtook, or that the user cancels here, is closed and resolves false.
   */
  async adopt(nb: Notebook, ticket: number): Promise<boolean> {
    const stale = () => ticket !== this.#ticket;
    if (stale() || !(await this.#host.confirmDiscardAll()) || stale()) {
      if (this.notebook.peek() !== nb) nb.dispose();
      return false;
    }
    this.set(nb);
    return true;
  }
}
