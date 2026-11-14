import {
  findNestedDelimiters,
  innerStart,
  parseLinePrefix,
  spanEnd,
  taskStateFromMark,
  type TaskState,
  type Token,
  type TokenType,
} from '@puffinote/markdown';
import type { BlockKind } from './blocks.ts';
import type { LineReveal, ViewMode } from './reveal.ts';

/** A synthetic glyph drawn in place of a concealed marker; it owns the marker's columns atomically. */
export type DecorationKind = 'bullet' | 'number' | 'checkbox' | 'indent' | 'image' | 'rule';

export interface Decoration {
  readonly kind: DecorationKind;
  /** The bullet, number or rule text. */
  readonly text: string;
  readonly task?: TaskState;
  /** An image's source URL, as written. */
  readonly url?: string;
  /** An indent's width in columns (a tab counts 4). */
  readonly columns?: number;
}

/**
 * One run of a line's source, `[from, to)` in line columns. Segments tile the line's content in order. A
 * concealed segment draws nothing, or its decoration.
 */
export interface Segment {
  readonly from: number;
  readonly to: number;
  readonly className: string;
  readonly concealed: boolean;
  readonly deco: Decoration | null;
  /** The link target, for a link text or autolink run (hover shows it; ⌘-click opens it). */
  readonly href: string | null;
}

/** How one source line draws. */
export interface LineRender {
  readonly className: string;
  readonly segments: readonly Segment[];
  /** The whole line is hidden (a concealed fence or setext underline). */
  readonly collapsed: boolean;
  readonly headingLevel: number;
  readonly task: TaskState | null;
  /** Length of the line's content (without a trailing CR). */
  readonly length: number;
}

const contentClass: Partial<Record<TokenType, string>> = {
  emphasis: 'pn-em',
  strong: 'pn-strong',
  strongEmphasis: 'pn-strong pn-em',
  strikethrough: 'pn-strike',
  highlight: 'pn-mark',
  inlineCode: 'pn-code',
  autolink: 'pn-link',
  imageAlt: 'pn-image-alt',
  wikiLink: 'pn-link pn-wiki',
  embed: 'pn-link pn-embed',
  codeText: 'pn-codetext',
  frontMatterText: 'pn-fm',
  linkUrl: 'pn-url',
  imageUrl: 'pn-url',
};

const linePrefixTypes = new Set<TokenType>([
  'headingMarker',
  'blockquoteMarker',
  'listMarker',
  'taskMarker',
  'horizontalRule',
  'setextUnderline',
  'hardBreak',
]);

/**
 * Lay out one source line as styled segments, deciding which markers conceal (PRD §10.2):
 * inline markers by their node, line-prefix markers by the caret's line, block fences by their block.
 * `tokens` are line-relative (`MarkdownModel.lineTokensRelative`).
 */
