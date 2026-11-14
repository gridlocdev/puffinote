import type { Selection } from '../document/selection.ts';
import { canCoalesce, coalesce, compose, touches, type TextEdit } from './textEdit.ts';

/**
 * Work outside the text that belongs to one undo step (Delete Picture's file): `undo` runs when the step is
 * undone, `redo` when it is redone.
 */
export interface UndoEffect {
  readonly undo: () => void;
  readonly redo: () => void;
}

/**
 * Which run of keys an edit belongs to, for edits that coalesce by key rather than as typing: a run of
 * Backspace (`group: 'deleteBack'`), a run of Delete, or one key held down (`key:Enter`). An edit with
 * `join` set folds into the step before it when that step is of the same group and the two edits touch. A
 * key's first press records with `join` false and its auto-repeats with `join` true, so a held key undoes
 * as one step while separate presses stay separate.
 */
export interface UndoRun {
  readonly group: string;
  readonly join: boolean;
}

/** One undo step: the edit, plus the selection before and after it, and any work outside the text. */
export interface HistoryEntry {
  readonly edit: TextEdit;
  readonly before: Selection;
  readonly after: Selection;
  readonly effect?: UndoEffect;
  /** The run the step belongs to (see `UndoRun`); none for typing and commands. */
  readonly group?: string;
}

/**
 * The undo and redo stacks. They live for the session only, hold at most `maxDepth` steps
 * (the oldest is dropped), and are never written to the file. Recording a new edit clears the
 * redo stack. A run of typing, or of one key (`UndoRun`), coalesces into one step until the caret jumps,
 * another kind of edit comes, or `breakCoalescing` is called.
 */
export class EditHistory {
  static readonly maxDepth = 1000;

  private readonly undoStack: HistoryEntry[] = [];
  private readonly redoStack: HistoryEntry[] = [];
  private coalesceBreak = true;

  get canUndo(): boolean {
    return this.undoStack.length > 0;
  }

  get canRedo(): boolean {
    return this.redoStack.length > 0;
  }

  /** Make the next recorded edit start a new undo step. */
  breakCoalescing(): void {
    this.coalesceBreak = true;
  }

  /** Record an edit that has already been applied. */
  record(edit: TextEdit, before: Selection, after: Selection, effect?: UndoEffect, run?: UndoRun): void {
    this.redoStack.length = 0;
    const top = this.undoStack[this.undoStack.length - 1];
    const open = top && !effect && !top.effect && !this.coalesceBreak;
    if (open && run && run.join && top.group === run.group && touches(top.edit, edit)) {
      this.undoStack[this.undoStack.length - 1] = { ...top, edit: compose(top.edit, edit), after };
    } else if (open && !run && top.group === undefined && canCoalesce(top.edit, edit)) {
      this.undoStack[this.undoStack.length - 1] = { ...top, edit: coalesce(top.edit, edit), after };
    } else {
      const entry: HistoryEntry = { edit, before, after };
      this.undoStack.push({
        ...entry,
        ...(effect ? { effect } : {}),
        ...(run ? { group: run.group } : {}),
      });
      if (this.undoStack.length > EditHistory.maxDepth) this.undoStack.shift();
    }
    this.coalesceBreak = false;
  }

  popUndo(): HistoryEntry | undefined {
    const entry = this.undoStack.pop();
    if (!entry) return undefined;
    this.redoStack.push(entry);
    this.coalesceBreak = true;
    return entry;
  }

  popRedo(): HistoryEntry | undefined {
    const entry = this.redoStack.pop();
    if (!entry) return undefined;
    this.undoStack.push(entry);
    this.coalesceBreak = true;
    return entry;
  }

  clear(): void {
    this.undoStack.length = 0;
    this.redoStack.length = 0;
    this.coalesceBreak = true;
  }
}
