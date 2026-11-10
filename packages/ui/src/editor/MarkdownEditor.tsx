import { stripDuplicateTaskMarker, toggledTaskState, parseLinePrefix } from '@puffinote/markdown';
import { isCollapsed, selectionEnd, selectionStart } from '@puffinote/text';
import { useLayoutEffect, useMemo, useRef } from 'preact/hooks';
import type { JSX } from 'preact';
import { CommandCatalog } from '../commands/command.ts';
import { blockAtLine } from './blocks.ts';
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
import { caretBox, hitTest, lineElements, lineSelectionRects } from './geometry.ts';
import { renderLine, snapOutOfConcealed } from './lineRender.ts';
import { BlockWidget, LineView } from './LineView.tsx';
import { lineContentEnd, wordLeft, wordRangeAt, wordRight } from './motion.ts';
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
const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform);

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
  const rows: JSX.Element[] = [];
  for (let i = 0; i < doc.lineCount; i++) {
    const block = blockAtLine(blocks, i);
    const blockRange = block
      ? { start: doc.getLineStart(block.startLine), end: doc.getLineEnd(block.endLine) }
      : null;
    if (block?.kind === 'table' && mode === 'live' && i === block.startLine) {
      const touched =
        blockRange !== null && selectionStart(sel) <= blockRange.end && selectionEnd(sel) >= blockRange.start;
      if (!touched) {
        rows.push(
          <BlockWidget
            key={`w${i}`}
            line={i}
            source={doc.getText(blockRange!.start, blockRange!.end - blockRange!.start)}
          />,
        );
        i = block.endLine;
        continue;
      }
    }
    const start = doc.getLineStart(i);
    const tokens = model.lineTokens(i);
    rows.push(
      <LineView
        key={i}
        index={i}
        text={doc.getLineText(i)}
        start={start}
        tokens={tokens}
        reveal={lineReveal(tokens, start, doc.getLineEnd(i), sel, blockRange)}
        mode={mode}
        block={block?.kind ?? null}
        resolveImage={resolveImage}
      />,
    );
  }

  // ----- Caret, selection and the input's position, after the DOM is up to date -----
  useLayoutEffect(() => {
    const content = contentRef.current;
    const caretEl = caretRef.current;
    const layer = selectionRef.current;
    const input = inputRef.current;
    if (!content || !caretEl || !layer || !input) return;
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

    // Selection highlight.
    layer.replaceChildren();
    if (!isCollapsed(sel)) {
      const a = doc.getPosition(selectionStart(sel));
      const b = doc.getPosition(selectionEnd(sel));
      for (let line = a.line; line <= b.line; line++) {
        const el = lineEl(line);
        if (!el || el.style.display === 'none') continue;
        const from = line === a.line ? a.column : 0;
        const to = line === b.line ? b.column : Number.MAX_SAFE_INTEGER;
        for (const r of lineSelectionRects(el, from, to, line < b.line)) {
          const div = document.createElement('div');
          div.className = 'pn-selection-rect';
          div.style.transform = `translate(${r.left - origin.left}px, ${r.top - origin.top}px)`;
          div.style.width = `${r.width}px`;
          div.style.height = `${r.height}px`;
          layer.appendChild(div);
        }
      }
    }
  });

  useLayoutEffect(() => {
    if (props.initialScrollTop && scrollerRef.current) scrollerRef.current.scrollTop = props.initialScrollTop;
  }, []);

  useLayoutEffect(() => {
    if (props.autoFocus) inputRef.current?.focus({ preventScroll: true });
  }, []);

  function lineEl(line: number): HTMLElement | null {
    return contentRef.current?.querySelector<HTMLElement>(`:scope > [data-line="${line}"]`) ?? null;
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
      const lines = lineElements(content);
      let i = lines.indexOf(current) + dir;
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
    const hit = hitTest(content, now.left + goalX.current, y);
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
        state.apply(edit);
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
    const mod = isMac ? e.metaKey : e.ctrlKey;
    const word = isMac ? e.altKey : e.ctrlKey;
    const shift = e.shiftKey;
    let handled = true;
    switch (e.key) {
      case 'ArrowLeft':
      case 'ArrowRight': {
        const dir = e.key === 'ArrowRight' ? 1 : -1;
        if (isMac && e.metaKey) horizontal(dir, shift, 'line');
        else horizontal(dir, shift, word ? 'word' : 'char');
        break;
      }
      case 'ArrowUp':
      case 'ArrowDown': {
        const dir = e.key === 'ArrowDown' ? 1 : -1;
        if (mod) moveTo(dir > 0 ? doc.length : 0, shift);
        else vertical(dir, shift);
        break;
      }
      case 'Home':
        horizontal(-1, shift, 'line');
        break;
      case 'End':
        horizontal(1, shift, 'line');
        break;
      case 'Backspace':
        if (!isCollapsed(sel)) state.insert('');
        else if (mod) apply(rangeEdit(doc.getLineStart(doc.getPosition(sel.caret).line), sel.caret));
        else if (word) apply(rangeEdit(wordLeft(doc, sel.caret), sel.caret));
        else if (!state.apply(backspacePairEdit(doc, sel))) state.buffer.backspace();
        revealCaret.current = true;
        break;
      case 'Delete':
        if (!isCollapsed(sel)) state.insert('');
        else if (word) apply(rangeEdit(sel.caret, wordRight(doc, sel.caret)));
        else state.buffer.deleteForward();
        revealCaret.current = true;
        break;
      case 'Enter':
        if (mod) handled = false;
        else apply(shift ? softBreakEdit(sel) : enterEdit(doc, sel, mode, state.contextAt(sel.caret)));
        break;
      case 'Tab':
        if (mod || e.altKey) handled = false;
        else apply(shift ? outdentEdit(doc, sel, state.indentUnit) : indentEdit(doc, sel, state.indentUnit));
        break;
      default:
        handled = false;
    }
    if (handled) {
      e.preventDefault();
      e.stopPropagation();
    }
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
    state.insert(text);
  }

  // ----- Mouse -----

  function offsetAt(x: number, y: number, dir: 1 | -1 = 1): number | null {
    const content = contentRef.current;
    if (!content) return null;
    const hit = hitTest(content, x, y);
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
      <div
        class="pn-scroller"
        ref={scrollerRef}
        onMouseDown={onMouseDown}
        onScroll={
          props.onScroll ? (e) => props.onScroll!((e.currentTarget as HTMLElement).scrollTop) : undefined
        }
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