export function renderLine(
  lineText: string,
  tokens: readonly Token[],
  reveal: LineReveal,
  mode: ViewMode,
  block: BlockKind | null,
  listDepth = 0,
): LineRender {
  const len = lineText.endsWith('\r') ? lineText.length - 1 : lineText.length;
  if (mode === 'raw') {
    return {
      className: 'pn-line pn-raw',
      segments: len > 0 ? [seg(0, len, '', false)] : [],
      collapsed: false,
      headingLevel: 0,
      task: null,
      length: len,
    };
  }

  const live = mode === 'live';
  const first = tokens[0];
  const headingLevel = first?.type === 'headingMarker' ? Number(first.meta ?? '0') : 0;
  const quoteDepth = first?.type === 'blockquoteMarker' ? Number(first.meta ?? '1') : 0;
  const taskToken = tokens.find((t) => t.type === 'taskMarker');
  const task = taskToken ? taskStateFromMark(taskToken.meta ?? ' ') : null;
  const listToken = tokens.find((t) => t.type === 'listMarker');

  const classes = ['pn-line'];
  if (len === 0) classes.push('pn-blank');
  if (headingLevel > 0) classes.push(`pn-h${headingLevel}`);
  if (quoteDepth > 0) classes.push('pn-quote', `pn-quote-${Math.min(quoteDepth, 3)}`);
  if (listToken) classes.push('pn-li');
  if (task) classes.push(`pn-task-${task}`);

  let collapsed = false;
  if (first?.type === 'codeFence') {
    classes.push('pn-fence');
    collapsed = live && !reveal.blockRevealed;
  } else if (first?.type === 'frontMatterFence') {
    classes.push('pn-fence', 'pn-fm-fence');
    collapsed = live && !reveal.blockRevealed;
  } else if (first?.type === 'codeText' || (block === 'fencedCode' && tokens.length === 0)) {
    classes.push('pn-codeblock');
  } else if (first?.type === 'frontMatterText' || (block === 'frontMatter' && tokens.length === 0)) {
    classes.push('pn-codeblock', 'pn-fmblock');
  } else if (first?.type === 'horizontalRule') {
    classes.push('pn-hr');
  } else if (first?.type === 'setextUnderline') {
    classes.push('pn-setext');
    collapsed = live && !reveal.lineActive;
  } else if (block === 'table') {
    classes.push('pn-tablerow');
  }

  // Nodes drawn as an image: all their tokens hide behind one picture until revealed.
  const imageNodes = new Set<number>();
  for (const t of tokens) if (t.type === 'imageUrl' && t.node !== 0) imageNodes.add(t.node);
  const linkSpans = linkTextSpans(tokens, lineText);

  const isConcealed = (t: Token): boolean => {
    if (!live) return false;
    if (t.node !== 0) {
      if (reveal.nodes.has(t.node)) return false;
      return t.role === 'marker' || imageNodes.has(t.node);
    }
    if (t.role !== 'marker') return false;
    if (linePrefixTypes.has(t.type)) return !reveal.prefixActive;
    if (t.type === 'codeFence' || t.type === 'frontMatterFence') return !reveal.blockRevealed;
    return false;
  };

  const segments: Segment[] = [];
  const done = task === 'done';
  const gapClass = (from: number, to: number): string => (inSpans(linkSpans, from, to) ? 'pn-link' : '');
  let pos = 0;
  const pushGap = (to: number): void => {
    if (to > pos) segments.push(seg(pos, to, gapClass(pos, to), false));
    pos = Math.max(pos, to);
  };

  for (const t of tokens) {
    const s = Math.max(pos, t.start);
    const e = Math.min(len, t.start + t.length);
    if (e <= s) continue;

    // A nested item's leading whitespace lays out as an indent step, not as narrow proportional spaces.
    if (
      t.type === 'listMarker' &&
      live &&
      !reveal.prefixActive &&
      s > pos &&
      /^[ \t]+$/.test(lineText.slice(pos, s))
    ) {
      segments.push(
        seg(pos, s, '', true, { kind: 'indent', text: '', columns: columnsOf(lineText.slice(pos, s)) }),
      );
      pos = s;
    }
    pushGap(s);

    const concealed = isConcealed(t);
    const markerClass = t.role === 'marker' ? `pn-mk pn-mk-${t.type}` : '';
    const base = [contentClass[t.type] ?? '', markerClass, inSpans(linkSpans, s, e) ? 'pn-link' : '']
      .filter(Boolean)
      .join(' ');

    if (concealed) {
      segments.push(
        seg(s, e, base, true, decorationFor(t, lineText.slice(s, e), task, imageNodes, listDepth)),
      );
    } else if (t.role === 'content' && isEmphasisLike(t.type)) {
      addNestedContent(segments, lineText, s, e, base, live && !reveal.nodes.has(t.node));
    } else {
      segments.push(seg(s, e, base, false));
    }
    pos = e;
  }
  pushGap(len);
  const linked = segments.map((s) => {
    const span = linkSpans.find((sp) => s.from >= sp.start && s.to <= sp.end);
    return span && !s.concealed ? { ...s, href: span.url } : s;
  });

  if (done) classes.push('pn-done');
  return { className: classes.join(' '), segments: linked, collapsed, headingLevel, task, length: len };
}

function seg(
  from: number,
  to: number,
  className: string,
  concealed: boolean,
  deco: Decoration | null = null,
): Segment {
  return { from, to, className, concealed, deco, href: null };
}

