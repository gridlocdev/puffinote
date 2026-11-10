import type { Selection } from '../document/selection.ts';
import { canCoalesce, coalesce, type TextEdit } from './textEdit.ts';

/** One undo step: the edit, plus the selection before and after it. */
export interface HistoryEntry {
  readonly edit: TextEdit;
  readonly before: Selection;
  readonly after: Selection;
}

/**
 * The undo and redo stacks. They live for the session only, hold at most `maxDepth` steps
 * (the oldest is dropped), and are never written to the file. Recording a new edit clears the
 * redo stack. A run of typing coalesces into one step until the caret jumps or
 * `breakCoalescing` is called.
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
  record(edit: TextEdit, before: Selection, after: Selection): void {
    this.redoStack.length = 0;
    const top = this.undoStack[this.undoStack.length - 1];
    if (!this.coalesceBreak && top && canCoalesce(top.edit, edit)) {
      this.undoStack[this.undoStack.length - 1] = { ...top, edit: coalesce(top.edit, edit), after };
    } else {
      this.undoStack.push({ edit, before, after });
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
