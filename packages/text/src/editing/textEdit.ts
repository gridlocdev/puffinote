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

/**
 * True when `next`, applied after `prev`, touches the text `prev` left: its removed range meets or overlaps
 * `prev`'s inserted range. Such edits can be folded into one with `compose`.
 */
export function touches(prev: TextEdit, next: TextEdit): boolean {
  const end = prev.start + prev.inserted.length;
  return next.start <= end && next.start + next.removed.length >= prev.start;
}

/**
 * One edit that does what `prev` then `next` do, for two edits that `touch`. The changed region of the text
 * between them is covered by `prev.inserted` and `next.removed`, so both sides can be rebuilt from the edits.
 */
export function compose(prev: TextEdit, next: TextEdit): TextEdit {
  const start = Math.min(prev.start, next.start);
  const head = next.start < prev.start ? next.removed.slice(0, prev.start - next.start) : '';
  const tail = next.removed.slice(Math.max(0, prev.start + prev.inserted.length - next.start));
  const middle = head + prev.inserted + tail; // the text between the two edits, from `start`
  const p = prev.start - start;
  const n = next.start - start;
  return {
    start,
    removed: middle.slice(0, p) + prev.removed + middle.slice(p + prev.inserted.length),
    inserted: middle.slice(0, n) + next.inserted + middle.slice(n + next.removed.length),
  };
}
