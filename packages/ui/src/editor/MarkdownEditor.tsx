import { stripDuplicateTaskMarker, toggledTaskState, parseLinePrefix, type Token } from '@puffinote/markdown';
import { isCollapsed, selectionEnd, selectionLength, selectionStart } from '@puffinote/text';
import { useLayoutEffect, useMemo, useRef } from 'preact/hooks';
import type { JSX } from 'preact';
import { CommandCatalog } from '../commands/command.ts';
import { isMac } from '../commands/shortcut.ts';
import { blockAtLine, type Block } from './blocks.ts';
import {
  backspacePairEdit,
  enterEdit,
  indentEdit,
  outdentEdit,
  setTaskStateEdit,
  withTaskSort,
  softBreakEdit,
  typeCharEdit,
} from './commands.ts';
import { markdownCommandSpecs } from './editorCommands.ts';
import type { EditorState } from './editorState.ts';
import { caretBox, firstRowEndingBelow, hitTest, lineSelectionRects } from './geometry.ts';
import { renderLine, snapOutOfConcealed } from './lineRender.ts';
import { BlockWidget, LineView } from './LineView.tsx';
import {
  itemTextStart as listTextStart,
  lineContentEnd,
  smartLineStart,
  wordLeft,
  wordRangeAt,
  wordRight,
} from './motion.ts';
import { keyAction } from './keys.ts';
import { lineReveal, type LineReveal } from './reveal.ts';

export interface MarkdownEditorProps {
  readonly state: EditorState;
  /** Map an image reference to a URL the page can load (a notebook-relative path, say). */
  readonly resolveImage?: (url: string) => string;
  /** Open a link the user ⌘-clicked. Only http, https and mailto links reach it. */
  readonly openLink?: (url: string) => void;
  readonly autoFocus?: boolean;
  readonly placeholder?: string;
  /** Where to scroll on mount (a tab coming back). */
  readonly initialScrollTop?: number;
  readonly onScroll?: (top: number) => void;
  /** How large to draw the note (1 is 100%). A change re-measures the caret and selection. */
  readonly zoom?: number;
  /** Ticking a task done moves it below the open ones in its list (Settings: Move ticked tasks to the bottom). */
  readonly sortTasks?: boolean;
  /** A right-click (or Control-click on macOS): show the editing menu at that point, or a picture's menu. */
  readonly onContextMenu?: (at: EditorMenuPoint) => void;
  /** Save pasted or dropped pictures and return the Markdown for each; without it, only text pastes. */
  readonly onPictures?: (files: readonly File[]) => Promise<readonly string[]>;
}

/** Where the editor was right-clicked, and the picture under the pointer, if any. */
export interface EditorMenuPoint {
  readonly x: number;
  readonly y: number;
  readonly picture?: PictureAt;
}

/** A picture drawn in the note: its Markdown's range in the document, and its link as written. */
export interface PictureAt {
  readonly start: number;
  readonly end: number;
  readonly url: string;
}

const identity = (url: string): string => url;
const defaultOpen = (url: string): void => void window.open(url, '_blank', 'noopener,noreferrer');
/** The longest selection the hidden input mirrors as text; longer ones mirror as a stand-in. */
const maxMirrorLength = 2000;

interface LineCache {
  readonly doc: EditorState['document'];
  readonly texts: string[];
  readonly starts: number[];
}

interface RowIndex {
  /** The line each row draws, in row order. */
  readonly lines: readonly number[];
  readonly rows: readonly HTMLElement[];
  readonly byLine: readonly (HTMLElement | undefined)[];
  readonly rowOfLine: readonly (number | undefined)[];
}

/** The picture files on the clipboard or in a drop. */
function pictureFiles(data: DataTransfer | null | undefined): File[] {
  return [...(data?.files ?? [])].filter((f) => f.type.startsWith('image/'));
}

/** The links a ⌘-click may open (PRD §10.3). */
export function isOpenableLink(url: string): boolean {
  return /^(https?:|mailto:)/i.test(url.trim());
}

/**
 * The live-preview Markdown editor (PRD §10). Built from scratch: lines render as DOM, a hidden textarea takes
 * keyboard, IME and clipboard input, and the caret and selection are drawn as an overlay from the offset ↔
 * visual map in `geometry.ts`.
 */
