import { signal } from '@preact/signals';
import { MarkdownModel } from '@puffinote/markdown';
import {
  DocumentBuffer,
  isCollapsed,
  selectionEnd,
  selectionIntersects,
  selectionsEqual,
  selectionLength,
  selectionStart,
  type Selection,
  type TextDocument,
  type UndoEffect,
  type UndoRun,
} from '@puffinote/text';
import { findBlocks, updateBlocks, type Block } from './blocks.ts';
import {
  backspaceItemEdit,
  backspacePairEdit,
  deleteRangeEdit,
  enterEdit,
  indentEdit,
  joinLinesDeleteEdit,
  mapRange,
  offsetAfterEdit,
  outdentEdit,
  pictureLinesEdit,
  softBreakEdit,
  type Edit,
  type EditContext,
} from './commands.ts';
import { lineContentEnd, wordLeft, wordRight } from './motion.ts';
import { pictureExtent, type ViewMode } from './reveal.ts';

/** Where pasted or dropped pictures go: the document and selection when they were pasted. */
export interface PictureSpot {
  readonly doc: TextDocument;
  readonly selection: Selection;
}

/**
 * The editor's state: the undoable buffer, the incrementally updated Markdown model and the view mode,
 * published as signals so the surface re-renders on change. The surface never edits the buffer except through
 * this class, so the model can't fall behind the text.
 */
export class EditorState {
  readonly buffer: DocumentBuffer;
  /** Bumps on every text change. */
  readonly version = signal(0);
  readonly selection = signal<Selection>({ anchor: 0, caret: 0 });
  readonly mode = signal<ViewMode>('live');
  /**
   * The start of the picture showing its link text (View Link Text, PRD §10.6), or `null`. It clears once the
   * selection leaves that picture, so the picture is drawn again.
   */
  readonly shownPicture = signal<number | null>(null);
  /** Bumps when `reveal` asks the surface to bring the caret into view. */
  readonly revealTick = signal(0);
  /** The editor's indent unit for Tab (PRD §10.1: four spaces by default). */
  indentUnit = '    ';

  private modelValue: MarkdownModel;
  private blocksValue: Block[];
  private modelDoc: TextDocument;
  private keys: number[];
  /**
   * The openers of pairs the editor auto-inserted (`(` of `(|)`), innermost last. Backspace removes both
   * halves only of these, and only until any other edit or caret move.
   */
  private autoPairs: number[] = [];
  private keepAutoPairs = false;

  constructor(text = '') {
    this.buffer = new DocumentBuffer(text);
    this.modelDoc = this.buffer.document;
    this.modelValue = MarkdownModel.build(this.modelDoc);
    this.blocksValue = findBlocks(this.modelDoc, this.modelValue);
    this.keys = freshKeys(this.modelDoc.lineCount);
    this.buffer.onChange(() => this.sync());
  }

  get document(): TextDocument {
    return this.buffer.document;
  }

  get model(): MarkdownModel {
    return this.modelValue;
  }

  get blocks(): readonly Block[] {
    return this.blocksValue;
  }

  get text(): string {
    return this.buffer.document.getText();
  }

  /** Replace the whole document (open a file). Clears undo. */
  load(text: string): void {
    this.buffer.load(text);
  }

  /**
   * A stable identity for `line` that moves with the line's text when lines are inserted or removed above it,
   * so the surface can key its rows by it and keep the DOM (and the memoized render) of untouched lines.
   */
  lineKey(line: number): number {
    return this.keys[line] ?? -1 - line;
  }

  /**
   * Apply one command result (Enter, Tab, a line move, a format) as its own undo step: it never coalesces
   * with the typing before or after it (PRD §10.1). With `run`, the edit is part of a key's run instead
   * (a run of Backspace, a held key), which folds into one step (`UndoRun`).
   */
  apply(edit: Edit | null, effect?: UndoEffect, run?: UndoRun): boolean {
    if (!edit) return false;
    if (!run) breakUndoRun(this.buffer);
    this.buffer.replaceRange(edit.start, edit.length, edit.text, edit.caret, effect, run);
    this.select(edit.anchor, edit.caret);
    if (!run) breakUndoRun(this.buffer);
    return true;
  }

  /** Apply the result of typing one character (an auto-pair, a step over a closer): it is part of the run. */
  applyTyping(edit: Edit): void {
    const pair = edit.length === 0 && edit.text.length === 2 && edit.caret === edit.start + 1;
    this.keepAutoPairs = pair;
    try {
      this.buffer.replaceRange(edit.start, edit.length, edit.text, edit.caret);
      this.select(edit.anchor, edit.caret);
    } finally {
      this.keepAutoPairs = false;
    }
    if (pair) this.autoPairs.push(edit.start);
  }

