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

/** One line's tokens, as the run that made them left them. */
interface LineEntry {
  readonly tokens: readonly Token[];
  /** The line's start offset when it was tokenized. Tokens of a line that has since moved shift lazily. */
  readonly base: number;
  /**
   * Whether the run reached this line at the top of its loop, so its tokens depend only on its entering
   * state and the text from here down. False for the lines a table or setext pair consumed with the line
   * that opened it.
   */
  readonly top: boolean;
}

/** Where an update may stop: past `afterLine`, new line `i` is old line `i - lineDelta`, text unchanged. */
interface Convergence {
  readonly afterLine: number;
  readonly lineDelta: number;
  readonly oldEntering: readonly BlockState[];
  readonly oldLines: readonly LineEntry[];
}

/**
 * The tokenized view of a document: per-line tokens plus the block-state checkpoint cache (PRD §11.1, §11.2).
 * Every line records the state it was entered with, so rendering a viewport can resume from the nearest line
 * above, and an edit re-tokenizes only from its first affected line down to where the block state converges
 * with the previous run, reusing the untouched prefix and the converged tail.
 */
export class MarkdownModel {
  private readonly doc: TextDocument;
  private readonly entering: readonly BlockState[];
  /** Entries are replaced in place when a moved line's tokens are first shifted; the array is this model's own. */
  private readonly lines: LineEntry[];
  /** Whether line 0 may open front matter, which depends on a closing fence anywhere below it. */
  private readonly allowFrontMatter: boolean;

  private constructor(
    doc: TextDocument,
    entering: readonly BlockState[],
    lines: LineEntry[],
    allowFrontMatter: boolean,
  ) {
    this.doc = doc;
    this.entering = entering;
    this.lines = lines;
    this.allowFrontMatter = allowFrontMatter;
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
    const entry = this.lines[line]!;
    const start = this.doc.getLineStart(line);
    if (entry.base === start) return entry.tokens;
    // A line carried over from the previous run below an edit: shift its offsets once, on first use.
    const delta = start - entry.base;
    const tokens = entry.tokens.map((t) => ({ ...t, start: t.start + delta }));
    this.lines[line] = { tokens, base: start, top: entry.top };
    return tokens;
  }

  /** All tokens in document order. */
  allTokens(): Token[] {
    const out: Token[] = [];
    for (let line = 0; line < this.lines.length; line++) out.push(...this.lineTokens(line));
    return out;
  }

  /** Tokenize a whole document from scratch. */
  static build(document: TextDocument): MarkdownModel {
    const allow = allowsFrontMatter(document);
    const entering: BlockState[] = [];
    const lines: LineEntry[] = [];
    tokenizeRange(document, 0, BlockStates.start, allow, entering, lines, null);
    return new MarkdownModel(document, entering, lines, allow);
  }

  /**
   * Re-tokenize after an edit, reusing the untouched prefix `[0, firstChangedLine)`. When the new document
   * was made from this model's by one edit (`TextDocument.changeSince`), the run stops at the first line
   * past the edit whose entering state matches the previous run's (PRD §11.2), and the rest is carried
   * over. The worst case is O(n), such as an opening fence typed at the top; a common edit re-runs a few
   * lines. The result always equals `build(newDocument)`.
   */
  update(newDocument: TextDocument, firstChangedLine: number): MarkdownModelUpdate {
    const n = newDocument.lineCount;
    const oldN = this.lines.length;
    const change = newDocument.changeSince(this.doc);
    let first = Math.min(Math.max(firstChangedLine, 0), n - 1, oldN - 1);
    if (change) first = Math.min(first, newDocument.getPosition(change.start).line);

    // Front matter at line 0 depends on a closing fence anywhere below, so typing or deleting that fence
    // changes line 0 without touching it.
    const allow = allowsFrontMatter(newDocument);
    if (allow !== this.allowFrontMatter) first = 0;

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

    // Never resume on a line the previous run reached inside a table or setext pair: its tokens depended on
    // the line that opened the group.
    while (first > 0 && !this.lines[first]!.top) first--;

    // Every line above `first` is unchanged, so its entering state is too.
    const startState = this.entering[first]!;
    const converge: Convergence | null = change
      ? {
          afterLine: newDocument.getPosition(change.start + change.insertedLength).line,
          lineDelta: n - oldN,
          oldEntering: this.entering,
          oldLines: this.lines,
        }
      : null;
    let entering = this.entering.slice(0, first);
    let lines = this.lines.slice(0, first);
    const stop = tokenizeRange(newDocument, first, startState, allow, entering, lines, converge);
    if (stop < n) {
      // Converged: the old run's tail, from the matching line, is this run's. Its tokens shift lazily.
      const from = stop - (n - oldN);
      entering = entering.concat(this.entering.slice(from));
      lines = lines.concat(this.lines.slice(from));
    }
    return { model: new MarkdownModel(newDocument, entering, lines, allow), dirtyFromLine: first };
  }
}

/**
 * May the run stop at new line `i`, entered with `state`? Only past the edit, and only where the old run was
 * at the top of the same (shifted) line with the same entering state.
 */
