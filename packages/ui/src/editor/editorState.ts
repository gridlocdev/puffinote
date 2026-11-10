import { signal } from '@preact/signals';
import { MarkdownModel } from '@puffinote/markdown';
import {
  DocumentBuffer,
  selectionLength,
  selectionStart,
  type Selection,
  type TextDocument,
} from '@puffinote/text';
import { findBlocks, type Block } from './blocks.ts';
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
  /** The editor's indent unit for Tab (PRD §7.2: four spaces by default). */
  indentUnit = '    ';

  private modelValue: MarkdownModel;
  private blocksValue: Block[];
  private modelDoc: TextDocument;

  constructor(text = '') {
    this.buffer = new DocumentBuffer(text);
    this.modelDoc = this.buffer.document;
    this.modelValue = MarkdownModel.build(this.modelDoc);
    this.blocksValue = findBlocks(this.modelDoc, this.modelValue);
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

  /** Apply one command result as one undo step. */
  apply(edit: Edit | null): boolean {
    if (!edit) return false;
    this.buffer.replaceRange(edit.start, edit.length, edit.text, edit.caret);
    this.select(edit.anchor, edit.caret);
    return true;
  }

  /** Insert text over the selection, as typing does. */
  insert(text: string): void {
    this.buffer.insert(text);
  }

  select(anchor: number, caret = anchor): void {
    this.buffer.selection = { anchor, caret };
    this.sync();
  }

  /** The selected Markdown source, which is what copy puts on the clipboard (PRD §7.2). */
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
      this.modelValue =
        this.modelValue.lineCount === 0 ? MarkdownModel.build(doc) : this.modelValue.update(doc, first).model;
      this.modelDoc = doc;
      this.blocksValue = findBlocks(doc, this.modelValue);
      this.version.value++;
    }
    const s = this.buffer.selection;
    const cur = this.selection.peek();
    if (cur.anchor !== s.anchor || cur.caret !== s.caret) this.selection.value = s;
  }
}