function isEmphasisLike(type: TokenType): boolean {
  return (
    type === 'strong' ||
    type === 'emphasis' ||
    type === 'strongEmphasis' ||
    type === 'strikethrough' ||
    type === 'highlight'
  );
}

/**
 * Emphasis content can wrap another construct (`***~~bold~~***`). Split it so the inner markers conceal with
 * the outer node and the inner text carries both styles.
 */
function addNestedContent(
  out: Segment[],
  lineText: string,
  s: number,
  e: number,
  base: string,
  conceal: boolean,
): void {
  const nested = findNestedDelimiters(lineText.slice(s, e));
  let p = 0;
  for (const n of nested) {
    const open = s + n.openStart;
    const inner = s + innerStart(n);
    const close = s + n.innerEnd;
    const end = s + spanEnd(n);
    if (open > s + p) out.push(seg(s + p, open, base, false));
    const extra = nestedClass(n.delim, n.markerLen);
    out.push(seg(open, inner, `${base} pn-mk`, conceal));
    out.push(seg(inner, close, `${base} ${extra}`, false));
    out.push(seg(close, end, `${base} pn-mk`, conceal));
    p = end - s;
  }
  if (s + p < e) out.push(seg(s + p, e, base, false));
}

function nestedClass(delim: string, markerLen: number): string {
  if (delim === '~') return 'pn-strike';
  if (delim === '=') return 'pn-mark';
  if (markerLen >= 3) return 'pn-strong pn-em';
  return markerLen === 2 ? 'pn-strong' : 'pn-em';
}

function decorationFor(
  t: Token,
  text: string,
  task: TaskState | null,
  imageNodes: ReadonlySet<number>,
  listDepth: number,
): Decoration | null {
  switch (t.type) {
    case 'listMarker': {
      const marker = text.trim();
      const m = /^(\d+)([.)])$/.exec(marker);
      if (m) return { kind: 'number', text: listOrdinal(Number(m[1]), listDepth) + m[2]! };
      return task ? null : { kind: 'bullet', text: bulletGlyphs[listDepth % bulletGlyphs.length]! };
    }
    case 'taskMarker':
      return { kind: 'checkbox', text: '', task: task ?? 'notStarted' };
    case 'horizontalRule':
      return { kind: 'rule', text: '' };
    case 'imageUrl':
      return imageNodes.has(t.node) ? { kind: 'image', text: '', url: t.meta ?? text } : null;
    default:
      return null;
  }
}

/** A bullet's glyph by nesting depth (PRD §10.2): a disc, then a circle, then a square, and round again. */
const bulletGlyphs = ['•', '◦', '▪'] as const;

/**
 * How an ordered item's number is drawn at nesting depth `depth` (PRD §10.2): 1, 2, 3 at the top level, then
 * a, b, c one level in, then i, ii, iii, and round again. The file keeps the digits.
 */
export function listOrdinal(n: number, depth: number): string {
  const style = depth % 3;
  if (style === 0 || n < 1) return String(n);
  if (style === 1) {
    let out = '';
    for (let k = n; k > 0; k = Math.floor((k - 1) / 26)) out = String.fromCharCode(97 + ((k - 1) % 26)) + out;
    return out;
  }
  if (n >= 4000) return String(n);
  const numerals: [number, string][] = [
    [1000, 'm'],
    [900, 'cm'],
    [500, 'd'],
    [400, 'cd'],
    [100, 'c'],
    [90, 'xc'],
    [50, 'l'],
    [40, 'xl'],
    [10, 'x'],
    [9, 'ix'],
    [5, 'v'],
    [4, 'iv'],
    [1, 'i'],
  ];
  let out = '';
  let k = n;
  for (const [value, glyph] of numerals) {
    while (k >= value) {
      out += glyph;
      k -= value;
    }
  }
  return out;
}

/**
 * Tracks list nesting down a document, one line at a time in order, so each list item knows how deep it sits
 * (for `listOrdinal`). An item nests under the open items whose marker starts left of its own; a non-blank
 * line that isn't a list item, at the left edge, ends every open list.
 */
export class ListDepthTracker {
  private readonly open: number[] = [];
  private quote = '';

