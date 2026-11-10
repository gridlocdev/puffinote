import {
  computeEnter,
  parseLinePrefix,
  renumberOrderedList,
  taskMark,
  toggledTaskState,
  type TaskState,
} from '@puffinote/markdown';
import {
  isCollapsed,
  selectionEnd,
  selectionLength,
  selectionStart,
  type Selection,
  type TextDocument,
} from '@puffinote/text';
import type { ViewMode } from './reveal.ts';

/**
 * One undoable change: replace `[start, start + length)` with `text`, then select `[anchor, caret]` in the
 * changed document. Every editing command is a pure function from the document and selection to an `Edit`, so
 * the editor applies it in one step and tests can run it without a DOM.
 */
export interface Edit {
  readonly start: number;
  readonly length: number;
  readonly text: string;
  readonly anchor: number;
  readonly caret: number;
}

/** Where the caret is, which changes what Enter and Tab mean. */
export type EditContext = 'text' | 'code' | 'table';

export const defaultIndent = '    ';

/** Apply `edit` to `doc` (for tests and for chaining). */
export function applyEditTo(doc: TextDocument, edit: Edit): TextDocument {
  return doc.replace(edit.start, edit.length, edit.text);
}

function replaceSelection(sel: Selection, text: string, caretInText = text.length): Edit {
  const start = selectionStart(sel);
  const caret = start + caretInText;
  return { start, length: selectionLength(sel), text, anchor: caret, caret };
}

function lineOf(doc: TextDocument, offset: number): number {
  return doc.getPosition(offset).line;
}

function contentOf(line: string): string {
  return line.endsWith('\r') ? line.slice(0, -1) : line;
}

// ----- Enter -----

/**
 * Enter (PRD §10.1): continue a list or quote, exit an empty item, keep a code block's indent, and otherwise
 * start a new paragraph, which is a blank line (`\n\n`) in the formatted views and one `\n` in raw view.
 */
export function enterEdit(doc: TextDocument, sel: Selection, mode: ViewMode, context: EditContext): Edit {
  const start = selectionStart(sel);
  const line = lineOf(doc, start);
  const lineStart = doc.getLineStart(line);
  const lineText = contentOf(doc.getLineText(line));
  const col = start - lineStart;

  if (mode === 'raw') return replaceSelection(sel, '\n');
  if (context === 'code') {
    const indent = /^[ \t]*/.exec(lineText)![0].slice(0, col);
    return replaceSelection(sel, '\n' + indent);
  }
  if (context === 'table') return replaceSelection(sel, '\n');

  const r = computeEnter(lineText, col);
  if (r.kind === 'exit' && isCollapsed(sel)) {
    const at = lineStart + r.exitStart;
    return { start: at, length: r.exitLength, text: '', anchor: at, caret: at };
  }
  if (r.kind === 'continue') return withRenumber(doc, replaceSelection(sel, r.text));
  if (r.kind === 'above' && isCollapsed(sel)) {
    // One new line above, with the caret at the start of the text of the item it pushed down: an empty item
    // inside a list, or a blank line (kept in its quote) when the item is the first of its list.
    const p = parseLinePrefix(lineText);
    const text = p.hasList && firstListItem(doc, line) ? p.quote.trimEnd() + '\n' : r.text;
    const caret = lineStart + text.length + p.length;
    return withRenumber(doc, { start: lineStart, length: 0, text, anchor: caret, caret });
  }
  const blank = lineText.trim().length === 0;
  return replaceSelection(sel, blank ? '\n' : '\n\n');
}

/**
 * Whether list item `line` is the first item of its list: no item at its level comes before it, past the lines
 * nested under earlier items. The first child of a nested list counts as first.
 */
function firstListItem(doc: TextDocument, line: number): boolean {
  const p = parseLinePrefix(doc.getLineText(line));
  for (let i = line - 1; i >= 0; i--) {
    const text = doc.getLineText(i);
    const q = parseLinePrefix(text);
    if (q.quote !== p.quote || text.slice(q.markerStart).trim().length === 0) return true;
    if (q.markerStart < p.markerStart) return true;
    if (q.markerStart === p.markerStart) return !(q.hasList && q.list === p.list);
  }
  return true;
}

/**
 * Take the picture written at `[start, end)` out of the note (Delete Picture, PRD §10.6). A picture alone on
 * its line takes the line with it; the caret lands where the picture was.
 */
