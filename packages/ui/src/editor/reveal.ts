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
   * The line-prefix markers show. On most lines that is `lineActive`; on a task line, only when the selection
   * touches the marker's own characters (`- [ ]`), so editing the task's text keeps the checkbox.
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
  const prefixActive = taskPrefixTouched(tokens, selection) ?? true;

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
 * On a task line, whether the selection touches the marker from the bullet through `]` (not the space after
 * it). `null` on a line with no task marker.
 */
function taskPrefixTouched(tokens: readonly Token[], selection: Selection): boolean | null {
  const task = tokens.find((t) => t.type === 'taskMarker');
  if (!task) return null;
  const list = tokens.find((t) => t.type === 'listMarker');
  const start = list ? list.start : task.start;
  // The token runs on through the space after `]`; a caret there, before the task's text, doesn't count.
  const end = task.start + Math.min(3, task.length);
  return selectionIntersects(selection, start, end - start);
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
