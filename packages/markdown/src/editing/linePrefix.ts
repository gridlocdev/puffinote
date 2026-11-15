import { isSetextTextLine } from '../markdown/lineTokenizer.ts';
import type { TaskState } from '../markdown/taskState.ts';
import { scanListMarker, scanTaskBox } from '../markdown/listMarker.ts';

/** The kind of list a line's marker belongs to. */
export type ListKind = 'none' | 'unordered' | 'ordered';

/**
 * The parsed leading block structure of one line: indentation, an optional blockquote prefix, and an optional
 * list marker with a task box. The editing assists (Enter continuation and exit, list renumbering; PRD §10.1)
 * are decided from this. It mirrors the tokenizer's recognizers so the assists agree with what is styled.
 */
export interface LinePrefix {
  /** Char count of the leading spaces and tabs. */
  readonly indent: number;
  /** The leading whitespace verbatim, so a continued line reproduces the same tabs and spaces. */
  readonly indentText: string;
  /** The blockquote prefix verbatim (`"> > "`), or ''. */
  readonly quote: string;
  readonly list: ListKind;
  /** `-`, `*` or `+` for an unordered item; '' otherwise. */
  readonly bullet: string;
  readonly number: number;
  /** `.` or `)` for an ordered item; '' otherwise. */
  readonly delim: string;
  readonly digitCount: number;
  readonly hasTask: boolean;
  readonly task: TaskState;
  /** The column where the list marker starts (just past indent and quote). */
  readonly markerStart: number;
  /** The column where the item's content starts: past indent, quote, marker and box. */
  readonly length: number;
  readonly hasList: boolean;
  readonly hasQuote: boolean;
  /** True when the box is done: the one state that takes an item off the active board. */
  readonly taskChecked: boolean;
  /** The column just past the list marker, where a task box sits; `markerStart` when there is no marker. */
  readonly afterMarker: number;
  /** The column of the character between the brackets. Only meaningful when `hasTask`. */
  readonly taskMarkColumn: number;
}

/** Parse the leading structure of a single line (a trailing CR is tolerated). */
export function parseLinePrefix(line: string): LinePrefix {
  let len = line.length;
  if (len > 0 && line.charAt(len - 1) === '\r') len--;

  let i = 0;
  while (i < len && (line.charAt(i) === ' ' || line.charAt(i) === '\t')) i++;
  const indent = i;
  const indentText = line.slice(0, i);

  // Blockquote: one or more '>' each optionally followed by spaces or tabs.
  const quoteStart = i;
  while (i < len && line.charAt(i) === '>') {
    i++;
    while (i < len && (line.charAt(i) === ' ' || line.charAt(i) === '\t')) i++;
  }
  const quote = line.slice(quoteStart, i);
  const markerStart = i;

  let list: ListKind = 'none';
  let bullet = '';
  let number = 0;
  let digitCount = 0;
  let delim = '';
  let afterMarker = i;

  const marker = scanListMarker(line, i, len);
  if (marker?.kind === 'unordered') {
    list = 'unordered';
    bullet = marker.bullet;
    afterMarker = marker.end;
  } else if (marker?.kind === 'ordered') {
    list = 'ordered';
    digitCount = marker.digitCount;
    number = marker.number;
    delim = marker.delim;
    afterMarker = marker.end;
  }

  let hasTask = false;
  let task: TaskState = 'notStarted';
  let prefixEnd = list === 'none' ? markerStart : afterMarker;
  const box = list === 'none' ? null : scanTaskBox(line, afterMarker, len);
  if (box) {
    hasTask = true;
    task = box.state;
    prefixEnd = box.end;
  }

  const markerEnd =
    list === 'unordered' ? markerStart + 2 : list === 'ordered' ? markerStart + digitCount + 2 : markerStart;

  return {
    indent,
    indentText,
    quote,
    list,
    bullet,
    number,
    delim,
    digitCount,
    hasTask,
    task,
    markerStart,
    length: prefixEnd,
    hasList: list !== 'none',
    hasQuote: quote.length > 0,
    taskChecked: hasTask && task === 'done',
    afterMarker: markerEnd,
    taskMarkColumn: markerEnd + 1,
  };
}

