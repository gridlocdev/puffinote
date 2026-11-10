/**
 * The offset ↔ visual map of the editing surface (PRD §10.2), read from the DOM the surface rendered. Each line
 * is a `[data-line]` element; each source run inside it a `[data-from][data-to]` span whose text node holds
 * exactly that run of the source, so a column maps to a text-node offset and back. Concealed runs have no text
 * and decorations are atomic: a point on one lands on its near side.
 */

export interface CaretBox {
  readonly left: number;
  readonly top: number;
  readonly height: number;
}

interface DomPoint {
  readonly node: Node;
  readonly offset: number;
}

function runsOf(lineEl: Element): HTMLElement[] {
  return Array.from(lineEl.querySelectorAll<HTMLElement>('[data-from]'));
}

function fromOf(el: Element): number {
  return Number(el.getAttribute('data-from'));
}

function toOf(el: Element): number {
  return Number(el.getAttribute('data-to'));
}

function isTextRun(el: HTMLElement): boolean {
  return !el.hasAttribute('data-deco') && el.firstChild?.nodeType === Node.TEXT_NODE;
}

/** The DOM position of column `col` in a line element, or null when the line draws no text. */
function domPointIn(
  lineEl: Element,
  col: number,
): DomPoint | { el: HTMLElement; side: 'left' | 'right' } | null {
  const runs = runsOf(lineEl);
  let lastText: HTMLElement | null = null;
  for (const run of runs) {
    const from = fromOf(run);
    const to = toOf(run);
    if (run.hasAttribute('data-deco')) {
      if (col === from && col < to) return { el: run, side: 'left' };
      if (col === to && from < to && !nextTextStartsAt(runs, run, col)) return { el: run, side: 'right' };
      continue;
    }
    if (!isTextRun(run)) continue;
    if (col >= from && col < to) return { node: run.firstChild!, offset: col - from };
    if (col === to) lastText = run;
    if (col < from) {
      if (lastText) break;
      return { node: run.firstChild!, offset: 0 }; // inside a concealed run ahead of this text
    }
  }
  if (lastText) return { node: lastText.firstChild!, offset: toOf(lastText) - fromOf(lastText) };
  // Past every text run: the end of the last one.
  for (let i = runs.length - 1; i >= 0; i--) {
    const run = runs[i]!;
    if (isTextRun(run)) return { node: run.firstChild!, offset: run.firstChild!.textContent!.length };
    if (run.hasAttribute('data-deco')) return { el: run, side: 'right' };
  }
  return null;
}

function nextTextStartsAt(runs: readonly HTMLElement[], after: HTMLElement, col: number): boolean {
  const i = runs.indexOf(after);
  for (let j = i + 1; j < runs.length; j++) {
    const r = runs[j]!;
    if (isTextRun(r)) return fromOf(r) === col;
    if (r.hasAttribute('data-deco')) return false;
  }
  return false;
}

/** DOM ranges over the drawn text of columns `[from, to)` of a line: one per text run they touch. */
export function textRanges(lineEl: Element, from: number, to: number): Range[] {
  const out: Range[] = [];
  for (const run of runsOf(lineEl)) {
    if (!isTextRun(run)) continue;
    const a = Math.max(from, fromOf(run));
    const b = Math.min(to, toOf(run));
    if (a >= b) continue;
    const range = document.createRange();
    range.setStart(run.firstChild!, a - fromOf(run));
    range.setEnd(run.firstChild!, b - fromOf(run));
    out.push(range);
  }
  return out;
}

/** Where the caret draws for `col` on a line, in viewport coordinates. */
export function caretBox(lineEl: Element, col: number): CaretBox {
  const p = domPointIn(lineEl, col);
  const lineRect = lineEl.getBoundingClientRect();
  if (!p) {
    // An empty line: a caret as tall as the text would be, centred on the line box.
    const style = getComputedStyle(lineEl);
    const lh = parseFloat(style.lineHeight) || lineRect.height || 20;
    const h = Math.min(lh, fontHeight(style) ?? lh);
    return {
      left: lineRect.left + parseFloat(style.paddingLeft || '0'),
      top: lineRect.top + parseFloat(style.paddingTop || '0') + (lh - h) / 2,
      height: h,
    };
  }
  if ('el' in p) {
    // Beside a decoration (a checkbox, a bullet): where the text before or after it would start, so past its
    // margin, and as tall as the line's text rather than the decoration.
    const r = p.el.getBoundingClientRect();
    const deco = getComputedStyle(p.el);
    const style = getComputedStyle(lineEl);
    const lh = parseFloat(style.lineHeight) || r.height;
    const h = Math.min(lh, fontHeight(style) ?? lh);
    const rowTop = r.top - (parseFloat(deco.marginTop) || 0);
    return {
      left:
        p.side === 'left'
          ? r.left - (parseFloat(deco.marginLeft) || 0)
          : r.right + (parseFloat(deco.marginRight) || 0),
      top: rowTop + (lh - h) / 2,
      height: h,
    };
  }
  const range = document.createRange();
  const text = p.node.textContent ?? '';
  if (p.offset < text.length) {
    // Measure the character after the caret: at a soft wrap a collapsed range measures the end of the row
    // above, but the caret belongs at the start of the row below (where typing would put the character).
    const code = text.charCodeAt(p.offset);
    const step = code >= 0xd800 && code <= 0xdbff && p.offset + 1 < text.length ? 2 : 1;
    range.setStart(p.node, p.offset);
    range.setEnd(p.node, p.offset + step);
    const r = firstRect(range);
    if (r && r.height > 0) return { left: r.left, top: r.top, height: r.height };
  }
  range.setStart(p.node, p.offset);
  range.setEnd(p.node, p.offset);
  let rect = firstRect(range);
  if (!rect || rect.height === 0) {
    // A collapsed range at a wrap point or line end can report nothing: measure the character before it.
    if (p.offset > 0) {
      range.setStart(p.node, p.offset - 1);
      const r = lastRect(range);
      if (r) rect = new DOMRect(r.right, r.top, 0, r.height);
    }
  }
  if (!rect) return { left: lineRect.left, top: lineRect.top, height: lineRect.height };
  return { left: rect.left, top: rect.top, height: rect.height };
}