export function removeRangeEdit(doc: TextDocument, start: number, end: number): Edit {
  const line = lineOf(doc, start);
  const lineStart = doc.getLineStart(line);
  const text = contentOf(doc.getLineText(line));
  const rest = text.slice(0, start - lineStart) + text.slice(end - lineStart);
  if (rest.trim().length > 0 || doc.lineCount === 1) {
    return { start, length: end - start, text: '', anchor: start, caret: start };
  }
  // The whole line goes: with the line break after it, or before it on the last line.
  const lineEnd = lineStart + text.length;
  const last = line === doc.lineCount - 1;
  const from = last ? lineStart - (doc.getText(lineStart - 2, 2) === '\r\n' ? 2 : 1) : lineStart;
  const to = last ? lineEnd : doc.getLineStart(line + 1);
  const caret = last ? from : lineStart;
  return { start: from, length: to - from, text: '', anchor: caret, caret };
}

/** Shift+Enter: a single line break, never a new paragraph. */
export function softBreakEdit(sel: Selection): Edit {
  return replaceSelection(sel, '\n');
}

// ----- Line transforms -----

interface Splice {
  readonly at: number;
  readonly remove: number;
  readonly insert: string;
}

/**
 * Apply one splice per line over `[firstLine, lastLine]`, as one edit that maps the selection's ends through
 * the splices, so the caret stays with the text it was next to.
 */
function spliceLines(
  doc: TextDocument,
  sel: Selection,
  firstLine: number,
  lastLine: number,
  splice: (line: string, index: number) => Splice | null,
): Edit | null {
  const regionStart = doc.getLineStart(firstLine);
  const regionEnd = doc.getLineEnd(lastLine);
  const pieces: string[] = [];
  const shifts: { lineStart: number; s: Splice }[] = [];
  let changed = false;
  for (let i = firstLine; i <= lastLine; i++) {
    const text = doc.getLineText(i);
    const s = splice(text, i);
    if (s && (s.remove > 0 || s.insert.length > 0)) {
      changed = true;
      pieces.push(text.slice(0, s.at) + s.insert + text.slice(s.at + s.remove));
      shifts.push({ lineStart: doc.getLineStart(i), s });
    } else {
      pieces.push(text);
    }
  }
  if (!changed) return null;

  const map = (offset: number): number => {
    let delta = 0;
    for (const { lineStart, s } of shifts) {
      const at = lineStart + s.at;
      if (offset < at) break;
      if (offset >= at + s.remove) delta += s.insert.length - s.remove;
      else return at + delta + s.insert.length;
    }
    return offset + delta;
  };
  return {
    start: regionStart,
    length: regionEnd - regionStart,
    text: pieces.join('\n'),
    anchor: map(sel.anchor),
    caret: map(sel.caret),
  };
}

function selectedLines(doc: TextDocument, sel: Selection): [number, number] {
  const a = lineOf(doc, selectionStart(sel));
  let b = lineOf(doc, selectionEnd(sel));
  // A selection ending at the very start of a line doesn't include that line.
  if (b > a && selectionEnd(sel) === doc.getLineStart(b)) b--;
  return [a, b];
}

// ----- Tab / Shift+Tab -----

/**
 * Tab: indent list items or a multi-line selection; elsewhere insert one indent at the caret (PRD §10.1).
 * Blank lines in a multi-line selection stay blank, so a selection of only blank lines changes nothing (null).
 */
export function indentEdit(doc: TextDocument, sel: Selection, indent = defaultIndent): Edit | null {
  const [a, b] = selectedLines(doc, sel);
  const isList = parseLinePrefix(doc.getLineText(a)).hasList;
  if (a === b && !isList) return replaceSelection(sel, indent);
  const edit = spliceLines(doc, sel, a, b, (text) =>
    contentOf(text).length === 0 && a !== b ? null : { at: 0, remove: 0, insert: indent },
  );
  return edit ? withRenumber(doc, edit, [a, b + 1]) : null;
}

/** Shift+Tab: remove one indent step (a tab, or up to `indent.length` spaces) from each selected line. */
export function outdentEdit(doc: TextDocument, sel: Selection, indent = defaultIndent): Edit | null {
  const [a, b] = selectedLines(doc, sel);
  const edit = spliceLines(doc, sel, a, b, (text) => {
    if (text.startsWith('\t')) return { at: 0, remove: 1, insert: '' };
    let n = 0;
    while (n < indent.length && text.charAt(n) === ' ') n++;
    return n > 0 ? { at: 0, remove: n, insert: '' } : null;
  });
  return edit ? withRenumber(doc, edit, [a, b + 1]) : null;
}

// ----- Ordered-list renumbering -----

