import { BlockStates, type BlockState } from './blockState.ts';
import { contentLength, isAsciiDigit } from './chars.ts';
import { tokenizeInline } from './inlineTokenizer.ts';
import { isTaskMark, taskMark, taskStateFromMark, type TaskState } from './taskState.ts';
import { token, type Token } from './token.ts';

/**
 * Tokenizes one line given the block state it is entered with, and returns the state the next line is
 * entered with. This is the heart of the incremental line-based tokenizer (PRD §11): no document AST, just a
 * per-line classifier driven by carried block state. Tables and setext headings need a neighbouring line and
 * are decided one level up, in the model.
 *
 * `lineIndex` gates front matter, which may only open on the very first line.
 */
export function tokenizeLine(
  line: string,
  lineStart: number,
  lineIndex: number,
  entering: BlockState,
  output: Token[],
  allowFrontMatterOpen = true,
): BlockState {
  const len = contentLength(line);
  switch (entering.kind) {
    case 'frontMatter':
      return inFrontMatter(line, lineStart, len, output);
    case 'fencedCode':
      return inFencedCode(line, lineStart, len, entering, output);
    case 'indentedCode':
      return inIndentedCode(line, lineStart, len, lineIndex, allowFrontMatterOpen, output);
    default:
      return normal(line, lineStart, len, lineIndex, allowFrontMatterOpen, entering.afterBlank, output);
  }
}

/**
 * Inside an indented code block a line indented four columns or more, or any blank line, stays in the block.
 * The first non-blank line dedented below four ends it and is classified normally (CommonMark §4.4).
 */
function inIndentedCode(
  line: string,
  lineStart: number,
  len: number,
  lineIndex: number,
  allowFrontMatterOpen: boolean,
  output: Token[],
): BlockState {
  const [ws, columns] = leadingWhitespace(line, len);
  if (ws === len) return BlockStates.indentedCode;
  if (columns >= 4) {
    output.push(token(lineStart, len, 'codeText', 'content'));
    return BlockStates.indentedCode;
  }
  return normal(line, lineStart, len, lineIndex, allowFrontMatterOpen, false, output);
}

function inFrontMatter(line: string, lineStart: number, len: number, output: Token[]): BlockState {
  if (isFrontMatterFence(line, len)) {
    output.push(token(lineStart, len, 'frontMatterFence', 'marker'));
    return BlockStates.normal;
  }
  if (len > 0) output.push(token(lineStart, len, 'frontMatterText', 'content'));
  return BlockStates.frontMatter;
}

function inFencedCode(
  line: string,
  lineStart: number,
  len: number,
  entering: BlockState,
  output: Token[],
): BlockState {
  if (isClosingFence(line, len, entering.fenceChar, entering.fenceLength)) {
    output.push(token(lineStart, len, 'codeFence', 'marker'));
    return BlockStates.normal;
  }
  if (len > 0) output.push(token(lineStart, len, 'codeText', 'content'));
  return entering; // an unterminated fence extends to the end of the file (PRD §10.2)
}

