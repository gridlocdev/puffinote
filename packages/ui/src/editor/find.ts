/** Find in Note (PRD §10.7): the matches of a query in a note's source, and stepping between them. */

export interface TextRange {
  readonly start: number;
  readonly end: number;
}

/** At most this many matches are found; a query that matches more stops counting. */
export const MAX_MATCHES = 10_000;

/** Every match of `query` in `text`, ignoring case, left to right and not overlapping. */
export function findAll(text: string, query: string): TextRange[] {
  if (!query) return [];
  const re = new RegExp(query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'giu');
  const out: TextRange[] = [];
  for (let m = re.exec(text); m && out.length < MAX_MATCHES; m = re.exec(text)) {
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
