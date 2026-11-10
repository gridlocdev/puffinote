import type { TextDocument } from '@puffinote/text';
import { BlockStates, type BlockState } from './blockState.ts';
import { contentLength } from './chars.ts';
import { tokenizeInline } from './inlineTokenizer.ts';
import {
  isFrontMatterFenceLine,
  isSetextTextLine,
  setextUnderlineLevel,
  tokenizeLine,
  tokenizeLineInline,
} from './lineTokenizer.ts';
import { token, type Token } from './token.ts';

/** The result of an incremental update: the new model and the first line that was re-tokenized. */
export interface MarkdownModelUpdate {
  readonly model: MarkdownModel;
  readonly dirtyFromLine: number;
}

/**
 * The tokenized view of a document: per-line tokens plus the block-state checkpoint cache (PRD §8.1, §8.2).
 * Every line records the state it was entered with, so rendering a viewport can resume from the nearest line
 * above, and an edit re-tokenizes only from its first affected line down, reusing the untouched prefix.
 */
export class MarkdownModel {
  private readonly entering: readonly BlockState[];
  private readonly lines: readonly (readonly Token[])[];

  private constructor(entering: readonly BlockState[], lines: readonly (readonly Token[])[]) {
    this.entering = entering;
    this.lines = lines;
  }

  /** Number of lines covered. */
  get lineCount(): number {
    return this.lines.length;
  }

  /** The block state `line` is entered with (the checkpoint above it). */
  enteringState(line: number): BlockState {
    return this.entering[line]!;
  }

  /** The tokens of `line`, in source order. */
  lineTokens(line: number): readonly Token[] {
    return this.lines[line]!;
  }

  /** All tokens in document order. */
  allTokens(): Token[] {
    return this.lines.flat();
  }

  /** Tokenize a whole document from scratch. */
  static build(document: TextDocument): MarkdownModel {
    const n = document.lineCount;
    const entering = new Array<BlockState>(n);
    const tokens = new Array<Token[]>(n);
    tokenizeRange(document, 0, n, BlockStates.start, entering, tokens);
    return new MarkdownModel(entering, tokens);
  }

  /**
   * Re-tokenize after an edit, reusing the untouched prefix `[0, firstChangedLine)`. The worst case is O(n),
   * such as an opening fence typed at the top (PRD §8.2); a common edit re-runs a short tail.
   */
  update(newDocument: TextDocument, firstChangedLine: number): MarkdownModelUpdate {
    const n = newDocument.lineCount;
    let first = Math.min(Math.max(firstChangedLine, 0), Math.max(0, n - 1));

    // A table body row depends on the header above it, which carried state doesn't track: back up to the
    // start of any run of pipe rows so the whole table is re-evaluated.
    while (first > 0 && isPotentialTableRow(newDocument.getLineText(first - 1))) first--;

    // A setext heading reaches backwards: typing or deleting the underline makes or unmakes the line above.
    if (first > 0 && isSetextTextLine(newDocument.getLineText(first - 1), false)) first--;

    // Never resume inside a setext pair: step back onto the pair's text line.
    while (
      first > 0 &&
      setextUnderlineLevel(newDocument.getLineText(first)) > 0 &&
      isSetextTextLine(newDocument.getLineText(first - 1), false)
    ) {
      first--;
    }

    // Every line above `first` is unchanged, so its entering state is too.
    const startState = first < this.entering.length ? this.entering[first]! : BlockStates.normal;
    const keep = Math.min(first, this.entering.length);
    const entering = this.entering.slice(0, keep) as BlockState[];
    const tokens = this.lines.slice(0, keep) as Token[][];
    entering.length = n;
    tokens.length = n;
    tokenizeRange(newDocument, first, n, startState, entering, tokens);
    return { model: new MarkdownModel(entering, tokens), dirtyFromLine: first };
  }
}

/**
 * Tokenize lines `[fromLine, toLine)`. Handles carried block state and the two one-line lookaheads: a table
 * (a header row followed by a delimiter row) and a setext heading (paragraph text followed by an underline).
 */