/**
 * Fold ordered-list renumbering into `edit` (PRD §10.1), so typing an item or changing its depth leaves the
 * numbers right in the same undo step. `lines` are lines of the edited document whose lists to check; by
 * default the caret's line and the line after it.
 */
export function withRenumber(doc: TextDocument, edit: Edit, lines?: readonly number[]): Edit {
  const after = applyEditTo(doc, edit);
  const all: string[] = [];
  for (let i = 0; i < after.lineCount; i++) all.push(after.getLineText(i));
  const caretLine = after.getPosition(edit.caret).line;
  const targets = lines ?? [caretLine, caretLine + 1];

  let changed = false;
  for (const target of targets) {
    if (target < 0 || target >= all.length) continue;
    const start = listStart(all, target);
    if (start === null) continue;
    const r = renumberOrderedList(all, start);
    if (!r) continue;
    all.splice(r.startLine, r.lines.length, ...r.lines);
    changed = true;
  }
  if (!changed) return edit;

  const oldText = doc.getText();
  const newText = all.join('\n');
  const mapEnd = (offset: number): number => {
    // Renumbering can widen a number (9 → 10): shift an offset by the length change of its line before it.
    const pos = after.getPosition(offset);
    const delta = all[pos.line]!.length - after.getLineText(pos.line).length;
    const p = parseLinePrefix(after.getLineText(pos.line));
    let base = 0;
    for (let i = 0; i < pos.line; i++) base += all[i]!.length + 1;
    return base + (pos.column > p.markerStart ? pos.column + delta : pos.column);
  };
  let p = 0;
  const max = Math.min(oldText.length, newText.length);
  while (p < max && oldText.charCodeAt(p) === newText.charCodeAt(p)) p++;
  let s = 0;
  while (
    s < max - p &&
    oldText.charCodeAt(oldText.length - 1 - s) === newText.charCodeAt(newText.length - 1 - s)
  ) {
    s++;
  }
  return {
    start: p,
    length: oldText.length - p - s,
    text: newText.slice(p, newText.length - s),
    anchor: mapEnd(edit.anchor),
    caret: mapEnd(edit.caret),
  };
}

/** The first line of the ordered list containing `line`, or null when it is not an ordered item. */
function listStart(lines: readonly string[], line: number): number | null {
  const p = parseLinePrefix(lines[line]!);
  if (p.list !== 'ordered') return null;
  let i = line;
  while (i > 0) {
    const q = parseLinePrefix(lines[i - 1]!);
    if (q.list !== 'ordered' || q.indent !== p.indent || q.quote !== p.quote) break;
    i--;
  }
  return i;
}

// ----- Auto-pairing -----

const closers: Record<string, string> = { '*': '*', _: '_', '`': '`', '[': ']', '(': ')' };
const symmetric = new Set(['*', '_', '`']);

function isWordChar(c: string): boolean {
  return c.length > 0 && /[\p{L}\p{Nd}_]/u.test(c);
}

/** Only indentation and quote markers before the caret: a `*`, `_` or `` ` `` here starts a list, rule or fence. */
function atLinePrefix(before: string): boolean {
  return /^[ \t]*(?:>[ \t]*)*$/.test(before);
}

/**
 * Typing `ch` (PRD §10.1): wrap a selection in a pair, insert an empty pair at a word start, or step over a
 * closer that is already there. Returns null for an ordinary insert.
 *
 * `*`, `_` and `` ` `` don't pair where they are more often literal: at the start of a line (`* item`, `---`,
 * a fence), right after the same character (the second and third backtick of a fence), or after a letter or
 * digit. Inside an empty pair the same character makes a double pair (`*|*` + `*` is `**|**`, bold), and a
 * space typed right after a lone opener drops its closer (`2 *|*` + space is `2 * |`).
 */
