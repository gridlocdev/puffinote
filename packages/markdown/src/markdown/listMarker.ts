import { isAsciiDigit } from './chars.ts';
import { isTaskMark, taskStateFromMark, type TaskState } from './taskState.ts';

/** A list item's marker: `-`, `*` or `+`, or one to nine digits and `.` or `)`; then a space. */
export type ListMarker =
  | { readonly kind: 'unordered'; readonly bullet: string; readonly end: number }
  | {
      readonly kind: 'ordered';
      readonly number: number;
      readonly digitCount: number;
      readonly delim: string;
      readonly end: number;
    };

/**
 * The list marker starting at `at` in `line` (whose content ends at `len`), or null. `end` is the column just
 * past the marker's space. The tokenizer and the editing assists both read markers through this, so what is
 * styled as a list item and what Enter continues as one can't drift apart.
 */
export function scanListMarker(line: string, at: number, len: number): ListMarker | null {
  if (at >= len) return null;
  const c = line.charAt(at);
  if (c === '-' || c === '*' || c === '+') {
    return at + 1 < len && line.charAt(at + 1) === ' ' ? { kind: 'unordered', bullet: c, end: at + 2 } : null;
  }
  let k = at;
  while (k < len && k - at < 9 && isAsciiDigit(line.charAt(k))) k++;
  if (k === at || k >= len) return null;
  const d = line.charAt(k);
  if ((d !== '.' && d !== ')') || k + 1 >= len || line.charAt(k + 1) !== ' ') return null;
  return {
    kind: 'ordered',
    number: Number.parseInt(line.slice(at, k), 10),
    digitCount: k - at,
    delim: d,
    end: k + 2,
  };
}

/**
 * A task box (`[ ]`, `[/]`, `[x]`) at `start`, followed by a space or the end of the line. `end` is past that
 * space.
 */
export function scanTaskBox(
  line: string,
  start: number,
  len: number,
): { end: number; state: TaskState } | null {
  if (
    start + 2 < len &&
    line.charAt(start) === '[' &&
    line.charAt(start + 2) === ']' &&
    isTaskMark(line.charAt(start + 1))
  ) {
    const after = start + 3;
    if (after >= len || line.charAt(after) === ' ') {
      return { end: after < len ? after + 1 : after, state: taskStateFromMark(line.charAt(start + 1)) };
    }
  }
  return null;
}
