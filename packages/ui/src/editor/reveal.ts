import type { Token } from '@puffinote/markdown';
import { isCollapsed, selectionIntersects, type Selection, type TextDocument } from '@puffinote/text';
import { isImagePath } from '../platform/paths.ts';

/**
 * How the editor draws Markdown:
 * - `live`: live preview; markers conceal until the caret or selection reaches them (PRD §10.2).
 * - `source`: every marker shows, still styled.
 * - `raw`: the literal source in one monospace style.
 */
export type ViewMode = 'live' | 'source' | 'raw';

/** What is revealed on one line, from the selection (PRD §10.2). */
export interface LineReveal {
  /** The caret or selection touches this line. */
  readonly lineActive: boolean;
  /**
   * The line-prefix markers show. On most lines that is `lineActive`; on a list or task line, only when the
   * selection touches the marker's own characters (`-`, `1.`, `- [ ]`), so editing the item's text keeps the
   * bullet, number or checkbox, and the text doesn't move.
   */
  readonly prefixActive: boolean;
  /** Inline nodes the selection touches: their markers show. */
  readonly nodes: ReadonlySet<number>;
  /** The selection touches the block this line belongs to: the block shows its raw source. */
  readonly blockRevealed: boolean;
}

const none: ReadonlySet<number> = new Set();

/**
 * The reveal state of a line spanning `[lineStart, lineEnd]`, from its line-relative tokens
 * (`MarkdownModel.lineTokensRelative`). Touching counts, so a caret just after `**b**`
 * reveals its markers, and a caret at the end of a line keeps that line's prefix visible. A picture is the
 * exception: it stays drawn wherever the selection is, and shows its link text only when it is the picture
 * at `shownPicture` (View Link Text, PRD §10.6) and the selection still touches it.
 */
export function lineReveal(
  tokens: readonly Token[],
  lineStart: number,
  lineEnd: number,
  selection: Selection,
  blockRange: { start: number; end: number } | null,
  shownPicture: number | null = null,
): LineReveal {
  const lineActive = selectionIntersects(selection, lineStart, lineEnd - lineStart);
  const blockRevealed =
    blockRange !== null &&
    selectionIntersects(selection, blockRange.start, blockRange.end - blockRange.start);
  // Shared objects for lines the caret isn't on, so a memoized line sees an unchanged prop.
  if (!lineActive) return blockRevealed ? blockOnlyReveal : concealedReveal;
  const prefixActive = listPrefixTouched(tokens, lineStart, selection) ?? true;

  // Each inline node's extent is the union of its tokens.
  const extents = new Map<number, { start: number; end: number }>();
  for (const t of tokens) {
    if (t.node === 0) continue;
    const e = extents.get(t.node);
    if (e) {
      e.start = Math.min(e.start, lineStart + t.start);
      e.end = Math.max(e.end, lineStart + t.start + t.length);
    } else {
      extents.set(t.node, { start: lineStart + t.start, end: lineStart + t.start + t.length });
    }
  }
  const pictures = new Set<number>();
  for (const t of tokens) if (isPictureToken(t)) pictures.add(t.node);
  const nodes = new Set<number>();
  for (const [node, e] of extents) {
    if (!selectionIntersects(selection, e.start, e.end - e.start)) continue;
    if (pictures.has(node) && (shownPicture === null || shownPicture < e.start || shownPicture >= e.end))
      continue;
    nodes.add(node);
  }
  if (nodes.size === 0) return activeReveals[(prefixActive ? 1 : 0) + (blockRevealed ? 2 : 0)]!;
  return { lineActive, prefixActive, nodes, blockRevealed };
}

/**
 * `prev` when it reveals the same as `next`, else `next`. The surface keeps each line's last reveal through
 * this, so a selection that moves without changing what a line shows (extending a select-all) leaves the
 * memoized line alone.
 */
export function keepReveal(prev: LineReveal | undefined, next: LineReveal): LineReveal {
  if (!prev || prev === next) return next;
  if (
    prev.lineActive !== next.lineActive ||
    prev.prefixActive !== next.prefixActive ||
    prev.blockRevealed !== next.blockRevealed ||
    prev.nodes.size !== next.nodes.size
  ) {
    return next;
  }
  for (const n of next.nodes) if (!prev.nodes.has(n)) return next;
  return prev;
}

/**
 * On a list or task line, whether the selection touches the marker's own characters: from the line start
 * through the bullet or number, or through `]` on a task (not the space after it). A range selection only
 * counts when one of its ends is inside the marker, so dragging across the item, or to the left of its
 * checkbox, keeps the checkbox drawn. `null` on a line that isn't a list item.
 */
