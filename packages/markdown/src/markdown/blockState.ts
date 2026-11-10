/** The multi-line block a line is inside when tokenization reaches it. */
export type BlockKind = 'normal' | 'fencedCode' | 'frontMatter' | 'indentedCode';

/**
 * The block state carried from one line to the next (PRD §8.1). Only constructs that truly span lines need
 * carried state: fenced code (its fence char and length, so only a matching fence closes it), front matter
 * and indented code. The checkpoint cache stores this at every line so a viewport can resume tokenizing just
 * above itself.
 *
 * `afterBlank` is true when the previous line was blank, or this is the top of the document: the only
 * position an indented code block may open from (CommonMark §4.4).
 */
export interface BlockState {
  readonly kind: BlockKind;
  readonly fenceChar: string;
  readonly fenceLength: number;
  readonly language: string | null;
  readonly afterBlank: boolean;
}

function state(
  kind: BlockKind,
  fenceChar = '',
  fenceLength = 0,
  language: string | null = null,
  afterBlank = false,
): BlockState {
  return { kind, fenceChar, fenceLength, language, afterBlank };
}

export const BlockStates = {
  /** Between blocks; the previous line was non-blank. */
  normal: state('normal'),
  /** The start of the document: normal, but a fresh block position. */
  start: state('normal', '', 0, null, true),
  frontMatter: state('frontMatter'),
  indentedCode: state('indentedCode'),
  /** Normal state entering a line, recording whether the line above it was blank. */
  normalAfter(blank: boolean): BlockState {
    return blank ? BlockStates.start : BlockStates.normal;
  },
  fenced(fenceChar: string, fenceLength: number, language: string | null): BlockState {
    return state('fencedCode', fenceChar, fenceLength, language);
  },
} as const;
