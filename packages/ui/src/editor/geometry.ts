/**
 * The offset ↔ visual map of the editing surface (PRD §7.4), read from the DOM the surface rendered. Each line
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

/** Where the caret draws for `col` on a line, in viewport coordinates. */
export function caretBox(lineEl: Element, col: number): CaretBox {
  const p = domPointIn(lineEl, col);
  const lineRect = lineEl.getBoundingClientRect();
  if (!p) {
    const style = getComputedStyle(lineEl);
    const lh = parseFloat(style.lineHeight) || lineRect.height || 20;
    return { left: lineRect.left + parseFloat(style.paddingLeft || '0'), top: lineRect.top, height: lh };
  }
  if ('el' in p) {
    const r = p.el.getBoundingClientRect();
    return { left: p.side === 'left' ? r.left : r.right, top: r.top, height: r.height };
  }
  const range = document.createRange();
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

/** The visible line element nearest to viewport `y`. */
export function nearestLine(root: Element, y: number): HTMLElement | null {
  let best: HTMLElement | null = null;
  let bestDist = Infinity;
  for (const el of lineElements(root)) {
    const r = el.getBoundingClientRect();
    if (r.height === 0) continue;
    const d = y < r.top ? r.top - y : y > r.bottom ? y - r.bottom : 0;
    if (d < bestDist) {
      bestDist = d;
      best = el;
      if (d === 0) break;
    }
  }
  return best;
}

/** A hit: a line and a column on it. */
export interface Hit {
  readonly line: number;
  readonly col: number;
}

/** Map a viewport point to a line and column, landing on the near side of a decoration. */
export function hitTest(root: Element, x: number, y: number): Hit | null {
  let lineEl = nearestLine(root, y);
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

  const pos = caretFromPoint(cx, cy);
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
    if (host === lineEl) {
      const kids = Array.from(lineEl.children);
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
