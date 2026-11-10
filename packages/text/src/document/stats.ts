import { countGraphemes } from './grapheme.ts';
import type { TextDocument } from './textDocument.ts';

/** The line-ending convention a document uses. */
export type NewlineStyle = 'lf' | 'crlf';

/** How many of each kind of line ending a document has. */
export interface LineEndingCounts {
  readonly lf: number;
  readonly crlf: number;
}

/**
 * Count the line endings in `text` in one pass. A lone `\r` is not counted: nothing in the
 * family writes one, and treating a stray carriage return as a line break would be worse.
 */
export function countLineEndings(text: string): LineEndingCounts {
  let lf = 0;
  let crlf = 0;
  for (let i = text.indexOf('\n'); i !== -1; i = text.indexOf('\n', i + 1)) {
    if (i > 0 && text.charCodeAt(i - 1) === 13) crlf++;
    else lf++;
  }
  return { lf, crlf };
}

export function isMixed(c: LineEndingCounts): boolean {
  return c.lf > 0 && c.crlf > 0;
}

/** The convention most of the file uses. A tie goes to LF, like a file with no line endings. */
export function dominantNewline(c: LineEndingCounts): NewlineStyle {
  return c.crlf > c.lf ? 'crlf' : 'lf';
}

export function newlineText(style: NewlineStyle): string {
  return style === 'crlf' ? '\r\n' : '\n';
}

/**
 * `text` with every line ending written as `style`. Returns the same string when nothing needs
 * to change, so a caller can skip an edit that would only mark the document dirty.
 */
export function normalizeLineEndings(text: string, style: NewlineStyle): string {
  const counts = countLineEndings(text);
  if (style === 'crlf' ? counts.lf === 0 : counts.crlf === 0) return text;
  // Go through LF, so a \r\n is never doubled into \r\r\n.
  const lf = text.replaceAll('\r\n', '\n');
  return style === 'crlf' ? lf.replaceAll('\n', '\r\n') : lf;
}

/** Average adult silent reading speed in words per minute, for the reading-time estimate. */
export const wordsPerMinute = 200;

/**
 * Document statistics for the status bar and the CLI's `stats` verb, computed in one place so
 * the two never disagree. Characters are grapheme clusters, not code units.
 */
export interface DocumentStats {
  readonly words: number;
  readonly characters: number;
  readonly lines: number;
  readonly readingMinutes: number;
  readonly newline: NewlineStyle;
  readonly newlineCounts: LineEndingCounts;
}

export function computeStats(document: TextDocument): DocumentStats {
  const text = document.getText();
  const words = countWords(text);
  const counts = countLineEndings(text);
  return {
    words,
    characters: countGraphemes(text),
    lines: document.lineCount,
    readingMinutes: words === 0 ? 0 : Math.max(1, Math.ceil(words / wordsPerMinute)),
    newline: dominantNewline(counts),
    newlineCounts: counts,
  };
}

/** "LF", "CRLF", with " (mixed)" when the document uses both. */
export function newlineLabel(stats: DocumentStats): string {
  return (stats.newline === 'crlf' ? 'CRLF' : 'LF') + (isMixed(stats.newlineCounts) ? ' (mixed)' : '');
}

function countWords(text: string): number {
  let count = 0;
  let inWord = false;
  for (const c of text) {
    if (/\s/u.test(c)) inWord = false;
    else if (!inWord) {
      inWord = true;
      count++;
    }
  }
  return count;
}
