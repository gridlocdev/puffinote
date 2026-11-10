import { stripDuplicateTaskMarker, toggledTaskState, parseLinePrefix } from '@puffinote/markdown';
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
  softBreakEdit,
  typeCharEdit,
} from './commands.ts';
import { markdownCommandSpecs } from './editorCommands.ts';
import type { EditorState } from './editorState.ts';
import { caretBox, firstRowEndingBelow, hitTest, lineSelectionRects } from './geometry.ts';
import { renderLine, snapOutOfConcealed } from './lineRender.ts';
import { BlockWidget, LineView } from './LineView.tsx';
import { lineContentEnd, wordLeft, wordRangeAt, wordRight } from './motion.ts';
import { keyAction } from './keys.ts';
import { lineReveal } from './reveal.ts';

export interface MarkdownEditorProps {
  readonly state: EditorState;
  /** Map an image reference to a URL the page can load (a notebook-relative path, say). */
  readonly resolveImage?: (url: string) => string;
  /** Open a link the user ⌘-clicked. Only http, https and mailto links reach it. */
  readonly openLink?: (url: string) => void;
  readonly autoFocus?: boolean;
  readonly placeholder?: string;
  /** Let the browser underline misspellings (PRD §10.3). */
  readonly spellcheck?: boolean;
  /** Where to scroll on mount (a tab coming back). */
  readonly initialScrollTop?: number;
  readonly onScroll?: (top: number) => void;
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
  const stateRef = useRef(state);
  stateRef.current = state;
  const catalog = useMemo(
    () =>
      new CommandCatalog(
        markdownCommandSpecs({ state: () => stateRef.current, focus: () => inputRef.current?.focus() }),
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
    const box = caretBox(el, p.column);
    if (el.classList.contains('pn-blank')) {
      // A blank line is shorter than a text line: centre a full-height caret on it.
      const lh = parseFloat(getComputedStyle(el).getPropertyValue('--pn-body-line')) || box.height;
      const r = el.getBoundingClientRect();
      return { left: box.left, top: r.top + (r.height - lh) / 2, height: lh };
    }
    return box;
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

  /** Step `offset` out of a concealed marker on its line, toward `dir` (concealed runs are atomic). */
  function snap(offset: number, dir: 1 | -1): number {
    const p = doc.getPosition(offset);
    const start = doc.getLineStart(p.line);
    const tokens = model.lineTokens(p.line);
    const r = renderLine(
      doc.getLineText(p.line),
      start,
      tokens,
      lineReveal(tokens, start, doc.getLineEnd(p.line), sel, null),
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
      to = dir > 0 ? lineContentEnd(doc, line) : doc.getLineStart(line);
    } else to = snap(dir > 0 ? state.buffer.nextCaret(at) : state.buffer.previousCaret(at), dir);
    moveTo(to, extend);
  }

  function vertical(dir: 1 | -1, extend: boolean): void {
    const content = contentRef.current;
    if (!content) return;
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
        moveTo(dir > 0 ? doc.length : 0, extend);
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
    moveTo(offset, extend, true);
  }

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
        if (action.unit === 'doc') moveTo(action.dir > 0 ? doc.length : 0, shift);
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
    const pasted = e.clipboardData?.getData('text/plain');
    e.preventDefault();
    if (!pasted) return;
    const at = selectionStart(sel);
    const p = doc.getPosition(at);
    const text = stripDuplicateTaskMarker(pasted.replace(/\r\n?/g, '\n'), doc.getLineText(p.line), p.column);
    revealCaret.current = true;
    state.paste(text);
  }

  // ----- Mouse -----

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
    const target = e.target as HTMLElement;
    e.preventDefault();
    inputRef.current?.focus({ preventScroll: true });

    const box = target.closest<HTMLElement>('.pn-checkbox');
    if (box) {
      const lineAttr = box.closest('[data-line]')?.getAttribute('data-line');
      if (lineAttr != null) {
        const line = Number(lineAttr);
        const p = parseLinePrefix(doc.getLineText(line));
        if (p.hasTask) state.apply(setTaskStateEdit(doc, sel, line, toggledTaskState(p.task)));
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
      const to = offsetAt(m.clientX, m.clientY, m.clientX > e.clientX ? 1 : -1);
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
    <div class={`pn-editor pn-mode-${mode}`}>
      <div class="pn-scroller" ref={scrollerRef} onMouseDown={onMouseDown} onScroll={onScrollerScroll}>
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
            spellcheck={props.spellcheck ?? false}
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