function listPrefixTouched(
  tokens: readonly Token[],
  lineStart: number,
  selection: Selection,
): boolean | null {
  const task = tokens.find((t) => t.type === 'taskMarker');
  const list = tokens.find((t) => t.type === 'listMarker');
  if (!task && !list) return null;
  // The tokens run on through the space after the marker; a caret there, before the item's text, doesn't count.
  const end =
    lineStart + (task ? task.start + Math.min(3, task.length) : list!.start + Math.max(1, list!.length - 1));
  if (isCollapsed(selection)) return selectionIntersects(selection, lineStart, end - lineStart);
  const inside = (o: number) => o > lineStart && o <= end;
  return inside(selection.anchor) || inside(selection.caret);
}

/** A picture drawn in the note: its Markdown's range in the document, and its link as written. */
export interface PictureAt {
  readonly start: number;
  readonly end: number;
  /** The link as written, or `[[name]]` for a wiki embed (`pictureRef`). */
  readonly url: string;
}

/**
 * The target of a `![[target|size]]` embed, without its size or `#part`. Empty when it names nothing.
 */
export function embedTarget(meta: string | null | undefined): string {
  return (meta ?? '').split(/[|#]/)[0]!.trim();
}

/** The width an embed asks for (`![[a.png|300]]`, `![[a.png|300x200]]`), or null. */
export function embedWidth(meta: string | null | undefined): number | null {
  const m = /\|\s*(\d+)\s*(?:x\s*\d+\s*)?$/.exec(meta ?? '');
  return m ? Number(m[1]) : null;
}

/** Whether `t` is the token a picture is drawn from: an image's link, or a wiki embed of a picture file. */
export function isPictureToken(t: Token): boolean {
  if (t.node === 0) return false;
  return t.type === 'imageUrl' || (t.type === 'embed' && isImagePath(embedTarget(t.meta)));
}

/**
 * The reference a picture token stands for: its link as written, or `[[name]]` for a wiki embed, which has no
 * folder and is looked up by name (PRD §10.6).
 */
export function pictureRef(t: Token, doc: TextDocument): string {
  if (t.type === 'embed') return `[[${embedTarget(t.meta)}]]`;
  return t.meta ?? doc.getText(t.start, t.length);
}

/** The name in a `[[name]]` picture reference, or null for an ordinary link. */
export function embedRefName(ref: string): string | null {
  return ref.startsWith('[[') && ref.endsWith(']]') ? ref.slice(2, -2) : null;
}

/** The source range of the picture on this line that covers `offset`, or `null` when none does. */
export function pictureExtent(
  tokens: readonly Token[],
  offset: number,
): { start: number; end: number } | null {
  const url = tokens.find((t) => isPictureToken(t) && inNode(tokens, t.node, offset));
  return url ? nodeExtent(tokens, url.node) : null;
}

/**
 * The picture whose link starts at `urlStart` on this line: the whole `![alt](url)` and its link, or `null`
 * when no picture's link starts there.
 */
export function pictureWithUrlAt(
  tokens: readonly Token[],
  urlStart: number,
  doc: TextDocument,
): PictureAt | null {
  const url = tokens.find((t) => isPictureToken(t) && t.start === urlStart);
  if (!url) return null;
  return { ...nodeExtent(tokens, url.node), url: pictureRef(url, doc) };
}

function nodeExtent(tokens: readonly Token[], node: number): { start: number; end: number } {
  let start = Infinity;
  let end = -Infinity;
  for (const t of tokens) {
    if (t.node !== node) continue;
    start = Math.min(start, t.start);
    end = Math.max(end, t.start + t.length);
  }
  return { start, end };
}

function inNode(tokens: readonly Token[], node: number, offset: number): boolean {
  return tokens.some((t) => t.node === node && offset >= t.start && offset < t.start + t.length);
}

/** A line nothing reveals (the caret is elsewhere). */
export const concealedReveal: LineReveal = {
  lineActive: false,
  prefixActive: false,
  nodes: none,
  blockRevealed: false,
};

/** The reveals of an active line with no inline markers to show, by `prefixActive + 2 * blockRevealed`. */
const activeReveals: readonly LineReveal[] = [false, true, false, true].map((prefixActive, i) => ({
  lineActive: true,
  prefixActive,
  nodes: none,
  blockRevealed: i >= 2,
}));

/** A line inside a revealed block that the caret isn't on. */
export const blockOnlyReveal: LineReveal = {
  lineActive: false,
  prefixActive: false,
  nodes: none,
  blockRevealed: true,
};
