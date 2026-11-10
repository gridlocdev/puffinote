/**
 * A caret and selection: an `anchor` (where the selection started) and a moving `caret`. When
 * the two are equal, the selection is empty and is just a caret. Offsets are code units.
 */
export interface Selection {
  readonly anchor: number;
  readonly caret: number;
}

/** A collapsed caret at `offset`. */
export function caretAt(offset: number): Selection {
  return { anchor: offset, caret: offset };
}

export function selectionStart(s: Selection): number {
  return Math.min(s.anchor, s.caret);
}

export function selectionEnd(s: Selection): number {
  return Math.max(s.anchor, s.caret);
}

export function selectionLength(s: Selection): number {
  return Math.abs(s.caret - s.anchor);
}

export function isCollapsed(s: Selection): boolean {
  return s.anchor === s.caret;
}

export function selectionsEqual(a: Selection, b: Selection): boolean {
  return a.anchor === b.anchor && a.caret === b.caret;
}

/** True when `offset` is inside the range or on one of its ends. */
export function selectionContains(s: Selection, offset: number): boolean {
  return offset >= selectionStart(s) && offset <= selectionEnd(s);
}

/** True when `s` overlaps `[start, start + length)`. Touching at a boundary counts. */
export function selectionIntersects(s: Selection, start: number, length: number): boolean {
  return selectionStart(s) <= start + length && selectionEnd(s) >= start;
}

/** A 0-based line and column. The UI shows it 1-based as "line:col". */
export interface LinePosition {
  readonly line: number;
  readonly column: number;
}

export function formatLinePosition(p: LinePosition): string {
  return `${p.line + 1}:${p.column + 1}`;
}