export function typeCharEdit(doc: TextDocument, sel: Selection, ch: string): Edit | null {
  const close = closers[ch];
  if (!isCollapsed(sel)) {
    if (!close) return null;
    const start = selectionStart(sel);
    const inner = doc.getText(start, selectionLength(sel));
    if (inner.includes('\n') && !symmetric.has(ch)) return null;
    return {
      start,
      length: inner.length,
      text: ch + inner + close,
      anchor: start + 1,
      caret: start + 1 + inner.length,
    };
  }

  const at = sel.caret;
  const line = lineOf(doc, at);
  const lineStart = doc.getLineStart(line);
  const text = contentOf(doc.getLineText(line));
  const col = at - lineStart;
  const prev = text.charAt(col - 1);
  const next = text.charAt(col);

  // Step over a closer that's already there (`(x|)` + `)`, `**b*|*` + `*`), but not out of an empty symmetric
  // pair (`*|*` + `*` makes `**|**`, the start of bold). A `*` after a word is a closer, not an empty pair.
  const emptyPair = symmetric.has(ch) && prev === ch && !isWordChar(text.charAt(col - 2));
  if ((ch === ')' || ch === ']' || symmetric.has(ch)) && next === ch && !emptyPair) {
    return { start: at, length: 0, text: '', anchor: at + 1, caret: at + 1 };
  }
  // A space right after a lone auto-paired opener (`*|*`, not `**|**`): the opener was literal after all.
  if (
    ch === ' ' &&
    symmetric.has(prev) &&
    next === prev &&
    text.charAt(col - 2) !== prev &&
    text.charAt(col + 1) !== prev &&
    !isWordChar(text.charAt(col - 2)) &&
    !isWordChar(text.charAt(col + 1))
  ) {
    return { start: at, length: 1, text: ' ', anchor: at + 1, caret: at + 1 };
  }
  if (!close) return null;
  const nextFree = next === '' || /\s/.test(next) || ')]}*_`'.includes(next);
  if (!nextFree) return null;
  if (symmetric.has(ch)) {
    if (isWordChar(prev) || atLinePrefix(text.slice(0, col))) return null;
    if (prev === ch && next !== ch) return null; // the next char of a run: `**`, a fence
  }
  return { start: at, length: 0, text: ch + close, anchor: at + 1, caret: at + 1 };
}

/** Backspace inside an empty pair (`(|)`, `*|*`) removes both halves; null otherwise. */
export function backspacePairEdit(doc: TextDocument, sel: Selection): Edit | null {
  if (!isCollapsed(sel) || sel.caret === 0 || sel.caret >= doc.length) return null;
  const prev = doc.charAt(sel.caret - 1);
  const next = doc.charAt(sel.caret);
  if (closers[prev] !== next) return null;
  const at = sel.caret - 1;
  return { start: at, length: 2, text: '', anchor: at, caret: at };
}

// ----- Inline formatting -----

/** The word around `col` in `text` (letters, digits, `_`), or null when the caret isn't in or next to one. */
export function wordAt(text: string, col: number): { start: number; end: number } | null {
  let a = col;
  let b = col;
  while (a > 0 && isWordChar(text.charAt(a - 1))) a--;
  while (b < text.length && isWordChar(text.charAt(b))) b++;
  return a === b ? null : { start: a, end: b };
}

/**
 * ⌘B / ⌘I / ⌘E and friends: wrap the selection (or the word at the caret) in `marker`, or unwrap it when it is
 * already wrapped. With no word, insert an empty pair with the caret between.
 */
export function toggleInlineEdit(doc: TextDocument, sel: Selection, marker: string): Edit {
  const m = marker.length;
  let a = selectionStart(sel);
  let b = selectionEnd(sel);
  const collapsed = a === b;
  if (collapsed) {
    const line = lineOf(doc, a);
    const lineStart = doc.getLineStart(line);
    const w = wordAt(contentOf(doc.getLineText(line)), a - lineStart);
    if (!w) return { start: a, length: 0, text: marker + marker, anchor: a + m, caret: a + m };
    a = lineStart + w.start;
    b = lineStart + w.end;
  }
  const keep = (start: number, end: number): Pick<Edit, 'anchor' | 'caret'> =>
    collapsed
      ? { anchor: start + (sel.caret - a), caret: start + (sel.caret - a) }
      : { anchor: start, caret: end };

  // Unwrap markers just outside the range: **|word|**.
  const before = a >= m ? doc.getText(a - m, m) : '';
  const after = b + m <= doc.length ? doc.getText(b, m) : '';
  if (before === marker && after === marker && !extendsRun(doc, a - m, b + m, marker)) {
    const inner = doc.getText(a, b - a);
    return { start: a - m, length: b - a + 2 * m, text: inner, ...keep(a - m, b - m) };
  }
  // Unwrap markers just inside the range: |**word**|.
  const inner = doc.getText(a, b - a);
  if (inner.length >= 2 * m && inner.startsWith(marker) && inner.endsWith(marker)) {
    const stripped = inner.slice(m, inner.length - m);
    return { start: a, length: inner.length, text: stripped, anchor: a, caret: a + stripped.length };
  }
  return { start: a, length: b - a, text: marker + inner + marker, ...keep(a + m, b + m) };
}

