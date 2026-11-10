/**
 * Grapheme-cluster boundary math. Caret movement, selection and deletion work on grapheme
 * clusters (what a reader sees as one character), so emoji, combining marks, surrogate pairs
 * and `\r\n` all move and delete as one unit. Built on `Intl.Segmenter`, so it needs no
 * dependency and behaves the same in a browser, a worker and Node.
 *
 * Offsets are UTF-16 code units, the same unit JavaScript strings index by.
 */
const segmenter = new Intl.Segmenter(undefined, { granularity: 'grapheme' });

/** The next grapheme boundary strictly greater than `index`, clamped to the end. */
export function nextBoundary(text: string, index: number): number {
  if (index < 0) return 0;
  if (index >= text.length) return text.length;
  const seg = segmenter.segment(text).containing(index);
  if (!seg) return Math.min(text.length, index + 1);
  return Math.min(text.length, Math.max(index + 1, seg.index + seg.segment.length));
}

/** The previous grapheme boundary strictly less than `index`, clamped to 0. */
export function previousBoundary(text: string, index: number): number {
  if (index <= 0) return 0;
  if (index > text.length) index = text.length;
  const seg = segmenter.segment(text).containing(index - 1);
  return seg ? seg.index : index - 1;
}

/** The number of grapheme clusters in `text`, used for character counts. */
export function countGraphemes(text: string): number {
  let count = 0;
  for (const _ of segmenter.segment(text)) count++;
  return count;
}
