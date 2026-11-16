import { nextBoundary, previousBoundary } from '../document/grapheme.ts';
import {
  caretAt,
  isCollapsed,
  selectionLength,
  selectionsEqual,
  selectionStart,
  type Selection,
} from '../document/selection.ts';
import { computeStats, type DocumentStats } from '../document/stats.ts';
import { TextDocument } from '../document/textDocument.ts';
import { EditHistory, type UndoEffect, type UndoRun } from './editHistory.ts';
import { reloadEdit } from './reloadEdit.ts';

/** A run of Backspace presses undoes as one step (PRD §10.1). */
export const deleteBackRun: UndoRun = { group: 'deleteBack', join: true };
/** A run of Delete presses undoes as one step (PRD §10.1). */
export const deleteForwardRun: UndoRun = { group: 'deleteForward', join: true };
import { applyEdit, caretAfter as editCaretAfter, invertEdit, replaceEdit } from './textEdit.ts';

export type BufferListener = () => void;

/**
 * The editable document: the current immutable `TextDocument`, the selection, the undo history
 * and the dirty flag. The UI never changes a `TextDocument` directly; it goes through these
 * methods, so every change is undoable and the dirty and caret state stay consistent. File
 * I/O is not here; the host owns it.
 */
export class DocumentBuffer {
  private readonly history = new EditHistory();
  private selectionValue: Selection = caretAt(0);
  private statsCache: DocumentStats | undefined;
  private dirtyCache: { doc: TextDocument; saved: TextDocument; dirty: boolean } | undefined;
  private readonly changeListeners = new Set<BufferListener>();
  private readonly textListeners = new Set<BufferListener>();

  document: TextDocument;
  /** The snapshot last marked saved. The dirty flag compares against it. */
  savedDocument: TextDocument;
  /**
   * The first line touched by the most recent text change. Lines above it are unchanged, so an
   * incremental tokenizer can reuse what it derived from them.
   */
  lastChangedLine = 0;

  constructor(text?: string) {
    this.document = TextDocument.fromString(text);
    this.savedDocument = this.document;
  }

  /** Called after any change to the text or the selection. Returns an unsubscribe function. */
  onChange(listener: BufferListener): () => void {
    this.changeListeners.add(listener);
    return () => this.changeListeners.delete(listener);
  }

  /** Called after a change to the text only. Returns an unsubscribe function. */
  onTextChange(listener: BufferListener): () => void {
    this.textListeners.add(listener);
    return () => this.textListeners.delete(listener);
  }

  get selection(): Selection {
    return this.selectionValue;
  }

  /** End the current typing run, so the next edit starts a new undo step (commands, paste). */
  breakCoalescing(): void {
    this.history.breakCoalescing();
  }

  set selection(value: Selection) {
    const clamped = this.clamp(value);
    if (selectionsEqual(clamped, this.selectionValue)) return;
    this.history.breakCoalescing(); // a caret jump ends the typing run
    this.selectionValue = clamped;
    this.changeListeners.forEach((l) => l());
  }

  /**
   * True when the text differs from the saved baseline. Snapshots are compared by text, not identity,
   * so undoing (or retyping) back to the saved text is clean again. The answer is cached per pair of
   * snapshots, so repeated reads between edits cost nothing.
   */
  get isDirty(): boolean {
    const doc = this.document;
    const saved = this.savedDocument;
    if (doc === saved) return false;
    if (doc.length !== saved.length) return true;
    const cache = this.dirtyCache;
    if (cache && cache.doc === doc && cache.saved === saved) return cache.dirty;
    const dirty = doc.getText() !== saved.getText();
    this.dirtyCache = { doc, saved, dirty };
    return dirty;
  }

  get canUndo(): boolean {
    return this.history.canUndo;
  }

  get canRedo(): boolean {
    return this.history.canRedo;
  }

  /** Statistics, cached until the text changes, so caret moves don't rescan the document. */
  get stats(): DocumentStats {
    return (this.statsCache ??= computeStats(this.document));
  }

  /** Replace the selection with `text`, or insert it at the caret. */
  insert(text: string): void {
    const s = this.selectionValue;
    this.replaceRange(selectionStart(s), selectionLength(s), text);
  }

  /** Delete the selection, or else the grapheme before the caret. A run of these undoes as one step. */
  backspace(): void {
    const s = this.selectionValue;
    if (!isCollapsed(s)) return this.replaceRange(selectionStart(s), selectionLength(s), '');
    if (s.caret === 0) return;
    const prev = this.previousCaret(s.caret);
    this.replaceRange(prev, s.caret - prev, '', undefined, undefined, deleteBackRun);
  }

