import { isWhiteSpace } from './chars.ts';

/**
 * One paired-delimiter construct inside a stretch of inline content: the opening run starts at `openStart`,
 * the closing run at `innerEnd`, each `markerLen` chars of `delim`.
 */
export interface InlineDelimiterSpan {
  readonly openStart: number;
  readonly innerEnd: number;
  readonly markerLen: number;
  readonly delim: string;
}

/** First column of the construct's content. */
export function innerStart(span: InlineDelimiterSpan): number {
  return span.openStart + span.markerLen;
}

/** One past the closing run: the end of the construct's whole source span. */
export function spanEnd(span: InlineDelimiterSpan): number {
  return span.innerEnd + span.markerLen;
}

/**
 * The top-level paired constructs (`~~`, `==`, and `*`/`_` runs of 1–3) inside `content`, in source order.
 *
 * The tokenizer emits emphasis, strike and highlight content as one flat token (PRD §8.4), so in
 * `***~~bold~~***` the `~~` has no token of its own. Anything that must see one level further in scans here:
 * the surface to draw the nested strike, the formatting toggles to find the run to remove. One level deep.
 */
export function findNestedDelimiters(content: string): InlineDelimiterSpan[] {
  const found: InlineDelimiterSpan[] = [];
  let i = 0;
  while (i < content.length) {
    const c = content.charAt(i);
    if (c === '~' || c === '=' || c === '*' || c === '_') {
      const run = delimRun(content, i, c);
      const count = c === '~' || c === '=' ? 2 : Math.min(run, 3);
      const inner = i + count;
      if (
        run >= count &&
        inner < content.length &&
        content.charAt(inner) !== c &&
        !isWhiteSpace(content.charAt(inner))
      ) {
        const close = findClose(content, inner, c, count);
        if (close > inner) {
          found.push({ openStart: i, innerEnd: close, markerLen: count, delim: c });
          i = close + count;
          continue;
        }
      }
    }
    i++;
  }
  return found;
}

function findClose(content: string, from: number, c: string, count: number): number {
  let j = from;
  while (j < content.length) {
    if (content.charAt(j) === c && !isWhiteSpace(content.charAt(j - 1))) {
      const run = delimRun(content, j, c);
      if (run >= count) return j;
      j += run;
    } else {
      j++;
    }
  }
  return -1;
}

function delimRun(s: string, i: number, c: string): number {
  let n = 0;
  while (i + n < s.length && s.charAt(i + n) === c) n++;
  return n;
}