  /**
   * Backspace inside an empty pair the editor has just auto-inserted removes both halves (PRD §10.1). False
   * anywhere else, including a pair the user typed, so Backspace deletes one character.
   */
  backspacePair(run?: UndoRun): boolean {
    const sel = this.buffer.selection;
    const top = this.autoPairs[this.autoPairs.length - 1];
    if (top === undefined || sel.caret !== top + 1) return false;
    const edit = backspacePairEdit(this.document, sel);
    if (!edit) return false;
    const rest = this.autoPairs.slice(0, -1);
    this.apply(edit, undefined, run);
    this.autoPairs = rest;
    return true;
  }

  /** Delete the selection as one undo step; across lines it renumbers lists in the same step. */
  deleteSelection(): void {
    const sel = this.buffer.selection;
    this.apply(deleteRangeEdit(this.document, selectionStart(sel), selectionEnd(sel)));
  }

  /**
   * Backspace, ⌥⌫ (`word`) and ⌘⌫ (`line`; PRD §10.1): the selection if there is one; else back to `lineFrom`
   * for ⌘⌫ (where the caret's visual row or a list item's text starts, which only the layout knows; null
   * deletes as Backspace does), or the word before. A plain Backspace first takes a list item's marker in
   * Rich view, then an empty pair the editor auto-inserted, then joins lines (renumbering lists), and
   * otherwise deletes one character.
   */
  deleteBackward(unit: 'char' | 'word' | 'line', run?: UndoRun, lineFrom: number | null = null): void {
    const doc = this.document;
    const sel = this.buffer.selection;
    const caret = sel.caret;
    if (!isCollapsed(sel)) this.deleteSelection();
    else if (unit === 'line' && lineFrom !== null)
      this.apply(deleteRangeEdit(doc, lineFrom, caret), undefined, run);
    else if (unit === 'word') this.apply(deleteRangeEdit(doc, wordLeft(doc, caret), caret), undefined, run);
    else if (this.mode.peek() !== 'raw' && this.apply(backspaceItemEdit(doc, sel), undefined, run)) return;
    else if (this.backspacePair(run)) return;
    else if (!this.apply(joinLinesDeleteEdit(doc, this.buffer.previousCaret(caret), caret), undefined, run)) {
      this.buffer.backspace();
    }
  }

  /**
   * Delete, ⌥⌦ (`word`) and ⌃K (`line`): the selection if there is one; else ⌃K deletes to the line's end, or
   * the line break when the caret is already there. Joining lines renumbers lists.
   */
  deleteForward(unit: 'char' | 'word' | 'line', run?: UndoRun): void {
    const doc = this.document;
    const sel = this.buffer.selection;
    const caret = sel.caret;
    const lineEnd = unit === 'line' ? lineContentEnd(doc, doc.getPosition(caret).line) : caret;
    if (!isCollapsed(sel)) this.deleteSelection();
    else if (lineEnd > caret) this.apply(deleteRangeEdit(doc, caret, lineEnd), undefined, run);
    else if (unit === 'word') this.apply(deleteRangeEdit(doc, caret, wordRight(doc, caret)), undefined, run);
    else if (!this.apply(joinLinesDeleteEdit(doc, caret, this.buffer.nextCaret(caret)), undefined, run)) {
      this.buffer.deleteForward();
    }
  }

  /** Enter, or Shift+Enter (`soft`): continues lists, quotes and code as the caret's context asks (PRD §10.1). */
  enter(soft: boolean, run?: UndoRun): void {
    const sel = this.buffer.selection;
    const doc = this.document;
    this.apply(
      soft
        ? softBreakEdit(sel, doc)
        : enterEdit(doc, sel, this.mode.peek(), this.contextAt(sel.caret), this.indentUnit),
      undefined,
      run,
    );
  }

  /** Tab and Shift+Tab (`outdent`): indent or outdent the selected lines by the indent unit. */
  indent(outdent: boolean, run?: UndoRun): void {
    const sel = this.buffer.selection;
    const doc = this.document;
    this.apply(
      outdent ? outdentEdit(doc, sel, this.indentUnit) : indentEdit(doc, sel, this.indentUnit),
      undefined,
      run,
    );
  }

  /** Insert text over the selection, as typing does. */
  insert(text: string): void {
    this.buffer.insert(text);
  }

  /** Insert pasted text over the selection as one undo step of its own (PRD §10.1). */
  paste(text: string): void {
    breakUndoRun(this.buffer);
    this.buffer.insert(text);
    breakUndoRun(this.buffer);
  }

  /** Remember where pictures pasted or dropped now go, for `insertPictureLines` once they are saved. */
  pictureSpot(): PictureSpot {
    return { doc: this.document, selection: this.buffer.selection };
  }