function normal(
  line: string,
  lineStart: number,
  len: number,
  lineIndex: number,
  allowFrontMatterOpen: boolean,
  afterBlank: boolean,
  output: Token[],
): BlockState {
  // `indent` is the char count of the leading spaces and tabs (for marker positions); `indentCols` its
  // width in columns (for the CommonMark <=3 / >=4 rules).
  const [indent, indentCols] = leadingWhitespace(line, len);

  // Front matter opens only as the first line, and only when a closing fence exists (decided by the model).
  if (lineIndex === 0 && allowFrontMatterOpen && isFrontMatterFence(line, len)) {
    output.push(token(lineStart, len, 'frontMatterFence', 'marker'));
    return BlockStates.frontMatter;
  }

  // An indented code block opens only at a fresh block position: it cannot interrupt a paragraph.
  if (afterBlank && indentCols >= 4 && len > indent) {
    output.push(token(lineStart, len, 'codeText', 'content'));
    return BlockStates.indentedCode;
  }

  if (indentCols <= 3) {
    const fence = tryFenceOpen(line, indent, len);
    if (fence) {
      output.push(token(lineStart, len, 'codeFence', 'marker', fence.language));
      return BlockStates.fenced(fence.char, fence.length, fence.language);
    }

    if (isHorizontalRule(line, indent, len)) {
      output.push(token(lineStart, len, 'horizontalRule', 'marker'));
      return BlockStates.normal;
    }

    const heading = tryHeading(line, indent, len);
    if (heading) {
      output.push(
        token(
          lineStart + indent,
          heading.afterMarker - indent,
          'headingMarker',
          'marker',
          String(heading.level),
        ),
      );
      // An optional closing sequence (`# Done #`) is markup too: the same marker type, so the editor
      // conceals it with the opening run and the export leaves it out.
      const close = closingSequenceStart(line, heading.afterMarker, len);
      tokenizeInline(line, lineStart, heading.afterMarker, close, output);
      if (close < len) {
        output.push(token(lineStart + close, len - close, 'headingMarker', 'marker', String(heading.level)));
      }
      return BlockStates.normal;
    }

    // A blockquote's marker spans the whole `>` prefix and carries its depth (the number of `>`) in meta.
    if (isQuote(line, indent, len)) {
      let p = indent;
      let depth = 0;
      while (p < len) {
        const c = line.charAt(p);
        if (c === '>') depth++;
        else if (c !== ' ' && c !== '\t') break;
        p++;
      }
      output.push(token(lineStart + indent, p - indent, 'blockquoteMarker', 'marker', String(depth)));
      tokenizeInline(line, lineStart, p, len, output, true);
      return BlockStates.normal;
    }
  }

  const markerEnd = tryListMarker(line, indent, len);
  if (markerEnd > 0) {
    output.push(token(lineStart + indent, markerEnd - indent, 'listMarker', 'marker'));
    let contentStart = markerEnd;
    const task = tryTaskMarker(line, contentStart, len);
    if (task) {
      // The marker carries the state it spells, so consumers read it off the token.
      output.push(
        token(
          lineStart + contentStart,
          task.end - contentStart,
          'taskMarker',
          'marker',
          taskMark(task.state),
        ),
      );
      contentStart = task.end;
    }
    tokenizeInline(line, lineStart, contentStart, len, output, true);
    return BlockStates.normal;
  }

  // A plain paragraph line. A blank line (empty or whitespace only) marks the next line as a fresh block
  // position, where an indented code block may open.
  tokenizeInline(line, lineStart, 0, len, output, true);
  return BlockStates.normalAfter(indent === len);
}

// ----- setext headings (driven from the model, which owns the one-line lookahead) -----

/**
 * The setext level `line` underlines (1 for `=`, 2 for `-`), or 0 when it is not an underline: a run of one
 * character and nothing else, indented at most three columns, with optional trailing whitespace.
 */
export function setextUnderlineLevel(line: string): number {
  const len = contentLength(line);
  const [indent, indentCols] = leadingWhitespace(line, len);
  if (indentCols > 3 || indent >= len) return 0;
  const c = line.charAt(indent);
  if (c !== '=' && c !== '-') return 0;
  let p = indent;
  while (p < len && line.charAt(p) === c) p++;
  while (p < len && (line.charAt(p) === ' ' || line.charAt(p) === '\t')) p++;
  if (p !== len) return 0;
  return c === '=' ? 1 : 2;
}

/**
 * Whether `line` can be the text of a setext heading: an ordinary paragraph line. Anything that opens a block
 * of its own (heading, rule, quote, list item, fence, front matter, indented code) takes no underline.
 * `isFrontMatterOpen` is true for line 0 of a document whose front matter opens there.
 */
export function isSetextTextLine(line: string, isFrontMatterOpen: boolean): boolean {
  if (isFrontMatterOpen) return false;
  const len = contentLength(line);
  const [indent, indentCols] = leadingWhitespace(line, len);
  if (indent === len || indentCols > 3) return false;
  return (
    !isHorizontalRule(line, indent, len) &&
    !tryHeading(line, indent, len) &&
    !isQuote(line, indent, len) &&
    tryListMarker(line, indent, len) === 0 &&
    !tryFenceOpen(line, indent, len) &&
    !isFrontMatterFence(line, len)
  );
}

/** Inline-tokenize a whole line's content (a setext heading's text has no line prefix). */
export function tokenizeLineInline(line: string, lineStart: number, output: Token[]): void {
  tokenizeInline(line, lineStart, 0, contentLength(line), output);
}

/** True when the whole line is a front-matter delimiter (`---`, `...` or `+++`). */
export function isFrontMatterFenceLine(line: string): boolean {
  return isFrontMatterFence(line, contentLength(line));
}

// ----- recognizers -----

/**
 * The count of leading spaces and tabs, and their width in columns (a tab advances to the next four-column
 * stop, as in CommonMark).
 */
