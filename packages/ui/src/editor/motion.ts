import { parseLinePrefix } from '@puffinote/markdown';
import {
  isCollapsed,
  nextBoundary,
  previousBoundary,
  selectionEnd,
  selectionStart,
  type Selection,
  type TextDocument,
} from '@puffinote/text';
import type { ViewMode } from './reveal.ts';

const words = new Intl.Segmenter(undefined, { granularity: 'word' });

/** ⌥→: the end of the next word, crossing to the next line at a line end. */
export function wordRight(doc: TextDocument, offset: number): number {
  const { line, column } = doc.getPosition(offset);
  const text = doc.getLineText(line).replace(/\r$/, '');
  if (column >= text.length) return line + 1 < doc.lineCount ? doc.getLineStart(line + 1) : offset;
  for (const s of words.segment(text)) {
    const end = s.index + s.segment.length;
    if (end > column && s.isWordLike) return doc.getLineStart(line) + end;
  }
  return doc.getLineStart(line) + text.length;
}

/** ⌥←: the start of the previous word, crossing to the previous line at a line start. */
export function wordLeft(doc: TextDocument, offset: number): number {
  const { line, column } = doc.getPosition(offset);
  if (column === 0) return line > 0 ? doc.getLineEnd(line - 1) - (endsWithCr(doc, line - 1) ? 1 : 0) : 0;
  const text = doc.getLineText(line).replace(/\r$/, '');
  let best = 0;
  for (const s of words.segment(text)) {
    if (s.index >= column) break;
    if (s.isWordLike) best = s.index;
  }
  return doc.getLineStart(line) + best;
}

function endsWithCr(doc: TextDocument, line: number): boolean {
  return doc.getLineText(line).endsWith('\r');
}

/** The end of `line`'s content, before any CR. */
export function lineContentEnd(doc: TextDocument, line: number): number {
  return doc.getLineEnd(line) - (endsWithCr(doc, line) ? 1 : 0);
}

/** The word (or run of other characters) at `offset`, for a double-click. */
export function wordRangeAt(doc: TextDocument, offset: number): { start: number; end: number } {
  const { line, column } = doc.getPosition(offset);
  const lineStart = doc.getLineStart(line);
  const text = doc.getLineText(line).replace(/\r$/, '');
  for (const s of words.segment(text)) {
    const end = s.index + s.segment.length;
    if (column >= s.index && column < end) return { start: lineStart + s.index, end: lineStart + end };
  }
  return { start: offset, end: offset };
}

/**
 * Where a list item's text starts on `line`, past its bullet and task box (`- [ ] |text`); the line start
 * when it isn't a list item. In Rich view the bullet and box draw as decorations, and a caret belongs after them.
 */
export function itemTextStart(doc: TextDocument, line: number): number {
  const p = parseLinePrefix(doc.getLineText(line));
  return doc.getLineStart(line) + (p.hasList ? p.length : 0);
}

/**
 * Home (⌘← on macOS) in Rich view from `from`: the start of a list item's text, or, pressed there (or from
 * inside the marker), the start of the line, so the marker can still be reached.
 */
export function smartLineStart(doc: TextDocument, from: number): number {
  const line = doc.getPosition(from).line;
  const text = itemTextStart(doc, line);
  return from > text ? text : doc.getLineStart(line);
}

/**
 * ⌘⌫ (PRD §10.1): where deleting back to the line start stops, given the start of the caret's visual row and
 * where the line's text starts (past a list item's marker and box in Rich view). A wrapped paragraph loses
 * only the caret's row, and a list item keeps its bullet or checkbox. Null when nothing is before the caret
 * on the row, so ⌘⌫ deletes as Backspace does.
 */
export function lineDeleteStart(caret: number, rowStart: number, textStart: number): number | null {
  const start = Math.max(rowStart, textStart);
  return start < caret ? start : null;
}

/**
 * `col` moved to a grapheme boundary of `text`: unchanged when it is on one, else the end (`dir` 1) or start
 * (`dir` -1) of the grapheme it falls inside. The browser can place a click between the code units of an
 * emoji or a combining sequence; a caret there would split the character on the next keystroke.
 */
export function graphemeColumn(text: string, col: number, dir: 1 | -1): number {
  if (col <= 0 || col >= text.length) return col;
  const start = previousBoundary(text, col + 1);
  if (start === col) return col;
  return dir === 1 ? nextBoundary(text, start) : start;
}

/**
 * Home and ⌘← (PRD §10.1): on a wrapped line's later rows, the start of the caret's visual row (`rowStart`);
 * on the first row, `lineStart` — where Home went before rows counted: a list item's text first, then the
 * line start. `textStart` is where the item's text starts (the line start on other lines).
 */