  /**
   * Put a line for each picture link where `spot` was, as one undo step (PRD §10.6). Edits made while the
   * pictures saved move the spot along, and a selection made meanwhile is kept, never replaced.
   */
  insertPictureLines(spot: PictureSpot, links: readonly string[]): void {
    const doc = this.document;
    const range = mapRange(spot.doc, doc, selectionStart(spot.selection), selectionEnd(spot.selection));
    const edit = pictureLinesEdit(doc, range.from, range.to, links);
    const now = this.buffer.selection;
    const moved = doc !== spot.doc || !selectionsEqual(now, spot.selection);
    this.apply(edit);
    if (moved) this.select(offsetAfterEdit(edit, now.anchor), offsetAfterEdit(edit, now.caret));
  }

  select(anchor: number, caret = anchor): void {
    this.buffer.selection = { anchor, caret };
    this.sync();
  }

  /** Select and ask the surface to scroll the caret into the middle of the view (Outline, Search). */
  reveal(anchor: number, caret = anchor): void {
    this.select(anchor, caret);
    this.revealTick.value++;
  }

  /** Show a picture's link text and select it (View Link Text, PRD §10.6). */
  showLinkText(start: number, end: number): void {
    this.shownPicture.value = start;
    this.select(start, end);
  }

  /** The selected Markdown source, which is what copy puts on the clipboard (PRD §10.1). */
  selectedText(): string {
    const s = this.buffer.selection;
    return this.document.getText(selectionStart(s), selectionLength(s));
  }

  undo(): void {
    this.buffer.undo();
  }

  redo(): void {
    this.buffer.redo();
  }

  /** What the caret is in, for Enter and Tab. */
  contextAt(offset: number): EditContext {
    const line = this.document.getPosition(offset).line;
    const first = this.modelValue.lineTokens(line)[0];
    const state = this.modelValue.enteringState(line).kind;
    if (state === 'fencedCode' || state === 'indentedCode' || first?.type === 'codeText') return 'code';
    if (this.blocksValue.some((b) => b.kind === 'table' && line >= b.startLine && line <= b.endLine)) {
      return 'table';
    }
    return 'text';
  }

  private sync(): void {
    if (!this.keepAutoPairs) this.autoPairs = [];
    const doc = this.buffer.document;
    if (doc !== this.modelDoc) {
      const first = Math.min(this.buffer.lastChangedLine, Math.max(0, this.modelValue.lineCount - 1));
      if (this.modelValue.lineCount === 0) {
        this.modelValue = MarkdownModel.build(doc);
        this.blocksValue = findBlocks(doc, this.modelValue);
      } else {
        const u = this.modelValue.update(doc, first);
        this.modelValue = u.model;
        this.blocksValue = updateBlocks(this.blocksValue, doc, this.modelValue, u.dirtyFromLine);
      }
      this.keys = shiftKeys(this.keys, this.buffer.lastChangedLine, doc.lineCount);
      this.modelDoc = doc;
      this.version.value++;
    }
    const s = this.buffer.selection;
    const cur = this.selection.peek();
    if (cur.anchor !== s.anchor || cur.caret !== s.caret) this.selection.value = s;
    const shown = this.shownPicture.peek();
    if (shown !== null) {
      const line = doc.getPosition(Math.min(shown, doc.length)).line;
      const e = pictureExtent(this.modelValue.lineTokens(line), shown);
      if (!e || !selectionIntersects(s, e.start, e.end - e.start)) this.shownPicture.value = null;
    }
  }
}

let nextKey = 1;

function freshKeys(n: number): number[] {
  const keys = new Array<number>(n);
  for (let i = 0; i < n; i++) keys[i] = nextKey++;
  return keys;
}

/**
 * The line keys after an edit that began on line `first`: lines above keep theirs, `first` keeps its own,
 * lines added below it get new keys and lines below the edit keep the keys they had. Keys are only a hint
 * for reusing DOM (the rows still compare their content), so a multi-line replacement that keeps the line
 * count needs no exact bookkeeping. The same array comes back when the line count is unchanged.
 */
function shiftKeys(keys: number[], first: number, lineCount: number): number[] {
  const delta = lineCount - keys.length;
  if (delta === 0) return keys;
  const at = Math.min(Math.max(first, 0), Math.min(keys.length, lineCount) - 1) + 1;
  return delta > 0
    ? keys.slice(0, at).concat(freshKeys(delta), keys.slice(at))
    : keys.slice(0, at).concat(keys.slice(at - delta));
}

/** End the current typing run, so the next edit starts a new undo step. */
function breakUndoRun(buffer: DocumentBuffer): void {
  buffer.breakCoalescing();
}