/**
 * Whether `line` is ordinary prose that lazily continues the block above it (the second source line of a
 * hard-wrapped list item) rather than opening a block of its own. Defers to the tokenizer's paragraph test.
 */
export function isLazyContinuation(line: string): boolean {
  return isSetextTextLine(line, false);
}

/**
 * The text a paste should insert, given the line it lands in. Pasting `"- [ ] Buy milk"` into the content of
 * a line that already is a task item would spell the marker twice, so the pasted first line drops its own
 * indent, quote, marker and box. Later lines keep theirs. Only when the target carries a box and the caret is
 * in its content: pasting before the marker asks for a new item above.
 */
export function stripDuplicateTaskMarker(pasted: string, targetLine: string, caretColumn: number): string {
  const target = parseLinePrefix(targetLine);
  if (!target.hasTask || caretColumn < target.length || pasted.length === 0) return pasted;
  const breakAt = pasted.indexOf('\n');
  const incoming = parseLinePrefix(breakAt < 0 ? pasted : pasted.slice(0, breakAt));
  return incoming.hasTask ? pasted.slice(incoming.length) : pasted;
}

/** The prefix that begins the next sibling item or line, after a newline (PRD §10.1). */
export function continuationPrefix(p: LinePrefix): string {
  let prefix = p.indentText + p.quote;
  if (p.list === 'unordered') prefix += `${p.bullet} `;
  else if (p.list === 'ordered') prefix += `${p.number + 1}${p.delim} `;
  if (p.hasTask) prefix += '[ ] '; // a continued task starts not started
  return prefix;
}

/** What an Enter keypress should do on the caret's line. */
export type EnterKind = 'plain' | 'continue' | 'exit' | 'above';

/**
 * The decision for an Enter keypress. `text` is what to insert at the caret. For `exit`, the columns
 * `[exitStart, exitStart + exitLength)` of the line are deleted and nothing is inserted, so an empty item
 * clears back to its outer level. For `above`, `text` (an empty item and its newline) goes in at the start of
 * the line, so the item moves down whole.
 */
export interface EnterResult {
  readonly kind: EnterKind;
  readonly text: string;
  readonly exitStart: number;
  readonly exitLength: number;
}

/**
 * Decide what Enter does on `line` with the caret at `caretColumn`: continue a list or quote, exit an empty
 * item, carry a plain line's indent down, or insert a plain newline.
 */
export function computeEnter(line: string, caretColumn: number): EnterResult {
  let contentLen = line.length;
  if (contentLen > 0 && line.charAt(contentLen - 1) === '\r') contentLen--;
  const p = parseLinePrefix(line);

  if (!p.hasList && !p.hasQuote) {
    // Carry down the leading whitespace that precedes the caret, verbatim.
    const carried = p.indentText.slice(0, Math.max(0, Math.min(p.indentText.length, caretColumn)));
    return carried.length > 0 ? enter('continue', '\n' + carried) : enter('plain', '\n');
  }

  // An empty item (only its prefix) with the caret at or past the marker: Enter exits the block.
  const isEmpty = contentLen <= p.length || line.slice(p.length, contentLen).trim().length === 0;
  if (isEmpty && caretColumn >= p.markerStart) {
    const exitStart = p.hasList ? p.markerStart : p.indent;
    return { kind: 'exit', text: '', exitStart, exitLength: contentLen - exitStart };
  }

  // The caret is inside the marker prefix, or at the start of an item's text: continuing would splice another
  // marker into the line or leave the item's marker behind on an empty line, so open an empty item above and
  // push the whole item down, keeping its marker and tick.
  if (caretColumn < p.length || (p.hasList && caretColumn === p.length)) {
    return enter('above', continuationPrefix(p) + '\n');
  }

  return enter('continue', '\n' + continuationPrefix(p));
}

function enter(kind: EnterKind, text: string): EnterResult {
  return { kind, text, exitStart: 0, exitLength: 0 };
}
