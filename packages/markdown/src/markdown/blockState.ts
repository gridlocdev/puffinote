/** The multi-line block a line is inside when tokenization reaches it. */
export type BlockKind = 'normal' | 'fencedCode' | 'frontMatter' | 'indentedCode';

/**
 * The block state carried from one line to the next (PRD §11.1). Only constructs that truly span lines need
 * carried state: fenced code (its fence char and length, so only a matching fence closes it), front matter,
 * indented code, and the list items a line may sit inside. The checkpoint cache stores this at every line so
 * a viewport can resume tokenizing just above itself.
 *
 * `afterBlank` is true when the previous line was blank, or this is the top of the document: the only
 * position an indented code block may open from (CommonMark §4.4).
 *
 * `listColumns` holds the content column of each open list item, outermost first (`- a` opens one at column
 * 2). Indentation inside an item counts from its content column, so `    - b` after a blank line under `- a`
 * is a nested item rather than indented code, and code inside an item needs four columns past that.
 */
export interface BlockState {
  readonly kind: BlockKind;
  readonly fenceChar: string;
  readonly fenceLength: number;
  readonly language: string | null;
  readonly afterBlank: boolean;
  readonly listColumns: readonly number[];
}

const noLists: readonly number[] = Object.freeze([]);

function state(
  kind: BlockKind,
  fenceChar = '',
  fenceLength = 0,
  language: string | null = null,
  afterBlank = false,
  listColumns: readonly number[] = noLists,
): BlockState {
  return { kind, fenceChar, fenceLength, language, afterBlank, listColumns };
}

export const BlockStates = {
  /** Between blocks; the previous line was non-blank. */
  normal: state('normal'),
  /** The start of the document: normal, but a fresh block position. */
  start: state('normal', '', 0, null, true),
  frontMatter: state('frontMatter'),
  indentedCode: state('indentedCode'),
  /** Normal state entering a line, recording whether the line above it was blank, inside `listColumns`. */
  normalAfter(blank: boolean, listColumns: readonly number[] = noLists): BlockState {
    if (listColumns.length === 0) return blank ? BlockStates.start : BlockStates.normal;
    return state('normal', '', 0, null, blank, listColumns);
  },
  /** Indented code inside the list items `listColumns`. */
  indentedCodeIn(listColumns: readonly number[]): BlockState {
    return listColumns.length === 0
      ? BlockStates.indentedCode
      : state('indentedCode', '', 0, null, false, listColumns);
  },
  fenced(
    fenceChar: string,
    fenceLength: number,
    language: string | null,
    listColumns: readonly number[] = noLists,
  ): BlockState {
    return state('fencedCode', fenceChar, fenceLength, language, false, listColumns);
  },
} as const;

/** The column indentation inside the innermost open list item counts from (0 outside a list). */
export function listBase(s: BlockState): number {
  return s.listColumns.length === 0 ? 0 : s.listColumns[s.listColumns.length - 1]!;
}

/** Structural equality of two states, list context included. */
export function sameBlockState(a: BlockState, b: BlockState): boolean {
  if (a === b) return true;
  if (
    a.kind !== b.kind ||
    a.fenceChar !== b.fenceChar ||
    a.fenceLength !== b.fenceLength ||
    a.language !== b.language ||
    a.afterBlank !== b.afterBlank ||
    a.listColumns.length !== b.listColumns.length
  ) {
    return false;
  }
  return a.listColumns.every((c, i) => c === b.listColumns[i]);
}