  /** The depth of `lineText` (0 at the top level), or 0 for a line that isn't a list item. */
  next(lineText: string, isListItem: boolean): number {
    const p = parseLinePrefix(lineText);
    const quote = p.quote.replace(/[^>]/g, ''); // the quote depth; its spacing belongs to the indent
    if (quote !== this.quote) {
      this.open.length = 0;
      this.quote = quote;
    }
    if (!isListItem) {
      if (p.indent === 0 && lineText.slice(p.markerStart).trim().length > 0) this.open.length = 0;
      return 0;
    }
    while (this.open.length > 0 && this.open[this.open.length - 1]! >= p.markerStart) this.open.pop();
    const depth = this.open.length;
    this.open.push(p.markerStart);
    return depth;
  }

  /** Forget every open list (after a block the caller skipped, such as a table). */
  reset(): void {
    this.open.length = 0;
  }
}

function columnsOf(ws: string): number {
  let c = 0;
  for (const ch of ws) c += ch === '\t' ? 4 : 1;
  return c;
}

interface LinkSpan {
  readonly start: number;
  readonly end: number;
  readonly url: string;
}

/**
 * The linked runs on a line, line-relative: the text of each `[text](url)` (between the `[` and the `](`) and
 * each autolink.
 */
function linkTextSpans(tokens: readonly Token[], lineText: string): LinkSpan[] {
  const spans: LinkSpan[] = [];
  for (const u of tokens) {
    const uStart = u.start;
    if (u.type === 'autolink') {
      const url = u.meta ?? lineText.slice(uStart, uStart + u.length);
      spans.push({ start: uStart, end: uStart + u.length, url });
      continue;
    }
    if (u.type === 'wikiLink') {
      // A wiki link's href is `[[target]]`, so the shell can tell it from a path (PRD §10.3).
      const target = u.meta ?? lineText.slice(uStart, uStart + u.length);
      spans.push({ start: uStart, end: uStart + u.length, url: `[[${target}]]` });
      continue;
    }
    if (u.type !== 'linkUrl') continue;
    const url = u.meta ?? lineText.slice(uStart, uStart + u.length);
    let openStart = Number.MAX_SAFE_INTEGER;
    let openEnd = -1;
    let midStart = -1;
    for (const m of tokens) {
      if (m.node !== u.node || m.role !== 'marker' || m.type !== 'marker') continue;
      if (m.start < openStart) {
        openStart = m.start;
        openEnd = m.start + m.length;
      }
      if (m.start + m.length === u.start) midStart = m.start;
    }
    if (openEnd >= 0 && midStart > openEnd) {
      spans.push({ start: openEnd, end: midStart, url });
    }
  }
  return spans;
}

function inSpans(spans: readonly { start: number; end: number }[], from: number, to: number): boolean {
  return spans.some((sp) => from >= sp.start && to <= sp.end);
}

/** True when column `col` sits strictly inside a concealed segment, where the caret may not rest. */
export function isInsideConcealed(segments: readonly Segment[], col: number): boolean {
  return segments.some((s) => s.concealed && col > s.from && col < s.to);
}

/** Move `col` out of any concealed run, toward `dir` (+1 forward, -1 back). */
export function snapOutOfConcealed(segments: readonly Segment[], col: number, dir: 1 | -1): number {
  for (const s of segments) {
    if (s.concealed && col > s.from && col < s.to) return dir > 0 ? s.to : s.from;
  }
  return col;
}

const taskStateWords: Readonly<Record<TaskState, string>> = {
  notStarted: 'not done',
  inProgress: 'in progress',
  done: 'done',
};

/**
 * A task checkbox's accessible name: the task's text and its state ("Buy milk, not done"), so a screen
 * reader says which task a checkbox belongs to.
 */
export function checkboxLabel(lineText: string, task: TaskState): string {
  const text = lineText.replace(/^[\s>]*(?:[-*+]|\d+[.)])\s+\[.\]\s?/u, '').trim();
  return `${text || 'Empty task'}, ${taskStateWords[task]}`;
}

/** What the editor announces when the caret moves to another line: its number and its source text. */
export function lineAnnouncement(lineText: string, line: number): string {
  return `Line ${line + 1}: ${lineText.trim() ? lineText.trim() : 'blank'}`;
}