const fontHeights = new Map<string, number | null>();
let measureCtx: CanvasRenderingContext2D | null | undefined;

/**
 * The height a text caret has in `style`'s font (its ascent plus descent), which is what a collapsed range
 * in a line of text measures. Cached per font; null where canvas text metrics are unavailable.
 */
export function fontHeight(style: CSSStyleDeclaration): number | null {
  const font = `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
  const cached = fontHeights.get(font);
  if (cached !== undefined) return cached;
  measureCtx ??= document.createElement('canvas').getContext('2d');
  let h: number | null = null;
  if (measureCtx) {
    measureCtx.font = font;
    const m = measureCtx.measureText('Hg');
    const sum = m.fontBoundingBoxAscent + m.fontBoundingBoxDescent;
    if (Number.isFinite(sum) && sum > 0) h = Math.round(sum);
  }
  fontHeights.set(font, h);
  return h;
}

function firstRect(range: Range): DOMRect | null {
  const rects = range.getClientRects();
  return rects.length > 0 ? rects[0]! : null;
}

function lastRect(range: Range): DOMRect | null {
  const rects = range.getClientRects();
  return rects.length > 0 ? rects[rects.length - 1]! : null;
}

/** Highlight rectangles for `[fromCol, toCol)` of a line, in viewport coordinates. */
export function lineSelectionRects(
  lineEl: Element,
  fromCol: number,
  toCol: number,
  toEnd: boolean,
): DOMRect[] {
  const out: DOMRect[] = [];
  const runs = runsOf(lineEl);
  const range = document.createRange();
  for (const run of runs) {
    const from = fromOf(run);
    const to = toOf(run);
    const a = Math.max(from, fromCol);
    const b = Math.min(to, toCol);
    if (b <= a && !(from === to)) continue;
    if (run.hasAttribute('data-deco')) {
      if (fromCol <= from && toCol >= to && from < to) out.push(run.getBoundingClientRect());
      continue;
    }
    if (!isTextRun(run) || b <= a) continue;
    range.setStart(run.firstChild!, a - from);
    range.setEnd(run.firstChild!, b - from);
    for (const r of Array.from(range.getClientRects())) out.push(r);
  }
  if (toEnd) {
    // The selection continues past this line: show the line break as a sliver.
    const end = caretBox(lineEl, Number(lineEl.getAttribute('data-length') ?? '0'));
    out.push(new DOMRect(end.left, end.top, 6, end.height));
  }
  return out;
}

/** The line elements in document order. */
export function lineElements(root: Element): HTMLElement[] {
  return Array.from(root.querySelectorAll<HTMLElement>(':scope > [data-line]'));
}

interface CaretPositionLike {
  readonly offsetNode: Node;
  readonly offset: number;
}

function caretFromPoint(x: number, y: number): CaretPositionLike | null {
  const doc = document as Document & {
    caretPositionFromPoint?: (x: number, y: number) => CaretPositionLike | null;
    caretRangeFromPoint?: (x: number, y: number) => Range | null;
  };
  if (doc.caretPositionFromPoint) return doc.caretPositionFromPoint(x, y);
  const r = doc.caretRangeFromPoint?.(x, y);
  return r ? { offsetNode: r.startContainer, offset: r.startOffset } : null;
}

/** Anything with a viewport rectangle: a line element, or a stand-in in tests. */
export interface RectSource {
  getBoundingClientRect(): { readonly top: number; readonly bottom: number; readonly height: number };
}

/**
 * The index of the first visible row (non-zero height) at or after `from` and before `to`, or `to` when
 * there is none. Collapsed rows (`display: none`) measure zero and take no part in the order.
 */
export function nextVisibleRow(rows: readonly RectSource[], from: number, to = rows.length): number {
  let i = from;
  while (i < to && rows[i]!.getBoundingClientRect().height === 0) i++;
  return i;
}

/**
 * The index of the first visible row whose bottom is at or below viewport `y`, or `rows.length` when every
 * row is above it. Rows are in document order, so their visible tops only grow: a binary search reads
 * O(log n) rectangles instead of measuring every row up to `y`.
 */
export function firstRowEndingBelow(rows: readonly RectSource[], y: number): number {
  let lo = 0;
  let hi = rows.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    const j = nextVisibleRow(rows, mid, hi);
    if (j === hi) {
      hi = mid; // [mid, hi) is all collapsed
      continue;
    }
    if (rows[j]!.getBoundingClientRect().bottom >= y) hi = j;
    else lo = j + 1;
  }
  return nextVisibleRow(rows, lo);
}

/** The index of the visible row nearest to viewport `y`, or -1 when no row is visible. */
export function nearestRowIndex(rows: readonly RectSource[], y: number): number {
  const after = firstRowEndingBelow(rows, y);
  let before = after - 1;
  while (before >= 0 && rows[before]!.getBoundingClientRect().height === 0) before--;
  if (after >= rows.length) return before;
  if (before < 0) return after;
  const a = rows[after]!.getBoundingClientRect();
  const b = rows[before]!.getBoundingClientRect();
  const dAfter = y < a.top ? a.top - y : 0;
  const dBefore = y - b.bottom;
  return dBefore <= dAfter ? before : after;
}

/** The visible line element nearest to viewport `y`. Pass `rows` when the caller already has them. */
export function nearestLine(
  root: Element,
  y: number,
  rows: readonly HTMLElement[] = lineElements(root),
): HTMLElement | null {
  const i = nearestRowIndex(rows, y);
  return i >= 0 ? rows[i]! : null;
}

/** A hit: a line and a column on it. */
export interface Hit {
  readonly line: number;
  readonly col: number;
}

/** Map a viewport point to a line and column, landing on the near side of a decoration. */
export function hitTest(
  root: Element,
  x: number,
  y: number,
  rows: readonly HTMLElement[] = lineElements(root),
): Hit | null {
  let lineEl = nearestLine(root, y, rows);
  if (!lineEl) return null;
  const lr = lineEl.getBoundingClientRect();
  const cy = Math.min(Math.max(y, lr.top + 1), lr.bottom - 1);
  const cx = Math.min(Math.max(x, lr.left), lr.right - 1);
  const line = Number(lineEl.getAttribute('data-line'));
  const length = Number(lineEl.getAttribute('data-length') ?? '0');

  // A widget (a table, an image block) maps to its first line.
  if (lineEl.hasAttribute('data-widget')) return { line, col: 0 };

  const target = document.elementFromPoint(cx, cy);
  const deco = target?.closest<HTMLElement>('[data-deco]');
  if (deco && lineEl.contains(deco)) {
    const r = deco.getBoundingClientRect();
    return { line, col: x < r.left + r.width / 2 ? fromOf(deco) : toOf(deco) };
  }

  // Under a list item's marker (not on its shown text), the row is the text column's row beside it (PRD §10.2).
  const itemBody = lineEl.querySelector(':scope > .pn-li-body');
  const onMarker =
    !!target && lineEl.contains(target) && !!target.closest('[data-from]') && !itemBody?.contains(target);
  const px = itemBody && !onMarker ? Math.max(cx, itemBody.getBoundingClientRect().left + 1) : cx;
  const pos = caretFromPoint(px, cy);
  if (pos) {
    const host =
      pos.offsetNode.nodeType === Node.TEXT_NODE ? pos.offsetNode.parentElement : (pos.offsetNode as Element);
    const run = host?.closest<HTMLElement>('[data-from]');
    if (
      run &&
      lineEl.contains(run) &&
      !run.hasAttribute('data-deco') &&
      pos.offsetNode.nodeType === Node.TEXT_NODE
    ) {
      return { line, col: Math.min(fromOf(run) + pos.offset, toOf(run)) };
    }
    const body = host?.classList.contains('pn-li-body') && host.parentElement === lineEl;
    if (host === lineEl || body) {
      const kids = Array.from(host!.children);
      const kid = kids[pos.offset];
      if (kid?.hasAttribute('data-from')) return { line, col: fromOf(kid) };
    }
  }
  lineEl = lineEl as HTMLElement;
  return { line, col: x <= lr.left + 4 ? firstVisibleCol(lineEl) : length };
}

function firstVisibleCol(lineEl: Element): number {
  for (const run of runsOf(lineEl)) {
    if (isTextRun(run) || run.hasAttribute('data-deco')) return fromOf(run);
  }
  return 0;
}
