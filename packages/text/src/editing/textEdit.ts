import type { TextDocument } from '../document/textDocument.ts';

/**
 * One reversible change: replace `removed` at `start` with `inserted`. Both texts are kept, so
 * the edit can be applied and undone against an immutable `TextDocument` with nothing else.
 */
export interface TextEdit {
  readonly start: number;
  readonly removed: string;
  readonly inserted: string;
}

/** The edit that replaces `[start, start + length)` of `document` with `inserted`. */
export function replaceEdit(
  document: TextDocument,
  start: number,
  length: number,
  inserted: string,
): TextEdit {
  return { start, removed: length === 0 ? '' : document.getText(start, length), inserted };
}

export function applyEdit(edit: TextEdit, document: TextDocument): TextDocument {
  return document.replace(edit.start, edit.removed.length, edit.inserted);
}

/** The edit that undoes `edit`. */
export function invertEdit(edit: TextEdit): TextEdit {
  return { start: edit.start, removed: edit.inserted, inserted: edit.removed };
}

/** The caret just past the inserted text. */
export function caretAfter(edit: TextEdit): number {
  return edit.start + edit.inserted.length;
}

/**
 * True when `next` can fold into `prev` as one undo step, so a run of typing undoes at once.
 * Only pure insertions that continue exactly where `prev` ended merge, and a newline on
 * either side ends the run.
 */
export function canCoalesce(prev: TextEdit, next: TextEdit): boolean {
  return (
    prev.removed.length === 0 &&
    next.removed.length === 0 &&
    next.start === caretAfter(prev) &&
    next.inserted.length > 0 &&
    !next.inserted.endsWith('\n') &&
    prev.inserted.length > 0 &&
    !prev.inserted.endsWith('\n')
  );
}

export function coalesce(prev: TextEdit, next: TextEdit): TextEdit {
  return { start: prev.start, removed: prev.removed, inserted: prev.inserted + next.inserted };
}
