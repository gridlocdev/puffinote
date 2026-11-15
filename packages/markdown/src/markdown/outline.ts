import type { MarkdownModel } from './markdownModel.ts';
import type { TextDocument } from '@puffinote/text';

/** A heading in the active note (PRD §6.2, the Outline panel). */
export interface OutlineItem {
  readonly level: number;
  readonly text: string;
  readonly line: number;
  /** Offset of the heading's text, where selecting the entry puts the caret. */
  readonly offset: number;
}

/** The note's ATX and setext headings, in order (the engine marks a setext text line with a zero-length marker). Headings inside code or front matter don't count. */
export function outlineOf(doc: TextDocument, model: MarkdownModel): OutlineItem[] {
  const out: OutlineItem[] = [];
  for (let line = 0; line < doc.lineCount; line++) {
    const tokens = model.lineTokens(line);
    // A heading inside a quote (`> ## Note`) belongs to the quote, not to the note's outline.
    const marker = tokens[0]?.type === 'headingMarker' ? tokens[0] : undefined;
    if (marker) {
      const text = doc.getLineText(line).replace(/\r$/, '');
      // An ATX heading's `#` run and closing sequence are markup. A setext heading's marker is zero-length,
      // and its text keeps every `#` it has (`#hashtag` over `===`).
      const atx = marker.length > 0;
      const content = atx ? withoutClosingSequence(text.replace(/^\s*#{1,6}[ \t]*/, '')).trim() : text.trim();
      const start = doc.getLineStart(line);
      out.push({
        level: Number(marker.meta),
        text: plain(content),
        line,
        offset: start + text.indexOf(content),
      });
    }
  }
  return out;
}

const isBlank = (c: string): boolean => c === ' ' || c === '\t';

/**
 * `s` without an ATX closing sequence: blanks, a run of `#`, then optional blanks at the end. A hand scan,
 * because the regex for it (`/[ \t]+#+[ \t]*$/`) backtracks quadratically over a long run of blanks.
 */
function withoutClosingSequence(s: string): string {
  let end = s.length;
  while (end > 0 && isBlank(s.charAt(end - 1))) end--;
  let hashes = end;
  while (hashes > 0 && s.charAt(hashes - 1) === '#') hashes--;
  if (hashes === end) return s;
  let blanks = hashes;
  while (blanks > 0 && isBlank(s.charAt(blanks - 1))) blanks--;
  return blanks < hashes ? s.slice(0, blanks) : s;
}

/**
 * Heading text without inline markup, for the panel. Links and images (`[text](url)`, `![alt](url)`) keep
 * their text. A hand scan with remembered searches, because the equivalent regex
 * (`/!?\[([^\]]*)\]\([^)]*\)/g`) rescans to the end of the line for every unmatched `[`.
 */
function plain(text: string): string {
  let out = '';
  let p = 0;
  let copied = 0;
  // The next `]` / `)` at or after a position; -1 once none is left. Positions only grow, so each is reused.
  let closeBracket = -2;
  let closeParen = -2;
  while (p < text.length) {
    const q = text.charAt(p) === '!' ? p + 1 : p;
    if (text.charAt(q) === '[') {
      if (closeBracket !== -1 && closeBracket < q + 1) closeBracket = text.indexOf(']', q + 1);
      const r = closeBracket;
      if (r >= 0 && text.charAt(r + 1) === '(') {
        if (closeParen !== -1 && closeParen < r + 2) closeParen = text.indexOf(')', r + 2);
        const c = closeParen;
        if (c >= 0) {
          out += text.slice(copied, p) + text.slice(q + 1, r);
          p = copied = c + 1;
          continue;
        }
      }
    }
    p++;
  }
  out += text.slice(copied);
  return out.replace(/(\*\*|__|~~|==|\*|_|`)/g, '').trim();
}