  /** Delete the selection, or else the grapheme after the caret. A run of these undoes as one step. */
  deleteForward(): void {
    const s = this.selectionValue;
    if (!isCollapsed(s)) return this.replaceRange(selectionStart(s), selectionLength(s), '');
    if (s.caret >= this.document.length) return;
    const next = this.nextCaret(s.caret);
    this.replaceRange(s.caret, next - s.caret, '', undefined, undefined, deleteForwardRun);
  }

  /**
   * Replace `[start, start + length)` with `text` as one undoable edit. The caret goes to
   * `caretAfter` when given, otherwise just after the inserted text. `effect` is work outside the text that
   * undoing and redoing this edit also undo and redo. `run` makes the edit part of a key's run (`UndoRun`).
   */
  replaceRange(
    start: number,
    length: number,
    text: string,
    caretAfter?: number,
    effect?: UndoEffect,
    run?: UndoRun,
  ): void {
    const edit = replaceEdit(this.document, start, length, text);
    if (edit.removed.length === 0 && edit.inserted.length === 0) return;
    const before = this.selectionValue;
    this.document = applyEdit(edit, this.document);
    this.selectionValue = caretAt(caretAfter ?? editCaretAfter(edit));
    this.history.record(edit, before, this.selectionValue, effect, run);
    this.textChanged(this.document.getPosition(edit.start).line);
  }

  undo(): boolean {
    const entry = this.history.popUndo();
    if (!entry) return false;
    this.document = applyEdit(invertEdit(entry.edit), this.document);
    this.selectionValue = this.clamp(entry.before);
    this.textChanged(this.document.getPosition(entry.edit.start).line);
    entry.effect?.undo();
    return true;
  }

  redo(): boolean {
    const entry = this.history.popRedo();
    if (!entry) return false;
    this.document = applyEdit(entry.edit, this.document);
    this.selectionValue = this.clamp(entry.after);
    this.textChanged(this.document.getPosition(entry.edit.start).line);
    entry.effect?.redo();
    return true;
  }

  /** Replace the whole document (open, revert). Clears the undo history. */
  load(text: string | null | undefined, markSaved = true): void {
    this.document = TextDocument.fromString(text);
    if (markSaved) this.savedDocument = this.document;
    this.selectionValue = caretAt(0);
    this.history.clear();
    this.textChanged(0);
  }

  /**
   * Take the version on disk as one undoable edit, which is what "Reload" means when the file
   * changed underneath. Undo gives back what was in the editor. The reloaded text becomes the
   * saved baseline, so undoing the reload makes the document dirty again. The caret stays where
   * `reloadEdit` puts it.
   */
  reloadFrom(text: string | null | undefined): void {
    const edit = reloadEdit(this.document.getText(), text ?? '', this.selectionValue.caret);
    if (edit) {
      // Its own undo step, never folded into the typing around it.
      this.history.breakCoalescing();
      this.replaceRange(edit.start, edit.length, edit.text, edit.caret);
      this.history.breakCoalescing();
    }
    this.markSaved();
  }

  markSaved(): void {
    this.savedDocument = this.document;
  }

  /**
   * The caret position one grapheme after `offset`. Works on the line's text (with its `\n`)
   * instead of the whole document, so a caret move costs O(line), and `\r\n` still counts as one
   * grapheme across the line break.
   */
  nextCaret(offset: number): number {
    const doc = this.document;
    offset = Math.min(doc.length, Math.max(0, offset));
    if (offset >= doc.length) return doc.length;
    const line = doc.getPosition(offset).line;
    const start = doc.getLineStart(line);
    const end = line + 1 < doc.lineCount ? doc.getLineStart(line + 1) : doc.length;
    return start + nextBoundary(doc.getText(start, end - start), offset - start);
  }

  /** The caret position one grapheme before `offset`, in O(line). */
  previousCaret(offset: number): number {
    const doc = this.document;
    offset = Math.min(doc.length, Math.max(0, offset));
    if (offset <= 0) return 0;
    const line = doc.getPosition(offset).line;
    const start = doc.getLineStart(line);
    if (offset === start) {
      // At a line start, step back over the previous line's newline (a \r\n stays whole).
      const prevStart = doc.getLineStart(line - 1);
      return prevStart + previousBoundary(doc.getText(prevStart, start - prevStart), start - prevStart);
    }
    const end = line + 1 < doc.lineCount ? doc.getLineStart(line + 1) : doc.length;
    return start + previousBoundary(doc.getText(start, end - start), offset - start);
  }

  private clamp(s: Selection): Selection {
    const len = this.document.length;
    return {
      anchor: Math.min(len, Math.max(0, s.anchor)),
      caret: Math.min(len, Math.max(0, s.caret)),
    };
  }

  private textChanged(firstChangedLine: number): void {
    this.lastChangedLine = firstChangedLine;
    this.statsCache = undefined;
    this.textListeners.forEach((l) => l());
    this.changeListeners.forEach((l) => l());
  }
}
