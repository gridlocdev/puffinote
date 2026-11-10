import { signal } from '@preact/signals';
import { MarkdownModel } from '@puffinote/markdown';
import {
  DocumentBuffer,
  selectionLength,
  selectionStart,
  type Selection,
  type TextDocument,
} from '@puffinote/text';
import { findBlocks, updateBlocks, type Block } from './blocks.ts';
import type { Edit, EditContext } from './commands.ts';
import type { ViewMode } from './reveal.ts';

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
  /** Bumps when `reveal` asks the surface to bring the caret into view. */
  readonly revealTick = signal(0);
  /** The editor's indent unit for Tab (PRD §10.1: four spaces by default). */
  indentUnit = '    ';

  private modelValue: MarkdownModel;
  private blocksValue: Block[];
  private modelDoc: TextDocument;
  private keys: number[];

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
   * with the typing before or after it (PRD §10.1).
   */
  apply(edit: Edit | null): boolean {
    if (!edit) return false;
    breakUndoRun(this.buffer);
    this.buffer.replaceRange(edit.start, edit.length, edit.text, edit.caret);
    this.select(edit.anchor, edit.caret);
    breakUndoRun(this.buffer);
    return true;
  }

  /** Apply the result of typing one character (an auto-pair, a step over a closer): it is part of the run. */
  applyTyping(edit: Edit): void {
    this.buffer.replaceRange(edit.start, edit.length, edit.text, edit.caret);
    this.select(edit.anchor, edit.caret);
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

  select(anchor: number, caret = anchor): void {
    this.buffer.selection = { anchor, caret };
    this.sync();
  }

  /** Select and ask the surface to scroll the caret into the middle of the view (Outline, Search). */
  reveal(anchor: number, caret = anchor): void {
    this.select(anchor, caret);
    this.revealTick.value++;
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
