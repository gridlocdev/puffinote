import { BlockStates, listBase, type BlockState } from './blockState.ts';
import { contentLength } from './chars.ts';
import { tokenizeInline } from './inlineTokenizer.ts';
import { taskMark } from './taskState.ts';
import { token, type Token } from './token.ts';
import { scanListMarker, scanTaskBox } from './listMarker.ts';

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
      return inIndentedCode(line, lineStart, len, lineIndex, allowFrontMatterOpen, entering, output);
    default:
      return normal(
        line,
        lineStart,
        len,
        lineIndex,
        allowFrontMatterOpen,
        entering.afterBlank,
        entering.listColumns,
        output,
      );
  }
}

/**
 * Inside an indented code block a line indented four columns or more (past the content column of the list
 * item it sits in), or any blank line, stays in the block. The first non-blank line dedented below that ends
 * it and is classified normally (CommonMark §4.4).
 */
function inIndentedCode(
  line: string,
  lineStart: number,
  len: number,
  lineIndex: number,
  allowFrontMatterOpen: boolean,
  entering: BlockState,
  output: Token[],
): BlockState {
  const [ws, columns] = leadingWhitespace(line, len);
  if (ws === len) return entering;
  if (columns >= listBase(entering) + 4) {
    output.push(token(lineStart, len, 'codeText', 'content'));
    return entering;
  }
  return normal(line, lineStart, len, lineIndex, allowFrontMatterOpen, false, entering.listColumns, output);
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
  const base = listBase(entering);
  if (isClosingFence(line, len, entering.fenceChar, entering.fenceLength, base)) {
    output.push(token(lineStart, len, 'codeFence', 'marker'));
    // A closing fence left of the item the fence opened in closes that item too.
    const columns = leadingWhitespace(line, len)[1];
    return BlockStates.normalAfter(false, popListsLeftOf(entering.listColumns, columns));
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
  enteringLists: readonly number[],
  output: Token[],
): BlockState {
  // `indent` is the char count of the leading spaces and tabs (for marker positions); `indentCols` its
  // width in columns.
  const [indent, indentCols] = leadingWhitespace(line, len);
  const blank = indent === len;

  // The list items this line sits in. A non-blank line left of an item's content column closes that item,
  // unless it is a lazy continuation: paragraph text straight after a non-blank line (CommonMark §5.2).
  let lists = enteringLists;
  if (!blank && lists.length > 0 && indentCols < lists[lists.length - 1]!) {
    const popped = popListsLeftOf(lists, indentCols);
    const rel = indentCols - (popped.length === 0 ? 0 : popped[popped.length - 1]!);
    if (afterBlank || opensBlock(line, indent, len, rel)) lists = popped;
  }
  // The CommonMark <=3 / >=4 column rules count from the content column of the innermost item.
  const relCols = indentCols - (lists.length === 0 ? 0 : lists[lists.length - 1]!);

  // Front matter opens only as the first line, and only when a closing fence exists (decided by the model).
  if (lineIndex === 0 && allowFrontMatterOpen && isFrontMatterFence(line, len)) {
    output.push(token(lineStart, len, 'frontMatterFence', 'marker'));
    return BlockStates.frontMatter;
  }

  // An indented code block opens only at a fresh block position: it cannot interrupt a paragraph.
  if (afterBlank && relCols >= 4 && !blank) {
    output.push(token(lineStart, len, 'codeText', 'content'));
    return BlockStates.indentedCodeIn(lists);
  }

  if (relCols <= 3) {
    const fence = tryFenceOpen(line, indent, len);
    if (fence) {
      output.push(token(lineStart, len, 'codeFence', 'marker', fence.language));
      return BlockStates.fenced(fence.char, fence.length, fence.language, lists);
    }

    if (isHorizontalRule(line, indent, len)) {
      output.push(token(lineStart, len, 'horizontalRule', 'marker'));
      return BlockStates.normalAfter(false, lists);
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
      return BlockStates.normalAfter(false, lists);
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
      // A list item or task inside the quote (`> - [x] done`) gets its markers as it would outside one.
      const quotedMarkerEnd = tryListMarker(line, p, len);
      const contentStart =
        quotedMarkerEnd > 0 ? listItemMarkers(line, lineStart, p, quotedMarkerEnd, len, output) : p;
      tokenizeInline(line, lineStart, contentStart, len, output, true);
      return BlockStates.normalAfter(false, lists);
    }
  }

  const markerEnd = tryListMarker(line, indent, len);
  if (markerEnd > 0) {
    const contentStart = listItemMarkers(line, lineStart, indent, markerEnd, len, output);
    tokenizeInline(line, lineStart, contentStart, len, output, true);
    // The item's content column (just past the marker) is where its nested lines count their indent from.
    return BlockStates.normalAfter(false, [...lists, indentCols + (markerEnd - indent)]);
  }

  // A plain paragraph line. A blank line (empty or whitespace only) marks the next line as a fresh block
  // position, where an indented code block may open; it closes no list item.
  tokenizeInline(line, lineStart, 0, len, output, true);
  return BlockStates.normalAfter(blank, lists);
}

/**
 * Emit a list item's marker (from `at` to `markerEnd`) and its task box, if any; returns where the item's text
 * starts.
 */
function listItemMarkers(
  line: string,
  lineStart: number,
  at: number,
  markerEnd: number,
  len: number,
  output: Token[],
): number {
  output.push(token(lineStart + at, markerEnd - at, 'listMarker', 'marker'));
  const task = scanTaskBox(line, markerEnd, len);
  if (!task) return markerEnd;
  // The marker carries the state it spells, so consumers read it off the token.
  output.push(
    token(lineStart + markerEnd, task.end - markerEnd, 'taskMarker', 'marker', taskMark(task.state)),
  );
  return task.end;
}

/** `lists` without the items whose content column is right of `columns`. */
function popListsLeftOf(lists: readonly number[], columns: number): readonly number[] {
  let n = lists.length;
  while (n > 0 && columns < lists[n - 1]!) n--;
  return n === lists.length ? lists : lists.slice(0, n);
}

/** Whether a line opens a block of its own, at `rel` columns inside its container, rather than being prose. */
function opensBlock(line: string, indent: number, len: number, rel: number): boolean {
  if (tryListMarker(line, indent, len) > 0) return true;
  return (
    rel <= 3 &&
    (!!tryFenceOpen(line, indent, len) ||
      isHorizontalRule(line, indent, len) ||
      !!tryHeading(line, indent, len) ||
      isQuote(line, indent, len))
  );
}

// ----- setext headings (driven from the model, which owns the one-line lookahead) -----

/**
 * The setext level `line` underlines (1 for `=`, 2 for `-`), or 0 when it is not an underline: a run of one
 * character and nothing else, indented at most three columns, with optional trailing whitespace. A lone `-`
 * is not an underline: it is the first keystroke of a list item, and taking it for one would flash the
 * paragraph above into a heading while the user types `- ` (PRD §10.2, §11.3).
 */
export function setextUnderlineLevel(line: string): number {
  const len = contentLength(line);
  const [indent, indentCols] = leadingWhitespace(line, len);
  if (indentCols > 3 || indent >= len) return 0;
  const c = line.charAt(indent);
  if (c !== '=' && c !== '-') return 0;
  let p = indent;
  while (p < len && line.charAt(p) === c) p++;
  if (c === '-' && p - indent < 2) return 0;
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

function isClosingFence(
  line: string,
  len: number,
  fenceChar: string,
  fenceLength: number,
  base: number,
): boolean {
  const [indent, columns] = leadingWhitespace(line, len);
  if (columns - base > 3) return false;
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
  return scanListMarker(line, indent, len)?.end ?? 0;
}
