import { previousBoundary, type Selection, type TextDocument } from '@puffinote/text';

/**
 * What the hidden `<textarea>` holds between keystrokes (PRD §10.1). The editor reads typing from it and
 * clears it, so normally it is empty. Two things are mirrored in it:
 *
 * - **A selection**, selected, so the browser enables Copy and Cut. Typing replaces it like any selection.
 * - **The letter before an empty selection**, with the input's caret after it. The OS edits text by ranges
 *   it reads from the input: press-and-hold accents on macOS (hold `e`, choose `é`) replace the letter
 *   before the caret, and an input method may too. With that letter in the input, the change the OS makes to
 *   it is the change to make to the note. Only a letter is mirrored, so nothing that rewrites punctuation
 *   (smart dashes: `-` `-` to `—`) has anything to act on.
 */
export interface InputMirror {
  readonly kind: 'selection' | 'context';
  readonly text: string;
}

export const emptyMirror: InputMirror = { kind: 'context', text: '' };

/** A letter, with any combining marks after it (`é` written as `e` and U+0301). */
const letter = /^\p{L}\p{M}*$/u;

/**
 * What the input mirrors for `sel`: the selected text (or one space for a selection longer than `maxSelection`,
 * since copy writes the real source either way), or for an empty selection the letter just before the caret
 * on its line (none at a line start or after anything but a letter).
 */
export function mirrorFor(doc: TextDocument, sel: Selection, maxSelection: number): InputMirror {
  if (sel.anchor !== sel.caret) {
    const start = Math.min(sel.anchor, sel.caret);
    const length = Math.abs(sel.caret - sel.anchor);
    return { kind: 'selection', text: length <= maxSelection ? doc.getText(start, length) : ' ' };
  }
  const caret = Math.min(sel.caret, doc.length);
  const line = doc.getPosition(caret).line;
  const lineStart = doc.getLineStart(line);
  if (caret === lineStart) return emptyMirror;
  const before = doc.getText(lineStart, caret - lineStart);
  const g = before.slice(previousBoundary(before, before.length));
  return letter.test(g) ? { kind: 'context', text: g } : emptyMirror;
}

/** What to do with the input's text: type it at the selection, or replace text just before the caret. */
export type InputEdit =
  | { readonly kind: 'type'; readonly text: string }
  | { readonly kind: 'replace'; readonly start: number; readonly length: number; readonly text: string };

/**
 * Read the input's `value` against what it mirrored (`mirror`), with the note as it is (`doc`, `sel`). Null when
 * there is nothing to add.
 *
 * - With a selection mirrored, the whole value is typed over the selection, as before mirroring.
 * - With a letter mirrored, the value normally starts with it, and the rest is typed at the caret. When the OS
 *   replaced the letter (a press-and-hold accent), the letter is replaced in the note, as one edit.
 * - Nothing here ever only deletes: deleting is the keys' job, so a value with nothing new in it (an input
 *   cleared under a late event) changes nothing. And if the note no longer holds the mirrored letter before
 *   the caret, the new text is typed at the caret instead of replacing anything.
 */
export function readInput(
  mirror: InputMirror,
  value: string,
  doc: TextDocument,
  sel: Selection,
): InputEdit | null {
  if (mirror.kind === 'selection') return value ? { kind: 'type', text: value } : null;
  const ctx = mirror.text;
  let same = 0;
  while (same < ctx.length && same < value.length && ctx.charCodeAt(same) === value.charCodeAt(same)) same++;
  const text = value.slice(same);
  if (!text) return null;
  const replaced = ctx.length - same;
  if (replaced === 0) return { kind: 'type', text };
  const caret = sel.caret;
  const start = caret - replaced;
  const intact =
    sel.anchor === caret && caret - ctx.length >= 0 && doc.getText(caret - ctx.length, ctx.length) === ctx;
  if (!intact) return { kind: 'type', text };
  return { kind: 'replace', start, length: replaced, text };
}
