import { effect, signal, type Signal } from '@preact/signals';
import { utf8NoBom, type TextFileFormat } from '@puffinote/text';
import { EditorState } from '../editor/editorState.ts';
import { baseName } from '../platform/paths.ts';
import type { RecoveryEntry } from './recovery.ts';

/** How a tab's file stands against the disk: as read, changed by someone else, or gone. */
export type TabConflict = 'none' | 'changed' | 'deleted';

/** One open document: a note in the editor, or a picture. */
let nextTabId = 0;

export class Tab {
  /** Stable for the tab's life, for keying its view. */
  readonly id = ++nextTabId;
  readonly path: Signal<string>;
  readonly kind: 'note' | 'image';
  /** The note's editor state; null for a picture. */
  readonly state: EditorState | null;
  /** Unsaved edits. */
  readonly dirty = signal(false);
  readonly conflict = signal<TabConflict>('none');
  /** Just saved: the title bar says "— Saved" for a moment (PRD §7). */
  readonly saved = signal(false);
  /** The encoding was guessed; saving would write the guess back, so the note is read-only. */
  readonly readOnly: boolean;
  /** A picture's URL. */
  readonly imageUrl: string | null;
  format: TextFileFormat;
  #version: string | null;
  #savedText: string;
  #generation = 0;
  /** Where the editor was scrolled, restored when the tab comes back. */
  scrollTop = 0;
  /** Kept at the front of the strip and in view (PRD §7); Close Other and Close All leave it open. */
  readonly pinned = signal(false);
  /**
   * Unsaved text a crash or a closed page left behind, different from the file (PRD §5): the banner offers to
   * restore or discard it. Until then the copy is kept and no new one is taken.
   */
  readonly recovered = signal<RecoveryEntry | null>(null);
  /** The user threw the edits away (Don't Save): stop keeping recovery copies. */
  recoveryOff = false;
  private readonly disposers: (() => void)[] = [];

  constructor(
    path: string,
    init: {
      kind: 'note' | 'image';
      text?: string;
      format?: TextFileFormat;
      version: string | null;
      readOnly?: boolean;
      imageUrl?: string | null;
    },
  ) {
    this.path = signal(path);
    this.kind = init.kind;
    this.format = init.format ?? utf8NoBom;
    this.#version = init.version;
    this.#savedText = init.text ?? '';
    this.readOnly = init.readOnly ?? false;
    this.imageUrl = init.imageUrl ?? null;
    this.state = init.kind === 'note' ? new EditorState(this.#savedText) : null;
    if (this.state) {
      const state = this.state;
      this.disposers.push(
        effect(() => {
          void state.version.value;
          const dirty = state.text !== this.#savedText;
          this.dirty.value = dirty;
          if (dirty) this.saved.value = false;
        }),
      );
    }
  }

  get name(): string {
    return baseName(this.path.value);
  }

  /** The version token of the file as this tab last read or wrote it. */
  get version(): string | null {
    return this.#version;
  }

  /** The text on disk as this tab last read or wrote it: the tab is dirty when the editor's text differs. */
  get savedText(): string {
    return this.#savedText;
  }

  /**
   * Changes whenever `version` or `savedText` does (a save, a reload, a move), so a refresh that read the
   * disk before the change can tell its answer is stale and drop it.
   */
  get generation(): number {
    return this.#generation;
  }

  /** Mark the current text as what's on disk. */
  markSaved(version: string | null): void {
    this.#setDisk(version, this.state?.text ?? '');
    this.dirty.value = false;
    this.conflict.value = 'none';
  }

  /**
   * A save of `text` in `format` landed as `version`. Edits made while it was in flight stay unsaved, and the
   * file is now in the format that save wrote, whatever a reload in the meantime read.
   */
  savedAs(version: string | null, text: string, format: TextFileFormat = this.format): void {
    this.#setDisk(version, text);
    this.format = format;
    this.dirty.value = (this.state?.text ?? '') !== text;
    this.conflict.value = 'none';
  }

  /** The file on disk has the text this tab saved, under a new version token (touched, or moved). */
  adoptVersion(version: string | null): void {
    this.#setDisk(version, this.#savedText);
  }

  /** The one place the disk state changes, so `generation` always moves with it. */
  #setDisk(version: string | null, savedText: string): void {
    this.#version = version;
    this.#savedText = savedText;
    this.#generation++;
  }

  onDispose(fn: () => void): void {
    this.disposers.push(fn);
  }

  dispose(): void {
    for (const d of this.disposers) d();
    this.disposers.length = 0;
  }
}