function tokenizeRange(
  doc: TextDocument,
  fromLine: number,
  toLine: number,
  startState: BlockState,
  entering: BlockState[],
  tokens: Token[][],
): void {
  // Front matter opens at line 0 only when a closing fence exists below it.
  const allowFrontMatter =
    doc.lineCount >= 2 && isFrontMatterFenceLine(doc.getLineText(0)) && hasClosingFrontMatterFence(doc);

  let state = startState;
  let i = fromLine;
  while (i < toLine) {
    entering[i] = state;

    if (
      state.kind === 'normal' &&
      i + 1 < doc.lineCount &&
      isPotentialTableRow(doc.getLineText(i)) &&
      isTableDelimiterRow(doc.getLineText(i + 1))
    ) {
      i = tokenizeTable(doc, i, toLine, entering, tokens);
      state = BlockStates.normal; // a table row is non-blank, so the next line isn't a fresh block position
      continue;
    }

    // Checking the setext pair before the line below is classified is what stops its `---` from being taken
    // for a horizontal rule.
    if (
      state.kind === 'normal' &&
      i + 1 < toLine &&
      isSetextTextLine(doc.getLineText(i), allowFrontMatter && i === 0)
    ) {
      const level = setextUnderlineLevel(doc.getLineText(i + 1));
      if (level > 0) {
        i = tokenizeSetextHeading(doc, i, level, entering, tokens);
        state = BlockStates.normal;
        continue;
      }
    }

    const lineTokens: Token[] = [];
    state = tokenizeLine(doc.getLineText(i), doc.getLineStart(i), i, state, lineTokens, allowFrontMatter);
    tokens[i] = lineTokens;
    i++;
  }
}

/**
 * A setext heading: the text line carries a zero-length `headingMarker` with the level, so it is shaped like
 * an ATX heading; the underline below is one `setextUnderline`. Returns the line after the pair.
 */
function tokenizeSetextHeading(
  doc: TextDocument,
  line: number,
  level: number,
  entering: BlockState[],
  tokens: Token[][],
): number {
  const start = doc.getLineStart(line);
  const heading: Token[] = [token(start, 0, 'headingMarker', 'marker', String(level))];
  tokenizeLineInline(doc.getLineText(line), start, heading);
  entering[line] = BlockStates.normal;
  tokens[line] = heading;

  const underline = line + 1;
  entering[underline] = BlockStates.normal;
  tokens[underline] = [
    token(
      doc.getLineStart(underline),
      contentLength(doc.getLineText(underline)),
      'setextUnderline',
      'marker',
    ),
  ];
  return underline + 1;
}

/** Tokenize a contiguous GFM pipe table; returns the line after it. */
function tokenizeTable(
  doc: TextDocument,
  start: number,
  toLine: number,
  entering: BlockState[],
  tokens: Token[][],
): number {
  let i = start;
  entering[i] = BlockStates.normal;
  tokens[i] = tokenizeTableRow(doc, i, false);
  i++;
  if (i < toLine) {
    entering[i] = BlockStates.normal;
    tokens[i] = tokenizeTableRow(doc, i, true);
    i++;
  }
  while (i < toLine && isPotentialTableRow(doc.getLineText(i))) {
    entering[i] = BlockStates.normal;
    tokens[i] = tokenizeTableRow(doc, i, false);
    i++;
  }
  return i;
}

function tokenizeTableRow(doc: TextDocument, line: number, isDelimiter: boolean): Token[] {
  const text = doc.getLineText(line);
  const start = doc.getLineStart(line);
  const len = contentLength(text);
  const output: Token[] = [];

  if (isDelimiter) {
    if (len > 0) output.push(token(start, len, 'tableDelimiter', 'marker'));
    return output;
  }

  let cellStart = 0;
  for (let k = 0; k <= len; k++) {
    const isPipe = k < len && isUnescapedPipe(text, k);
    if (isPipe || k === len) {
      if (k > cellStart) tokenizeInline(text, start, cellStart, k, output);
      if (isPipe) {
        output.push(token(start + k, 1, 'marker', 'marker'));
        cellStart = k + 1;
      }
    }
  }
  return output;
}

function isUnescapedPipe(line: string, k: number): boolean {
  return line.charAt(k) === '|' && (k === 0 || line.charAt(k - 1) !== '\\');
}

function hasClosingFrontMatterFence(doc: TextDocument): boolean {
  for (let i = 1; i < doc.lineCount; i++) {
    if (isFrontMatterFenceLine(doc.getLineText(i))) return true;
  }
  return false;
}

function isPotentialTableRow(line: string): boolean {
  const len = contentLength(line);
  for (let k = 0; k < len; k++) {
    if (isUnescapedPipe(line, k)) return true;
  }
  return false;
}

function isTableDelimiterRow(line: string): boolean {
  const len = contentLength(line);
  let sawDash = false;
  let sawContent = false;
  for (let k = 0; k < len; k++) {
    const c = line.charAt(k);
    if (c === '-') {
      sawDash = true;
      sawContent = true;
    } else if (c === '|' || c === ':') {
      sawContent = true;
    } else if (c !== ' ') {
      return false;
    }
  }
  return sawDash && sawContent;
}
