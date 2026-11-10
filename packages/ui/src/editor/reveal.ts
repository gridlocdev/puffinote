import type { Token } from '@puffinote/markdown';
import { selectionIntersects, type Selection } from '@puffinote/text';

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
 * The reveal state of a line spanning `[lineStart, lineEnd]`. Touching counts, so a caret just after `**b**`
 * reveals its markers, and a caret at the end of a line keeps that line's prefix visible.
 */
export function lineReveal(
  tokens: readonly Token[],
  lineStart: number,
  lineEnd: number,
  selection: Selection,
  blockRange: { start: number; end: number } | null,
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
      e.start = Math.min(e.start, t.start);
      e.end = Math.max(e.end, t.start + t.length);
    } else {
      extents.set(t.node, { start: t.start, end: t.start + t.length });
    }
  }
  const nodes = new Set<number>();
  for (const [node, e] of extents) {
    if (selectionIntersects(selection, e.start, e.end - e.start)) nodes.add(node);
  }
  return { lineActive, prefixActive, nodes, blockRevealed };
}

/**
 * On a list or task line, whether the selection touches the marker's own characters: from the line start
 * through the bullet or number, or through `]` on a task (not the space after it). `null` on a line that isn't
 * a list item.
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
  const end = task ? task.start + Math.min(3, task.length) : list!.start + Math.max(1, list!.length - 1);
  return selectionIntersects(selection, lineStart, end - lineStart);
}

/** A line nothing reveals (the caret is elsewhere). */
export const concealedReveal: LineReveal = {
  lineActive: false,
  prefixActive: false,
  nodes: none,
  blockRevealed: false,
};

/** A line inside a revealed block that the caret isn't on. */
export const blockOnlyReveal: LineReveal = {
  lineActive: false,
  prefixActive: false,
  nodes: none,
  blockRevealed: true,
};