function leadingWhitespace(line: string, len: number): [chars: number, columns: number] {
  let n = 0;
  let columns = 0;
  while (n < len) {
    const c = line.charAt(n);
    if (c === '\t') columns += 4 - (columns % 4);
    else if (c === ' ') columns++;
    else break;
    n++;
  }
  return [n, columns];
}

function isFrontMatterFence(line: string, len: number): boolean {
  const t = line.slice(0, len).trimEnd();
  return t === '---' || t === '...' || t === '+++';
}

interface FenceOpen {
  readonly char: string;
  readonly length: number;
  readonly language: string | null;
}

function tryFenceOpen(line: string, indent: number, len: number): FenceOpen | null {
  if (indent >= len) return null;
  const c = line.charAt(indent);
  if (c !== '`' && c !== '~') return null;
  let run = 0;
  while (indent + run < len && line.charAt(indent + run) === c) run++;
  if (run < 3) return null;
  const info = line.slice(indent + run, len).trim();
  // A backtick fence's info string may not contain a backtick (CommonMark).
  if (c === '`' && info.includes('`')) return null;
  return { char: c, length: run, language: info.length === 0 ? null : info };
}

function isClosingFence(line: string, len: number, fenceChar: string, fenceLength: number): boolean {
  const [indent, columns] = leadingWhitespace(line, len);
  if (columns > 3) return false;
  let run = 0;
  while (indent + run < len && line.charAt(indent + run) === fenceChar) run++;
  if (run < fenceLength) return false;
  return line.slice(indent + run, len).trim().length === 0;
}

function isHorizontalRule(line: string, indent: number, len: number): boolean {
  if (indent >= len) return false;
  const c = line.charAt(indent);
  if (c !== '-' && c !== '*' && c !== '_') return false;
  let count = 0;
  for (let k = indent; k < len; k++) {
    const ch = line.charAt(k);
    if (ch === c) count++;
    else if (ch !== ' ') return false;
  }
  return count >= 3;
}

function tryHeading(
  line: string,
  indent: number,
  len: number,
): { level: number; afterMarker: number } | null {
  let k = indent;
  while (k < len && line.charAt(k) === '#') k++;
  const level = k - indent;
  if (level < 1 || level > 6) return null;
  // The run must be followed by a space or the end of the line.
  if (k < len && line.charAt(k) !== ' ') return null;
  if (k < len) k++;
  return { level, afterMarker: k };
}

/**
 * Where an ATX heading's closing sequence starts (the blanks before a trailing run of `#`, through the end of
 * the line), or `len` when it has none. The run must follow a blank or the opening marker: `# C#` and
 * `# D \#` keep their `#` (CommonMark §4.2).
 */
function closingSequenceStart(line: string, afterMarker: number, len: number): number {
  const isBlank = (c: string): boolean => c === ' ' || c === '\t';
  let end = len;
  while (end > afterMarker && isBlank(line.charAt(end - 1))) end--;
  let hashes = end;
  while (hashes > afterMarker && line.charAt(hashes - 1) === '#') hashes--;
  if (hashes === end) return len;
  if (hashes === afterMarker) return afterMarker;
  let blanks = hashes;
  while (blanks > afterMarker && isBlank(line.charAt(blanks - 1))) blanks--;
  return blanks < hashes ? blanks : len;
}

function isQuote(line: string, indent: number, len: number): boolean {
  return indent < len && line.charAt(indent) === '>';
}

/** The column just past a list marker starting at `indent`, or 0 when there is none. */
function tryListMarker(line: string, indent: number, len: number): number {
  if (indent >= len) return 0;
  const c = line.charAt(indent);
  if (c === '-' || c === '*' || c === '+') {
    return indent + 1 < len && line.charAt(indent + 1) === ' ' ? indent + 2 : 0;
  }
  // Ordered: one to nine digits, then '.' or ')', then a space.
  let k = indent;
  while (k < len && k - indent < 9 && isAsciiDigit(line.charAt(k))) k++;
  if (k === indent || k >= len) return 0;
  const d = line.charAt(k);
  return (d === '.' || d === ')') && k + 1 < len && line.charAt(k + 1) === ' ' ? k + 2 : 0;
}

/** A task box (`[ ]`, `[/]`, `[x]`) at `start`, followed by a space or the end of the line. */
function tryTaskMarker(line: string, start: number, len: number): { end: number; state: TaskState } | null {
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