export function MarkdownEditor(props: MarkdownEditorProps) {
  const { state } = props;
  const resolveImage = props.resolveImage ?? identity;
  const scrollerRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const caretRef = useRef<HTMLDivElement>(null);
  const selectionRef = useRef<HTMLDivElement>(null);
  const goalX = useRef<number | null>(null);
  const composing = useRef(false);
  const revealCaret = useRef(false);
  /** The latest render's vertical move, for a move that finishes on the next frame. */
  const verticalRef = useRef<(dir: 1 | -1, extend: boolean) => void>(() => {});
  const stateRef = useRef(state);
  stateRef.current = state;
  const sortTasksRef = useRef(false);
  sortTasksRef.current = props.sortTasks ?? false;
  const catalog = useMemo(
    () =>
      new CommandCatalog(
        markdownCommandSpecs({
          state: () => stateRef.current,
          focus: () => inputRef.current?.focus(),
          sortTasks: () => sortTasksRef.current,
        }),
      ),
    [],
  );

  // Subscribing: reading the signals re-renders on any text, selection or mode change.
  void state.version.value;
  const sel = state.selection.value;
  const mode = state.mode.value;
  const revealTick = state.revealTick.value;
  const seenReveal = useRef(revealTick);
  const doc = state.document;
  const model = state.model;
  const blocks = state.blocks;

  // ----- Render the lines -----
  // Line texts and starts for this snapshot, kept across renders so a caret move re-reads nothing and the
  // memoized rows compare identical strings.
  const lineCache = useRef<LineCache>({ doc, texts: [], starts: [] });
  if (lineCache.current.doc !== doc) lineCache.current = { doc, texts: [], starts: [] };
  const cache = lineCache.current;
  const rows: JSX.Element[] = [];
  /** The line each row draws (a widget row draws its block's first line). */
  const rowLines: number[] = [];
  let bi = 0;
  let blockRange: { start: number; end: number } | null = null;
  let rangeOf: Block | null = null;
  for (let i = 0; i < doc.lineCount; i++) {
    while (bi < blocks.length && blocks[bi]!.endLine < i) bi++;
    const block = bi < blocks.length && blocks[bi]!.startLine <= i ? blocks[bi]! : null;
    if (block && block !== rangeOf) {
      rangeOf = block;
      blockRange = { start: doc.getLineStart(block.startLine), end: doc.getLineEnd(block.endLine) };
    }
    const range = block ? blockRange : null;
    if (block?.kind === 'table' && mode === 'live' && i === block.startLine) {
      const touched = range !== null && selectionStart(sel) <= range.end && selectionEnd(sel) >= range.start;
      if (!touched) {
        rows.push(
          <BlockWidget
            key={`w${state.lineKey(i)}`}
            line={i}
            source={doc.getText(range!.start, range!.end - range!.start)}
          />,
        );
        rowLines.push(i);
        i = block.endLine;
        continue;
      }
    }
    const start = (cache.starts[i] ??= doc.getLineStart(i));
    const text = (cache.texts[i] ??= doc.getLineText(i));
    const tokens = model.lineTokens(i);
    rows.push(
      <LineView
        key={state.lineKey(i)}
        index={i}
        text={text}
        start={start}
        tokens={tokens}
        reveal={lineReveal(tokens, start, start + text.length, sel, range)}
        mode={mode}
        block={block?.kind ?? null}
        resolveImage={resolveImage}
      />,
    );
    rowLines.push(i);
  }

  // The row elements in order and by line, rebuilt only when the rows changed. Memoized rows that moved
  // (a line inserted above them) skip their render, so their `data-line` is renumbered here.
  const rowIndex = useRef<RowIndex>({ lines: [], rows: [], byLine: [], rowOfLine: [] });

  function indexRows(content: HTMLElement): void {
    const prev = rowIndex.current;
    const kids = content.children;
    const same =
      prev.lines.length === rowLines.length &&
      prev.rows.every((el, k) => el === kids[k]) &&
      prev.lines.every((l, k) => l === rowLines[k]);
    if (same) return;
    const rowEls: HTMLElement[] = new Array<HTMLElement>(rowLines.length);
    const byLine: (HTMLElement | undefined)[] = [];
    const rowOfLine: number[] = [];
    for (let k = 0; k < rowLines.length; k++) {
      const el = kids[k] as HTMLElement;
      const line = rowLines[k]!;
      const attr = String(line);
      if (el.getAttribute('data-line') !== attr) el.setAttribute('data-line', attr);
      rowEls[k] = el;
      byLine[line] = el;
      rowOfLine[line] = k;
    }
    rowIndex.current = { lines: rowLines, rows: rowEls, byLine, rowOfLine };
  }

  // ----- Caret, selection and the input's position, after the DOM is up to date -----
  const paintRef = useRef<() => void>(() => {});
  /** Put the caret and the input back over the caret's spot, after lines moved without a render. */
  const placeRef = useRef<() => void>(() => {});
  useLayoutEffect(() => {
    const content = contentRef.current;
    const caretEl = caretRef.current;
    const layer = selectionRef.current;
    const input = inputRef.current;
    if (!content || !caretEl || !layer || !input) return;
    indexRows(content);
    const origin = content.getBoundingClientRect();

    const box = caretBoxAt(sel.caret);
    if (box) {
      const x = box.left - origin.left;
      const y = box.top - origin.top;
      caretEl.style.transform = `translate(${x}px, ${y}px)`;
      caretEl.style.height = `${box.height}px`;
      caretEl.style.display = '';
      input.style.transform = `translate(${x}px, ${y}px)`;
      // Restart the blink so the caret is solid right after it moves.
      caretEl.classList.remove('pn-blink');
      void caretEl.offsetWidth;
      caretEl.classList.add('pn-blink');
      if (seenReveal.current !== revealTick) {
        seenReveal.current = revealTick;
        centreInView(box);
      } else if (revealCaret.current) scrollIntoView(box);
    } else {
      caretEl.style.display = 'none';
    }
    revealCaret.current = false;

    placeRef.current = () => {
      const b = caretBoxAt(sel.caret);
      if (!b) return;
      const o = content.getBoundingClientRect();
      const t = `translate(${b.left - o.left}px, ${b.top - o.top}px)`;
      caretEl.style.transform = t;
      caretEl.style.height = `${b.height}px`;
      input.style.transform = t;
    };
    paintRef.current = () => paintSelection(content, layer);
    paintRef.current();
    // Mirror a non-empty selection in the input, selected, so the browser enables Copy and Cut (Safari
    // dispatches neither for an empty textarea selection). A long selection mirrors as one space: the copy
    // handler writes the real Markdown source either way, and typing replaces the mirror like any selection.
    if (!composing.current) {
      const mirror = isCollapsed(sel)
        ? ''
        : selectionLength(sel) <= maxMirrorLength
          ? state.selectedText()
          : ' ';
      if (input.value !== mirror) input.value = mirror;
      if (mirror) input.setSelectionRange(0, mirror.length);
    }
  });

  /**
   * Draw the selection highlight for the rows on screen (plus a screen of margin each way); scrolling repaints.
   * All rectangles are measured before the layer changes, so the browser lays out once, and a Select All on a
   * long note measures a few dozen lines instead of every one.
   */
  function paintSelection(content: HTMLElement, layer: HTMLElement): void {
    if (isCollapsed(sel)) {
      if (layer.firstChild) layer.replaceChildren();
      return;
    }
    const { rows, rowOfLine, byLine } = rowIndex.current;
    const scroller = scrollerRef.current;
    const a = doc.getPosition(selectionStart(sel));
    const b = doc.getPosition(selectionEnd(sel));
    let first = a.line;
    let last = b.line;
    if (scroller && rows.length > 0) {
      const view = scroller.getBoundingClientRect();
      const margin = Math.max(view.height, 200);
      const top = firstRowEndingBelow(rows, view.top - margin);
      const bottom = firstRowEndingBelow(rows, view.bottom + margin);
      const lineOfRow = rowIndex.current.lines;
      first = Math.max(first, lineOfRow[Math.min(top, rows.length - 1)] ?? 0);
      last = Math.min(last, lineOfRow[Math.min(bottom, rows.length - 1)] ?? last);
    }
    const origin = content.getBoundingClientRect();
    const rects: DOMRect[] = [];
    for (let line = first; line <= last; line++) {
      const el = byLine[line];
      if (!el || rowOfLine[line] === undefined || el.style.display === 'none') continue;
      const from = line === a.line ? a.column : 0;
      const to = line === b.line ? b.column : Number.MAX_SAFE_INTEGER;
      for (const r of lineSelectionRects(el, from, to, line < b.line)) rects.push(r);
    }
    const frag = document.createDocumentFragment();
    for (const r of rects) {
      const div = document.createElement('div');
      div.className = 'pn-selection-rect';
      div.style.transform = `translate(${r.left - origin.left}px, ${r.top - origin.top}px)`;
      div.style.width = `${r.width}px`;
      div.style.height = `${r.height}px`;
      frag.appendChild(div);
    }
    layer.replaceChildren(frag);
  }

  const paintQueued = useRef(false);
  function onScrollerScroll(e: Event): void {
    props.onScroll?.((e.currentTarget as HTMLElement).scrollTop);
    if (isCollapsed(sel) || paintQueued.current) return;
    paintQueued.current = true;
    requestAnimationFrame(() => {
      paintQueued.current = false;
      paintRef.current();
    });
  }

  useLayoutEffect(() => {
    if (props.initialScrollTop && scrollerRef.current) scrollerRef.current.scrollTop = props.initialScrollTop;
  }, []);

  // Lines can move with no render: off-screen lines take their real height a frame after a view change or
  // zoom (content-visibility), text rewraps when the window resizes, a picture loads. The content's size
  // changes with each, so redraw the caret and selection then.
  useLayoutEffect(() => {
    const content = contentRef.current;
    if (!content || typeof ResizeObserver === 'undefined') return;
    let queued = false;
    const ro = new ResizeObserver(() => {
      if (queued) return;
      queued = true;
      requestAnimationFrame(() => {
        queued = false;
        placeRef.current();
        paintRef.current();
      });
    });
    ro.observe(content);
    return () => ro.disconnect();
  }, []);

  useLayoutEffect(() => {
    if (props.autoFocus) inputRef.current?.focus({ preventScroll: true });
  }, []);

  function lineEl(line: number): HTMLElement | null {
    return rowIndex.current.byLine[line] ?? null;
  }

  function caretBoxAt(offset: number) {
    const p = doc.getPosition(Math.min(offset, doc.length));
    const el = lineEl(p.line);
    if (!el || el.hasAttribute('data-widget')) return null;
    return caretBox(el, p.column);
  }

  function centreInView(box: { top: number; height: number }): void {
    const scroller = scrollerRef.current;
    if (!scroller) return;
    const r = scroller.getBoundingClientRect();
    scroller.scrollTop += box.top - (r.top + r.height / 3);
  }

  function scrollIntoView(box: { top: number; height: number }): void {
    const scroller = scrollerRef.current;
    if (!scroller) return;
    const r = scroller.getBoundingClientRect();
    const margin = 24;
    if (box.top < r.top + margin) scroller.scrollTop -= r.top + margin - box.top;
    else if (box.top + box.height > r.bottom - margin)
      scroller.scrollTop += box.top + box.height - (r.bottom - margin);
  }

  // ----- Selection helpers -----

  /**
   * The reveal that decides where a caret may land: inline markers as the selection reveals them now, and a
   * task's prefix as the caret at `offset` would reveal it, so stepping left from a task's text into `]`
   * shows the marker instead of jumping over it.
   */
  function snapReveal(tokens: readonly Token[], start: number, end: number, offset: number): LineReveal {
    const now = lineReveal(tokens, start, end, sel, null);
    const there = lineReveal(tokens, start, end, { anchor: offset, caret: offset }, null);
    return now.prefixActive === there.prefixActive ? now : { ...now, prefixActive: there.prefixActive };
  }

  /** Step `offset` out of a concealed marker on its line, toward `dir` (concealed runs are atomic). */
  function snap(offset: number, dir: 1 | -1): number {
    const p = doc.getPosition(offset);
    const start = doc.getLineStart(p.line);
    const tokens = model.lineTokens(p.line);
    const r = renderLine(
      doc.getLineText(p.line),
      start,
      tokens,
      snapReveal(tokens, start, doc.getLineEnd(p.line), offset),
      mode,
      blockAtLine(blocks, p.line)?.kind ?? null,
    );
    return start + snapOutOfConcealed(r.segments, p.column, dir);
  }

  function moveTo(offset: number, extend: boolean, keepGoal = false): void {
    if (!keepGoal) goalX.current = null;
    revealCaret.current = true;
    const clamped = Math.max(0, Math.min(doc.length, offset));
    state.select(extend ? sel.anchor : clamped, clamped);
  }

  function horizontal(dir: 1 | -1, extend: boolean, unit: 'char' | 'word' | 'line'): void {
    if (!extend && !isCollapsed(sel) && unit === 'char') {
      moveTo(dir > 0 ? selectionEnd(sel) : selectionStart(sel), false);
      return;
    }
    const at = sel.caret;
    let to: number;
    if (unit === 'word') to = dir > 0 ? wordRight(doc, at) : wordLeft(doc, at);
    else if (unit === 'line') {
      const line = doc.getPosition(at).line;
      to = dir > 0 ? lineContentEnd(doc, line) : startOf(line, at);
    } else to = snap(dir > 0 ? state.buffer.nextCaret(at) : state.buffer.previousCaret(at), dir);
    moveTo(to, extend);
  }

  /** Where a list item's text starts on `line` in Rich view; the line start in the views that show markers. */
  function itemTextStart(line: number): number {
    return mode === 'live' ? listTextStart(doc, line) : doc.getLineStart(line);
  }

  /** Home, ⌘↑: the start of a list item's text first in Rich view (PRD §10.2). */
  function startOf(line: number, from: number): number {
    if (mode !== 'live') return doc.getLineStart(line);
    return doc.getPosition(from).line === line ? smartLineStart(doc, from) : listTextStart(doc, line);
  }

  function vertical(dir: 1 | -1, extend: boolean): void {
    const content = contentRef.current;
    if (!content) return;
    const from = doc.getPosition(sel.caret).line;
    if (sel.caret < itemTextStart(from)) {
      // In a task's revealed `- [ ]`: the line is laid out wider than it will be once the caret leaves the
      // marker. Step to the text first and measure the move once the marker has turned back into a checkbox.
      moveTo(itemTextStart(from), extend, true);
      requestAnimationFrame(() => verticalRef.current(dir, extend));
      return;
    }
    const box = caretBoxAt(sel.caret);
    const caretLine = doc.getPosition(sel.caret).line;
    const current = lineEl(caretLine);
    if (!box || !current) return;
    const origin = content.getBoundingClientRect();
    goalX.current ??= box.left - origin.left;

    const cr = current.getBoundingClientRect();
    // A wrapped line has more rows: step one line-height within it before leaving it.
    const lh = parseFloat(getComputedStyle(current).lineHeight) || box.height;
    const mid = box.top + box.height / 2;
    let y: number;
    if (dir > 0 && mid + lh < cr.bottom) {
      y = mid + lh;
    } else if (dir < 0 && mid - lh > cr.top) {
      y = mid - lh;
    } else {
      const lines = rowIndex.current.rows;
      let i = (rowIndex.current.rowOfLine[caretLine] ?? lines.indexOf(current)) + dir;
      while (i >= 0 && i < lines.length && lines[i]!.getBoundingClientRect().height === 0) i += dir;
      const targetEl = lines[i];
      if (!targetEl) {
        // ↑ on the first row stops at the start of its text (a task's checkbox stays a checkbox).
        moveTo(dir > 0 ? doc.length : sel.caret >= itemTextStart(0) ? itemTextStart(0) : 0, extend);
        return;
      }
      targetEl.scrollIntoView({ block: 'nearest' });
      const tr = targetEl.getBoundingClientRect();
      y = dir > 0 ? tr.top + Math.min(tr.height / 2, 6) : tr.bottom - Math.min(tr.height / 2, 6);
    }
    const now = content.getBoundingClientRect();
    const hit = hitTest(content, now.left + goalX.current, y, rowIndex.current.rows);
    if (!hit) return;
    const offset =
      doc.getLineStart(hit.line) +
      Math.min(hit.col, lineContentEnd(doc, hit.line) - doc.getLineStart(hit.line));
    // Stepping onto a list item at its left edge lands where its text starts, after the bullet or checkbox,
    // not before it (which would reveal a task's `- [ ]` and type in front of the marker).
    moveTo(Math.max(offset, itemTextStart(hit.line)), extend, true);
  }

  verticalRef.current = vertical;

  function apply(edit: Parameters<EditorState['apply']>[0]): void {
    goalX.current = null;
    revealCaret.current = true;
    state.apply(edit);
  }

  function insertText(text: string): void {
    goalX.current = null;
    revealCaret.current = true;
    if ([...text].length === 1) {
      const edit = typeCharEdit(doc, sel, text);
      if (edit) {
        state.applyTyping(edit);
        return;
      }
    }
    state.insert(text);
  }

  // ----- Keyboard -----

  function onKeyDown(e: KeyboardEvent): void {
    if (composing.current || e.isComposing || e.keyCode === 229) return;
    // Bound commands first (⌘B, ⌥↑, …): one catalog serves these keys and the Command Menu.
    const command = catalog.forEvent(e, false);
    if (command) {
      revealCaret.current = true;
      catalog.run(command.id);
      e.preventDefault();
      e.stopPropagation();
      return;
    }
    const action = keyAction(e, isMac);
    if (!action) return;
    const shift = e.shiftKey;
    switch (action.kind) {
      case 'move':
        if (action.unit === 'doc') moveTo(action.dir > 0 ? doc.length : startOf(0, sel.caret), shift);
        else horizontal(action.dir, shift, action.unit);
        break;
      case 'vertical':
        vertical(action.dir, shift);
        break;
      case 'deleteBack':
        if (!isCollapsed(sel)) state.insert('');
        else if (action.unit === 'line') {
          apply(rangeEdit(doc.getLineStart(doc.getPosition(sel.caret).line), sel.caret));
        } else if (action.unit === 'word') apply(rangeEdit(wordLeft(doc, sel.caret), sel.caret));
        else if (!state.apply(backspacePairEdit(doc, sel))) state.buffer.backspace();
        revealCaret.current = true;
        break;
      case 'deleteForward':
        if (!isCollapsed(sel)) state.insert('');
        else if (action.unit === 'word') apply(rangeEdit(sel.caret, wordRight(doc, sel.caret)));
        else state.buffer.deleteForward();
        revealCaret.current = true;
        break;
      case 'enter':
        apply(action.soft ? softBreakEdit(sel) : enterEdit(doc, sel, mode, state.contextAt(sel.caret)));
        break;
      case 'tab':
        apply(
          action.outdent ? outdentEdit(doc, sel, state.indentUnit) : indentEdit(doc, sel, state.indentUnit),
        );
        break;
    }
    e.preventDefault();
    e.stopPropagation();
  }

  function rangeEdit(start: number, end: number) {
    return { start, length: end - start, text: '', anchor: start, caret: start };
  }

  // ----- Text input, IME and clipboard -----

  function onInput(e: Event): void {
    if (composing.current) return;
    const ta = e.currentTarget as HTMLTextAreaElement;
    const text = ta.value;
    ta.value = '';
    if (text) insertText(text.replace(/\r\n?/g, '\n'));
  }

  function onCompositionStart(): void {
    composing.current = true;
    inputRef.current?.classList.add('pn-composing');
  }

  function onCompositionEnd(e: CompositionEvent): void {
    composing.current = false;
    const ta = e.currentTarget as HTMLTextAreaElement;
    ta.classList.remove('pn-composing');
    const text = ta.value;
    ta.value = '';
    if (text) state.insert(text);
  }

  function onCopy(e: ClipboardEvent, cut: boolean): void {
    if (isCollapsed(sel)) return;
    e.preventDefault();
    e.clipboardData?.setData('text/plain', state.selectedText());
    if (cut) {
      revealCaret.current = true;
      state.insert('');
    }
  }

  function onPaste(e: ClipboardEvent): void {
    const pictures = pictureFiles(e.clipboardData);
    if (pictures.length > 0 && props.onPictures) {
      e.preventDefault();
      void insertPictures(pictures);
      return;
    }
    const pasted = e.clipboardData?.getData('text/plain');
    e.preventDefault();
    if (!pasted) return;
    const at = selectionStart(sel);
    const p = doc.getPosition(at);
    const text = stripDuplicateTaskMarker(pasted.replace(/\r\n?/g, '\n'), doc.getLineText(p.line), p.column);
    revealCaret.current = true;
    state.paste(text);
  }

  /** Save `files` and put a picture line for each at the caret, each on a line of its own (PRD §10.6). */
  async function insertPictures(files: readonly File[]): Promise<void> {
    const links = await props.onPictures!(files);
    if (links.length === 0) return;
    const now = state.selection.peek();
    const from = selectionStart(now);
    const to = Math.max(now.anchor, now.caret);
    const d = state.document;
    const start = d.getLineStart(d.getPosition(from).line);
    const endLine = d.getPosition(to).line;
    const end = d.getLineStart(endLine) + d.getLineText(endLine).length;
    const before = d.getText(start, from - start).trim() ? '\n' : '';
    const after = d.getText(to, end - to).trim() ? '\n' : '';
    revealCaret.current = true;
    state.paste(before + links.join('\n') + after);
  }

  function onDragOver(e: DragEvent): void {
    if (!props.onPictures || !e.dataTransfer?.types.includes('Files')) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = 'copy';
  }

  function onDrop(e: DragEvent): void {
    const pictures = pictureFiles(e.dataTransfer);
    if (!props.onPictures || !e.dataTransfer?.types.includes('Files')) return;
    e.preventDefault();
    if (pictures.length === 0) return;
    inputRef.current?.focus({ preventScroll: true });
    // The picture goes after the line it lands on, never splitting the line's text.
    const at = offsetAt(e.clientX, e.clientY);
    if (at !== null) {
      const line = doc.getPosition(at).line;
      const end = doc.getLineStart(line) + doc.getLineText(line).length;
      state.select(end, end);
    }
    void insertPictures(pictures);
  }

  // ----- Mouse -----

  function onContextMenu(e: MouseEvent): void {
    if (!props.onContextMenu) return;
    e.preventDefault();
    inputRef.current?.focus({ preventScroll: true });
    const picture = pictureAt(e.target as HTMLElement);
    if (picture) {
      props.onContextMenu({ x: e.clientX, y: e.clientY, picture });
      return;
    }
    // Like a text field: a right-click inside the selection keeps it, anywhere else moves the caret there.
    const offset = offsetAt(e.clientX, e.clientY);
    const from = selectionStart(sel);
    const to = Math.max(sel.anchor, sel.caret);
    if (offset !== null && (isCollapsed(sel) || offset < from || offset > to)) state.select(offset, offset);
    props.onContextMenu({ x: e.clientX, y: e.clientY });
  }

  /**
   * Dragging back to the left of a list item's bullet or checkbox: the selection takes in the whole item, as
   * it does on the lines it passed over, instead of stopping where the item's text starts.
   */
  function itemStartLeftOf(offset: number, x: number): number {
    const line = doc.getPosition(offset).line;
    if (mode !== 'live' || offset !== itemTextStart(line)) return offset;
    const body = lineEl(line)?.querySelector(':scope > .pn-li-body');
    return body && x < body.getBoundingClientRect().left ? doc.getLineStart(line) : offset;
  }

  /** The picture whose drawing `target` is in: the whole `![alt](url)` it was drawn from. */
  function pictureAt(target: HTMLElement): PictureAt | undefined {
    const img = target.closest<HTMLElement>('.pn-image');
    const lineAttr = img?.closest('[data-line]')?.getAttribute('data-line');
    if (!img || lineAttr == null) return undefined;
    const line = Number(lineAttr);
    const lineStart = doc.getLineStart(line);
    const tokens = state.model.lineTokens(line);
    const at = lineStart + Number(img.dataset.from);
    const urlToken = tokens.find((t) => t.type === 'imageUrl' && t.start === at);
    if (!urlToken || urlToken.node === 0) return undefined;
    const parts = tokens.filter((t) => t.node === urlToken.node);
    return {
      start: Math.min(...parts.map((t) => t.start)),
      end: Math.max(...parts.map((t) => t.start + t.length)),
      url: urlToken.meta ?? doc.getText(urlToken.start, urlToken.length),
    };
  }

  function offsetAt(x: number, y: number, dir: 1 | -1 = 1): number | null {
    const content = contentRef.current;
    if (!content) return null;
    const hit = hitTest(content, x, y, rowIndex.current.rows);
    if (!hit) return null;
    const start = doc.getLineStart(hit.line);
    return snap(start + Math.min(hit.col, lineContentEnd(doc, hit.line) - start), dir);
  }

  function onMouseDown(e: MouseEvent): void {
    if (e.button !== 0) return;
    // Control-click is macOS's right-click: onContextMenu handles it.
    if (isMac && e.ctrlKey && props.onContextMenu) {
      e.preventDefault();
      return;
    }
    const target = e.target as HTMLElement;
    e.preventDefault();
    inputRef.current?.focus({ preventScroll: true });

    const box = target.closest<HTMLElement>('.pn-checkbox');
    if (box) {
      const lineAttr = box.closest('[data-line]')?.getAttribute('data-line');
      if (lineAttr != null) {
        const line = Number(lineAttr);
        const p = parseLinePrefix(doc.getLineText(line));
        if (p.hasTask) {
          const e = setTaskStateEdit(doc, sel, line, toggledTaskState(p.task));
          state.apply(props.sortTasks ? withTaskSort(doc, e) : e);
        }
      }
      return;
    }
    const link = target.closest<HTMLElement>('[data-href]');
    if (link && (isMac ? e.metaKey : e.ctrlKey)) {
      const url = link.getAttribute('data-href') ?? '';
      if (isOpenableLink(url)) (props.openLink ?? defaultOpen)(url);
      return;
    }

    const offset = offsetAt(e.clientX, e.clientY);
    if (offset === null) return;
    goalX.current = null;
    let anchor = e.shiftKey ? sel.anchor : offset;
    let caret = offset;
    if (e.detail === 2) {
      const w = wordRangeAt(doc, offset);
      anchor = w.start;
      caret = w.end;
    } else if (e.detail >= 3) {
      const line = doc.getPosition(offset).line;
      anchor = doc.getLineStart(line);
      caret = line + 1 < doc.lineCount ? doc.getLineStart(line + 1) : doc.length;
    }
    state.select(anchor, caret);
    if (e.detail >= 2) return;

    const fixedAnchor = anchor;
    const onMove = (m: MouseEvent): void => {
      let to = offsetAt(m.clientX, m.clientY, m.clientX > e.clientX ? 1 : -1);
      if (to !== null && to < fixedAnchor) to = itemStartLeftOf(to, m.clientX);
      if (to !== null) {
        revealCaret.current = true;
        state.select(fixedAnchor, to);
      }
    };
    const onUp = (): void => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
    };
    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', onUp);
  }

  const empty = doc.length === 0;
  return (
    <div class={`pn-editor pn-mode-${mode}`} style={props.zoom ? { '--pn-z': props.zoom } : undefined}>
      <div
        class="pn-scroller"
        ref={scrollerRef}
        onMouseDown={onMouseDown}
        onContextMenu={onContextMenu}
        onDragOver={onDragOver}
        onDrop={onDrop}
        onScroll={onScrollerScroll}
      >
        <div class="pn-content" ref={contentRef} data-placeholder={empty ? props.placeholder : undefined}>
          {rows}
          <div class="pn-selection-layer" ref={selectionRef} aria-hidden="true" />
          <div class="pn-caret pn-blink" ref={caretRef} aria-hidden="true" />
          <textarea
            ref={inputRef}
            class="pn-input"
            aria-label="Markdown editor"
            aria-multiline="true"
            autocapitalize="off"
            autocomplete="off"
            spellcheck={false}
            onKeyDown={onKeyDown}
            onInput={onInput}
            onCompositionStart={onCompositionStart}
            onCompositionEnd={onCompositionEnd}
            onCopy={(e) => onCopy(e, false)}
            onCut={(e) => onCopy(e, true)}
            onPaste={onPaste}
          />
        </div>
      </div>
    </div>
  );
}