/** True when the marker around a range is part of a longer run (`*` inside `**`), so it isn't ours to remove. */
function extendsRun(doc: TextDocument, openAt: number, closeEnd: number, marker: string): boolean {
  const c = marker.charAt(0);
  const left = openAt > 0 ? doc.charAt(openAt - 1) : '';
  const right = closeEnd < doc.length ? doc.charAt(closeEnd) : '';
  return marker.length === 1 && (c === '*' || c === '_') && left === c && right === c;
}

/** ⌘K: make the selection a link and put the caret where the URL goes; a selected URL becomes the target. */
export function linkEdit(doc: TextDocument, sel: Selection): Edit {
  const start = selectionStart(sel);
  const text = doc.getText(start, selectionLength(sel));
  if (/^(https?:\/\/|mailto:)\S+$/.test(text)) {
    return { start, length: text.length, text: `[](${text})`, anchor: start + 1, caret: start + 1 };
  }
  if (text.length === 0) return { start, length: 0, text: '[]()', anchor: start + 1, caret: start + 1 };
  const caret = start + text.length + 3;
  return { start, length: text.length, text: `[${text}]()`, anchor: caret, caret };
}

// ----- Headings -----

/** ⌘1–6 sets the heading level of the selected lines; the same level again (or 0) makes them paragraphs. */
export function headingEdit(doc: TextDocument, sel: Selection, level: number): Edit | null {
  const [a, b] = selectedLines(doc, sel);
  const current = headingLevelOf(doc.getLineText(a));
  const target = current === level ? 0 : level;
  return spliceLines(doc, sel, a, b, (text) => {
    const m = /^#{1,6}(?:[ \t]+|$)/.exec(text);
    const remove = m ? m[0].length : 0;
    const insert = target > 0 ? '#'.repeat(target) + ' ' : '';
    return { at: 0, remove, insert };
  });
}