function converges(c: Convergence, i: number, state: BlockState): boolean {
  if (i <= c.afterLine) return false;
  const old = i - c.lineDelta;
  const entry = c.oldLines[old];
  return entry !== undefined && entry.top && sameState(c.oldEntering[old]!, state);
}

function sameState(a: BlockState, b: BlockState): boolean {
  return (
    a === b ||
    (a.kind === b.kind &&
      a.fenceChar === b.fenceChar &&
      a.fenceLength === b.fenceLength &&
      a.language === b.language &&
      a.afterBlank === b.afterBlank)
  );
}

/** Front matter opens at line 0 only when a closing fence exists below it. */
function allowsFrontMatter(doc: TextDocument): boolean {
  return doc.lineCount >= 2 && isFrontMatterFenceLine(doc.getLineText(0)) && hasClosingFrontMatterFence(doc);
}

/**
 * Tokenize from `fromLine`, appending to `entering` and `lines` (which hold exactly the lines above it), to
 * the end of the document or until `converge` says the rest is unchanged; returns the line it stopped at.
 * Handles carried block state and the two one-line lookaheads: a table (a header row followed by a delimiter
 * row) and a setext heading (paragraph text followed by an underline).
 *
 * Stopping is checked only at the top of the loop. There the run's future depends on nothing but the entering
 * state and the text from this line down, so when both equal the old run's at a line where it too was at the
 * top, everything below comes out the same.
 */
function tokenizeRange(
  doc: TextDocument,
  fromLine: number,
  startState: BlockState,
  allowFrontMatter: boolean,
  entering: BlockState[],
  tokens: LineEntry[],
  converge: Convergence | null,
): number {
  const toLine = doc.lineCount;
  let state = startState;
  let i = fromLine;
  while (i < toLine) {
    if (converge && converges(converge, i, state)) return i;
    entering[i] = state;

    if (
      state.kind === 'normal' &&
      i + 1 < doc.lineCount &&
      isPotentialTableRow(doc.getLineText(i)) &&
      isTableDelimiterRow(doc.getLineText(i + 1), doc.getLineText(i))
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
    const start = doc.getLineStart(i);
    state = tokenizeLine(doc.getLineText(i), start, i, state, lineTokens, allowFrontMatter);
    tokens[i] = { tokens: lineTokens, base: start, top: true };
    i++;
  }
  return toLine;
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
  tokens: LineEntry[],
): number {
  const start = doc.getLineStart(line);
  const heading: Token[] = [token(start, 0, 'headingMarker', 'marker', String(level))];
  tokenizeLineInline(doc.getLineText(line), start, heading);
  // The text line keeps the state it was entered with (set by the caller): an update may resume here.
  tokens[line] = { tokens: heading, base: start, top: true };

  const underline = line + 1;
  const underlineStart = doc.getLineStart(underline);
  entering[underline] = BlockStates.normal;
  tokens[underline] = {
    tokens: [token(underlineStart, contentLength(doc.getLineText(underline)), 'setextUnderline', 'marker')],
    base: underlineStart,
    top: false,
  };
  return underline + 1;
}

/** Tokenize a contiguous GFM pipe table; returns the line after it. */
function tokenizeTable(
  doc: TextDocument,
  start: number,
  toLine: number,
  entering: BlockState[],
  tokens: LineEntry[],
): number {
  // The header row keeps the state it was entered with (set by the caller): an update may resume there.
  const row = (i: number, isDelimiter: boolean): void => {
    if (i !== start) entering[i] = BlockStates.normal;
    tokens[i] = {
      tokens: tokenizeTableRow(doc, i, isDelimiter),
      base: doc.getLineStart(i),
      top: i === start,
    };
  };
  let i = start;
  row(i++, false);
  if (i < toLine) row(i++, true);
  while (i < toLine && isPotentialTableRow(doc.getLineText(i))) row(i++, false);
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

/**
 * Whether `line` is the delimiter row of a table whose header is `header` (GFM): at least one pipe, every
 * cell a run of `-` with optional `:` at either end, and as many cells as the header. Requiring the pipe and
 * the count is what stops a lone `-` typed under `name | price` from turning it into a table for one
 * keystroke (PRD §10.2, §11.3).
 */
function isTableDelimiterRow(line: string, header: string): boolean {
  if (!isPotentialTableRow(line)) return false;
  const cells = tableCells(line);
  return (
    cells.length > 0 &&
    cells.length === tableCells(header).length &&
    cells.every((c) => /^:?-+:?$/.test(c.trim()))
  );
}

/** A table row's cells: the text between unescaped pipes, without the empty edges an outer `|` leaves. */
function tableCells(line: string): string[] {
  const len = contentLength(line);
  const cells: string[] = [];
  let cellStart = 0;
  for (let k = 0; k <= len; k++) {
    if (k === len || isUnescapedPipe(line, k)) {
      cells.push(line.slice(cellStart, k));
      cellStart = k + 1;
    }
  }
  if (cells.length > 1 && cells[0]!.trim() === '') cells.shift();
  if (cells.length > 1 && cells[cells.length - 1]!.trim() === '') cells.pop();
  return cells;
}