export function rowStartTarget(rowStart: number, textStart: number, lineStart: number): number {
  return rowStart > textStart ? rowStart : lineStart;
}

/** Where a line's text starts in `mode`: past a list item's marker in Rich view, else the line start. */
export function textStartIn(doc: TextDocument, mode: ViewMode, line: number): number {
  return mode === 'live' ? itemTextStart(doc, line) : doc.getLineStart(line);
}

/** Home, ⌘↑: the start of a list item's text first in Rich view (PRD §10.2). */
export function lineStartFor(doc: TextDocument, mode: ViewMode, line: number, from: number): number {
  if (mode !== 'live') return doc.getLineStart(line);
  return doc.getPosition(from).line === line ? smartLineStart(doc, from) : itemTextStart(doc, line);
}

/**
 * ← and → across a list item's hidden marker in Rich view (PRD §10.2): ← from the start of an item's text
 * goes to the end of the line above, and → from the end of a line goes to the start of the next item's
 * text, so the bullet or number is one step, not one per character. Null for an ordinary step.
 */
export function listHop(doc: TextDocument, mode: ViewMode, at: number, dir: 1 | -1): number | null {
  if (mode !== 'live') return null;
  const line = doc.getPosition(at).line;
  if (dir < 0) {
    const text = itemTextStart(doc, line);
    return line > 0 && at === text && text > doc.getLineStart(line) ? lineContentEnd(doc, line - 1) : null;
  }
  if (line + 1 >= doc.lineCount || at !== lineContentEnd(doc, line)) return null;
  const next = itemTextStart(doc, line + 1);
  return next > doc.getLineStart(line + 1) ? next : null;
}

export type CaretUnit = 'char' | 'word' | 'row' | 'line';

/**
 * What `caretMotion` needs from the drawn note: how its lines wrap into rows, which markers are concealed, and
 * where the caret may stop between characters. The editor answers from the DOM; tests answer from a model.
 */
export interface CaretLayout {
  /** The offset at the start (`dir` -1) or end (1) of the visual row `offset` is drawn on; null if not drawn as text. */
  rowEdge(offset: number, dir: 1 | -1): number | null;
  /** Whether a caret at `offset` is drawn on a lower row than one at `from`. */
  drawnBelow(offset: number, from: number): boolean;
  /** `offset` stepped out of a concealed marker toward `dir` (concealed runs are atomic). */
  snap(offset: number, dir: 1 | -1): number;
  /** The next and previous caret stops (grapheme boundaries, a line break as one). */
  nextCaret(offset: number): number;
  previousCaret(offset: number): number;
}

/**
 * Where ← or → (by character, word, visual row or line; PRD §10.1) takes the caret from `sel`. A collapsing
 * step without Shift goes to the selection's edge.
 */
export function caretMotion(
  doc: TextDocument,
  mode: ViewMode,
  sel: Selection,
  dir: 1 | -1,
  extend: boolean,
  unit: CaretUnit,
  layout: CaretLayout,
): number {
  if (!extend && !isCollapsed(sel) && unit === 'char')
    return dir > 0 ? selectionEnd(sel) : selectionStart(sel);
  const at = sel.caret;
  const line = doc.getPosition(at).line;
  switch (unit) {
    case 'word':
      return dir > 0 ? wordRight(doc, at) : wordLeft(doc, at);
    case 'line':
      return dir > 0 ? lineContentEnd(doc, line) : lineStartFor(doc, mode, line, at);
    case 'row':
      return dir > 0 ? rowEnd(doc, at, layout) : rowStart(doc, mode, at, layout);
    case 'char':
      return (
        listHop(doc, mode, at, dir) ??
        layout.snap(dir > 0 ? layout.nextCaret(at) : layout.previousCaret(at), dir)
      );
  }
}

/** Home, ⌘←: the start of the caret's visual row; on a line's first row, as `lineStartFor` (PRD §10.1). */
function rowStart(doc: TextDocument, mode: ViewMode, at: number, layout: CaretLayout): number {
  const line = doc.getPosition(at).line;
  const row = layout.rowEdge(at, -1) ?? doc.getLineStart(line);
  return rowStartTarget(row, textStartIn(doc, mode, line), lineStartFor(doc, mode, line, at));
}

/**
 * End, ⌘→: the end of the caret's visual row. Where a line wraps, the offset after the row's last character
 * is drawn at the start of the next row, so the caret stops before that character (the space the line
 * wrapped at) instead.
 */
function rowEnd(doc: TextDocument, at: number, layout: CaretLayout): number {
  const end = lineContentEnd(doc, doc.getPosition(at).line);
  const row = layout.rowEdge(at, 1);
  if (row === null || row >= end || row < at) return end;
  if (layout.drawnBelow(row, at)) {
    const before = layout.previousCaret(row);
    return before >= at ? before : row;
  }
  return row;
}