function headingLevelOf(line: string): number {
  const m = /^(#{1,6})(?:[ \t]|$)/.exec(line);
  return m ? m[1]!.length : 0;
}

// ----- Tasks -----

/**
 * ⌘L: toggle the task on each selected line between not started and done. A list item gains a box; a plain
 * line becomes a task item.
 */
export function toggleTaskEdit(doc: TextDocument, sel: Selection): Edit | null {
  const [a, b] = selectedLines(doc, sel);
  return spliceLines(doc, sel, a, b, (text) => {
    if (contentOf(text).trim().length === 0 && a !== b) return null;
    const p = parseLinePrefix(text);
    if (p.hasTask) return { at: p.taskMarkColumn, remove: 1, insert: taskMark(toggledTaskState(p.task)) };
    if (p.hasList) return { at: p.afterMarker, remove: 0, insert: '[ ] ' };
    return { at: p.markerStart, remove: 0, insert: '- [ ] ' };
  });
}

/** Set the task on `line` to `state` (a checkbox click, the task picker). Null when the line has no task. */
export function setTaskStateEdit(
  doc: TextDocument,
  sel: Selection,
  line: number,
  state: TaskState,
): Edit | null {
  const p = parseLinePrefix(doc.getLineText(line));
  if (!p.hasTask) return null;
  return spliceLines(doc, sel, line, line, () => ({
    at: p.taskMarkColumn,
    remove: 1,
    insert: taskMark(state),
  }));
}

/** One item of a bulleted list: its first line, its last (nested lines included), and whether it's done. */
interface ListItem {
  readonly first: number;
  readonly last: number;
  readonly done: boolean;
}

/**
 * The items of the list that `line` is an item of (its own level only), or null: a bulleted list, or with
 * `anyKind` a numbered one too.
 */
function listItemsAround(doc: TextDocument, line: number, anyKind = false): ListItem[] | null {
  const p = parseLinePrefix(doc.getLineText(line));
  if (!p.hasList || (p.list !== 'unordered' && !anyKind)) return null;
  const sibling = (i: number) => {
    const q = parseLinePrefix(doc.getLineText(i));
    return q.hasList && q.list === p.list && q.quote === p.quote && q.markerStart === p.markerStart;
  };
  // A nested line: not blank, in the same quote, and starting right of this level's marker.
  const nested = (i: number) => {
    const text = doc.getLineText(i);
    const q = parseLinePrefix(text);
    return (
      text.slice(q.markerStart).trim().length > 0 && q.quote === p.quote && q.markerStart > p.markerStart
    );
  };
  let first = line;
  for (let i = line - 1; i >= 0; i--) {
    if (sibling(i)) first = i;
    else if (!nested(i)) break;
  }
  const items: ListItem[] = [];
  for (let i = first; i < doc.lineCount && sibling(i);) {
    let last = i;
    while (last + 1 < doc.lineCount && nested(last + 1)) last++;
    items.push({ first: i, last, done: parseLinePrefix(doc.getLineText(i)).taskChecked });
    i = last + 1;
  }
  return items;
}

/** A list rewritten with its items in a new order: the range it covers, its new text, and where offsets go. */
interface Reordered {
  readonly start: number;
  readonly end: number;
  readonly text: string;
  readonly map: (offset: number) => number;
}

/**
 * Rewrite the list `items` in the order `order` (the same items), each keeping its nested lines. In a
 * numbered list the numbers stay with the positions, so the list still counts up from where it started.
 */
function reorderItems(doc: TextDocument, items: readonly ListItem[], order: readonly ListItem[]): Reordered {
  const start = doc.getLineStart(items[0]!.first);
  const end = doc.getLineEnd(items[items.length - 1]!.last);
  const numbers = items.map((it) => parseLinePrefix(doc.getLineText(it.first)));
  let at = start;
  const placed = order.map((it, k) => {
    const a = doc.getLineStart(it.first);
    const b = doc.getLineEnd(it.last);
    let body = doc.getText(a, b - a);
    // Renumber: the item takes the number of the position it lands in.
    const p = parseLinePrefix(doc.getLineText(it.first));
    let numberEnd = 0;
    let delta = 0;
    if (p.list === 'ordered' && numbers[k]!.list === 'ordered') {
      const digits = String(numbers[k]!.number).padStart(numbers[k]!.digitCount, '0');
      const from = p.markerStart;
      numberEnd = from + p.digitCount;
      delta = digits.length - p.digitCount;
      body = body.slice(0, from) + digits + body.slice(numberEnd);
    }
    const to = at;
    at += body.length + 1;
    return { a, b, to, body, numberEnd, delta };
  });
  return {
    start,
    end,
    text: placed.map((pl) => pl.body).join('\n'),
    map: (o) => {
      const hit = placed.find((pl) => o >= pl.a && o <= pl.b);
      if (!hit) return o;
      const rel = o - hit.a;
      return hit.to + (rel >= hit.numberEnd ? rel + hit.delta : Math.min(rel, hit.numberEnd + hit.delta));
    },
  };
}

/**
 * "Move ticked tasks to the bottom" (PRD §10.1): reorder the bulleted lists holding `lines` so each list's
 * done tasks follow the others, every item keeping its nested lines and the two groups keeping their order.
 * The selection moves with the text it was in. Null when every list is already in that order.
 */
export function sortTasksEdit(doc: TextDocument, sel: Selection, lines: readonly number[]): Edit | null {
  const pieces: Reordered[] = [];
  const seen = new Set<number>();
  for (const line of lines) {
    const items = listItemsAround(doc, line);
    if (!items || items.length < 2 || seen.has(items[0]!.first)) continue;
    seen.add(items[0]!.first);
    const order = [...items.filter((it) => !it.done), ...items.filter((it) => it.done)];
    if (order.every((it, i) => it === items[i])) continue;
    pieces.push(reorderItems(doc, items, order));
  }
  if (pieces.length === 0) return null;
  pieces.sort((x, y) => x.start - y.start);
  const start = pieces[0]!.start;
  const end = pieces[pieces.length - 1]!.end;
  let text = '';
  let at = start;
  for (const pc of pieces) {
    text += doc.getText(at, pc.start - at) + pc.text;
    at = pc.end;
  }
  const map = (o: number) => pieces.find((pc) => o >= pc.start && o <= pc.end)?.map(o) ?? o;
  return { start, length: end - start, text, anchor: map(sel.anchor), caret: map(sel.caret) };
}

/**
 * ⌥⌘↑ / ⌥⌘↓: move the list item the selection is in (or the items it spans) to the top or bottom of its list,
 * with its nested lines. Works in bulleted, numbered and task lists; a numbered list is renumbered. The
 * selection moves with the item. Null outside a list item, or when the item is already there.
 */
export function moveItemsToEdgeEdit(doc: TextDocument, sel: Selection, dir: -1 | 1): Edit | null {
  const [a, b] = selectedLines(doc, sel);
  const items = listItemsAround(doc, a, true);
  if (!items) return null;
  const i0 = items.findIndex((it) => a >= it.first && a <= it.last);
  const i1 = items.findIndex((it) => b >= it.first && b <= it.last);
  if (i0 < 0 || i1 < 0) return null;
  if ((dir < 0 && i0 === 0) || (dir > 0 && i1 === items.length - 1)) return null;
  const moving = items.slice(i0, i1 + 1);
  const rest = [...items.slice(0, i0), ...items.slice(i1 + 1)];
  const r = reorderItems(doc, items, dir < 0 ? [...moving, ...rest] : [...rest, ...moving]);
  return {
    start: r.start,
    length: r.end - r.start,
    text: r.text,
    anchor: r.map(sel.anchor),
    caret: r.map(sel.caret),
  };
}

/**
 * `edit` (a task state change) followed by sorting the lists it touched, as one undoable edit. Edits that
 * change the text's length (adding a box) are returned as they are.
 */
export function withTaskSort(doc: TextDocument, edit: Edit | null): Edit | null {
  if (!edit || edit.text.length !== edit.length) return edit;
  const after = applyEditTo(doc, edit);
  const first = after.getPosition(edit.start).line;
  const last = after.getPosition(edit.start + edit.text.length).line;
  const lines = Array.from({ length: last - first + 1 }, (_, i) => first + i);
  const sort = sortTasksEdit(after, { anchor: edit.anchor, caret: edit.caret }, lines);
  if (!sort) return edit;
  const start = Math.min(edit.start, sort.start);
  const end = Math.max(edit.start + edit.length, sort.start + sort.length);
  const sorted = applyEditTo(after, sort);
  return {
    start,
    length: end - start,
    text: sorted.getText(start, end - start),
    anchor: sort.anchor,
    caret: sort.caret,
  };
}

/** ⇧⌘L: take the task box off each selected line, leaving the list item. */
export function removeTaskEdit(doc: TextDocument, sel: Selection): Edit | null {
  const [a, b] = selectedLines(doc, sel);
  return spliceLines(doc, sel, a, b, (text) => {
    const p = parseLinePrefix(text);
    if (!p.hasTask) return null;
    return { at: p.afterMarker, remove: p.length - p.afterMarker, insert: '' };
  });
}

// ----- Block formatting -----

function nonBlank(doc: TextDocument, a: number, b: number): number[] {
  const out: number[] = [];
  for (let i = a; i <= b; i++) if (contentOf(doc.getLineText(i)).trim().length > 0) out.push(i);
  return out.length > 0 ? out : [a];
}

/** Toggle a blockquote on the selected lines: remove one level when every line is quoted, else add one. */
export function quoteEdit(doc: TextDocument, sel: Selection): Edit | null {
  const [a, b] = selectedLines(doc, sel);
  const lines = nonBlank(doc, a, b);
  const allQuoted = lines.every((i) => /^[ \t]*>/.test(doc.getLineText(i)));
  return spliceLines(doc, sel, a, b, (text, i) => {
    if (!lines.includes(i)) return null;
    if (allQuoted) {
      const m = /^([ \t]*)> ?/.exec(text)!;
      const indent = m[1]!.length;
      return { at: indent, remove: m[0].length - indent, insert: '' };
    }
    return { at: 0, remove: 0, insert: '> ' };
  });
}

/**
 * Toggle a bulleted or numbered list on the selected lines: when every line already is that kind the markers
 * come off; otherwise each line gets (or switches to) that marker, numbered from 1.
 */
export function listEdit(doc: TextDocument, sel: Selection, kind: 'unordered' | 'ordered'): Edit | null {
  const [a, b] = selectedLines(doc, sel);
  const lines = nonBlank(doc, a, b);
  const all = lines.every((i) => parseLinePrefix(doc.getLineText(i)).list === kind);
  let n = 0;
  return spliceLines(doc, sel, a, b, (text, i) => {
    if (!lines.includes(i)) return null;
    const p = parseLinePrefix(text);
    const remove = p.hasList ? p.afterMarker - p.markerStart : 0;
    if (all) return { at: p.markerStart, remove, insert: '' };
    n++;
    return { at: p.markerStart, remove, insert: kind === 'ordered' ? `${n}. ` : '- ' };
  });
}

/** ⇧⌘E: fence the selected lines as a code block, or open an empty one on a blank line. */
export function codeBlockEdit(doc: TextDocument, sel: Selection): Edit {
  const [a, b] = selectedLines(doc, sel);
  const start = doc.getLineStart(a);
  const end = doc.getLineEnd(b);
  const body = doc.getText().slice(start, end);
  if (isCollapsed(sel) && body.trim().length === 0) {
    return { start, length: end - start, text: '```\n\n```', anchor: start + 4, caret: start + 4 };
  }
  const text = '```\n' + body + '\n```';
  return { start, length: end - start, text, anchor: start + 4, caret: start + 4 + body.length };
}

/** Insert a horizontal rule below the caret's line, as its own paragraph. */
export function ruleEdit(doc: TextDocument, sel: Selection): Edit {
  const line = lineOf(doc, sel.caret);
  const end = doc.getLineEnd(line);
  if (contentOf(doc.getLineText(line)).trim().length === 0) {
    const start = doc.getLineStart(line);
    return { start, length: end - start, text: '---\n', anchor: start + 4, caret: start + 4 };
  }
  const text = '\n\n---\n\n';
  return { start: end, length: 0, text, anchor: end + text.length, caret: end + text.length };
}

// ----- Line operations -----

/** The document's line ending: CRLF when its first line break is one, else LF. */
function lineEnding(doc: TextDocument): string {
  return doc.lineCount > 1 && doc.getLineText(0).endsWith('\r') ? '\r\n' : '\n';
}

/**
 * ⌘D: duplicate the selected lines below themselves; the selection moves to the copy. The copy keeps its
 * bytes, and the break before it is the document's line ending (the last line of a file has none of its own).
 */
export function duplicateLinesEdit(doc: TextDocument, sel: Selection): Edit {
  const [a, b] = selectedLines(doc, sel);
  const start = doc.getLineStart(a);
  const end = doc.getLineEnd(b);
  const block = doc.getText(start, end - start);
  // A CRLF line already holds its `\r` before `end`, so the copy goes between that and the `\n`.
  const text = (block.endsWith('\r') ? '\n' : lineEnding(doc)) + block;
  return { start: end, length: 0, text, anchor: sel.anchor + text.length, caret: sel.caret + text.length };
}

/** ⇧⌘K: delete the selected lines. */
export function deleteLinesEdit(doc: TextDocument, sel: Selection): Edit {
  const [a, b] = selectedLines(doc, sel);
  let start = doc.getLineStart(a);
  let end = doc.getLineEnd(b);
  if (b + 1 < doc.lineCount) end = doc.getLineStart(b + 1);
  else if (a > 0) start = doc.getLineEnd(a - 1);
  return { start, length: end - start, text: '', anchor: start, caret: start };
}

/**
 * ⌥↑ / ⌥↓: move the selected lines past their neighbour; the selection moves with them. Line contents move
 * and line breaks stay where they are, so a CRLF file stays CRLF and the file's last line keeps its lack of
 * a terminator wherever the moved lines land.
 */
export function moveLinesEdit(doc: TextDocument, sel: Selection, dir: -1 | 1): Edit | null {
  const [a, b] = selectedLines(doc, sel);
  if ((dir < 0 && a === 0) || (dir > 0 && b === doc.lineCount - 1)) return null;
  const lo = dir < 0 ? a - 1 : a;
  const hi = dir < 0 ? b : b + 1;
  const n = hi - lo + 1;
  const contents: string[] = [];
  // The break after each position. The region ends before the last line's `\n`, so only its `\r` is in it.
  const breaks: string[] = [];
  for (let i = lo; i <= hi; i++) {
    const t = doc.getLineText(i);
    const cr = t.endsWith('\r');
    contents.push(cr ? t.slice(0, -1) : t);
    breaks.push(i < hi ? (cr ? '\r\n' : '\n') : cr ? '\r' : '');
  }
  // The old line (relative to `lo`) drawn at each new position: the neighbour swaps to the block's far side.
  const order: number[] = [];
  if (dir < 0) {
    for (let r = 1; r < n; r++) order.push(r);
    order.push(0);
  } else {
    order.push(n - 1);
    for (let r = 0; r < n - 1; r++) order.push(r);
  }
  const regionStart = doc.getLineStart(lo);
  const newStart = new Array<number>(n);
  let text = '';
  let afterBlock = regionStart;
  for (let k = 0; k < n; k++) {
    const r = order[k]!;
    newStart[r] = regionStart + text.length;
    text += contents[r]! + breaks[k]!;
    // Past the block's line break; a trailing `\r` is half of a CRLF whose `\n` follows the region.
    if (r === b - lo) afterBlock = regionStart + text.length + (breaks[k] === '\r' ? 1 : 0);
  }
  const map = (offset: number): number => {
    const p = doc.getPosition(offset);
    if (p.line < a || p.line > b) return afterBlock; // a selection ending at the start of the next line
    const r = p.line - lo;
    return newStart[r]! + Math.min(p.column, contents[r]!.length);
  };
  return {
    start: regionStart,
    length: doc.getLineEnd(hi) - regionStart,
    text,
    anchor: map(sel.anchor),
    caret: map(sel.caret),
  };
}
