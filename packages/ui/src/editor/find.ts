/** Find in Note (PRD §10.7): the matches of a query in a note's source, and stepping between them. */

export interface TextRange {
  readonly start: number;
  readonly end: number;
}

/** At most this many matches are found; a query that matches more stops counting. */
export const MAX_MATCHES = 10_000;

/**
 * Every match of `query` in `text`, ignoring case, left to right and not overlapping; at most `limit` of them
 * (Replace All passes no limit).
 */
export function findAll(text: string, query: string, limit = MAX_MATCHES): TextRange[] {
  if (!query) return [];
  const re = new RegExp(query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'giu');
  const out: TextRange[] = [];
  for (let m = re.exec(text); m && out.length < limit; m = re.exec(text)) {
    out.push({ start: m.index, end: m.index + m[0].length });
  }
  return out;
}

/**
 * The match to go to from `offset` (the selection's start): `0` the first at or after it (typing into the box
 * keeps the match it is on), `1` the next after it, `-1` the last before it. It wraps around the note; -1 when
 * there are no matches.
 */
export function matchFrom(matches: readonly TextRange[], offset: number, dir: -1 | 0 | 1): number {
  if (matches.length === 0) return -1;
  if (dir === -1) {
    for (let i = matches.length - 1; i >= 0; i--) if (matches[i]!.start < offset) return i;
    return matches.length - 1;
  }
  const i = matches.findIndex((m) => (dir === 0 ? m.start >= offset : m.start > offset));
  return i === -1 ? 0 : i;
}

/** The index of the match that is exactly `[start, end)`, or -1. */
export function matchAt(matches: readonly TextRange[], start: number, end: number): number {
  let lo = 0;
  let hi = matches.length - 1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    const m = matches[mid]!;
    if (m.start === start) return m.end === end ? mid : -1;
    if (m.start < start) lo = mid + 1;
    else hi = mid - 1;
  }
  return -1;
}

/**
 * The matches that overlap `[start, end)`, as the index range `[from, to)` into `matches` (sorted and not
 * overlapping, as `findAll` returns them), so the surface draws only the matches near the rows in view.
 */
export function matchesBetween(matches: readonly TextRange[], start: number, end: number): [number, number] {
  let lo = 0;
  let hi = matches.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (matches[mid]!.end <= start) lo = mid + 1;
    else hi = mid;
  }
  let to = lo;
  while (to < matches.length && matches[to]!.start < end) to++;
  return [lo, to];
}

/** One edit, as the editor applies it: replace `[start, start + length)` with `text`, then select. */
export interface ReplaceEdit {
  readonly start: number;
  readonly length: number;
  readonly text: string;
  readonly anchor: number;
  readonly caret: number;
}

/**
 * Replace All (PRD §10.7) as one edit, so it undoes as one step: the span from the first match to the end of
 * the last, with every match in it replaced by `replacement` (taken as typed) and the text between them kept.
 * Nothing outside that span is touched. The caret goes to the end of the last replacement. Null when there
 * is nothing to replace.
 */
export function replaceAllEdit(
  text: string,
  matches: readonly TextRange[],
  replacement: string,
): ReplaceEdit | null {
  const first = matches[0];
  const last = matches[matches.length - 1];
  if (!first || !last) return null;
  let out = '';
  let at = first.start;
  for (const m of matches) {
    out += text.slice(at, m.start) + replacement;
    at = m.end;
  }
  const caret = first.start + out.length;
  return { start: first.start, length: last.end - first.start, text: out, anchor: caret, caret };
}
