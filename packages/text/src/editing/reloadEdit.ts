import { nextBoundary, previousBoundary } from '../document/grapheme.ts';

/** The one replacement that turns the editor's text into a newer version of the file, and where the caret goes. */
export interface ReloadEdit {
  readonly start: number;
  readonly length: number;
  readonly text: string;
  readonly caret: number;
}

/**
 * Take `next` (the file as it is now) in place of `current` as the smallest single replacement, so the undo
 * step and the caret keep their footing. The caret keeps its offset where `next` is long enough, moved back
 * to the start of the character it would land inside: a caret in the middle of an emoji would split it on
 * the next keystroke, and half an emoji saves as U+FFFD. Null when the texts are the same.
 */
export function reloadEdit(current: string, next: string, caret: number): ReloadEdit | null {
  if (current === next) return null;
  const max = Math.min(current.length, next.length);
  let p = 0;
  while (p < max && current.charCodeAt(p) === next.charCodeAt(p)) p++;
  let s = 0;
  while (s < max - p && current.charCodeAt(current.length - 1 - s) === next.charCodeAt(next.length - 1 - s))
    s++;
  const at = Math.min(Math.max(0, caret), next.length);
  return {
    start: p,
    length: current.length - p - s,
    text: next.slice(p, next.length - s),
    caret: at === next.length ? at : previousBoundary(next, nextBoundary(next, at)),
  };
}
