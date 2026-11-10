import { parseLinePrefix } from '@puffinote/markdown';
import type { TextDocument } from '@puffinote/text';

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
