import type { Block, Token } from '@puffinote/markdown';
import { selectionEnd, selectionStart, type Selection, type TextDocument } from '@puffinote/text';
import { ListDepthTracker } from './lineRender.ts';
import type { ViewMode } from './reveal.ts';

/** One row the editor draws: a source line, or a block drawn whole in place of its lines. */
export type Row = LineRow | WidgetRow;

export interface LineRow {
  readonly kind: 'line';
  readonly line: number;
  /** The multi-line block the line is in, if any. */
  readonly block: Block | null;
  /** That block's offsets, `[start, end]`, which reveal as one unit; null outside a block. */
  readonly range: { readonly start: number; readonly end: number } | null;
  /** How deep the line's list item sits (0 for a line that isn't one). */
  readonly listDepth: number;
}

export interface WidgetRow {
  readonly kind: 'widget';
  /** The block's first line, which the row stands for. */
  readonly line: number;
  readonly block: Block;
  /** The block's source text. */
  readonly source: string;
}

/**
 * The rows the editor draws for a note, in order. In the live view a table the selection doesn't touch is
 * one widget row; every other line is a row of its own. `lineText` and `tokensOf` give a line's text and
 * line-relative tokens, so the caller can serve them from its caches.
 */
export function buildRows(
  doc: TextDocument,
  blocks: readonly Block[],
  mode: ViewMode,
  sel: Selection,
  lineText: (line: number) => string,
  tokensOf: (line: number) => readonly Token[],
): Row[] {
  const rows: Row[] = [];
  const depths = new ListDepthTracker();
  let bi = 0;
  let range: { start: number; end: number } | null = null;
  let rangeOf: Block | null = null;
  for (let i = 0; i < doc.lineCount; i++) {
    while (bi < blocks.length && blocks[bi]!.endLine < i) bi++;
    const block = bi < blocks.length && blocks[bi]!.startLine <= i ? blocks[bi]! : null;
    if (block && block !== rangeOf) {
      rangeOf = block;
      range = { start: doc.getLineStart(block.startLine), end: doc.getLineEnd(block.endLine) };
    }
    if (block && range && block.kind === 'table' && mode === 'live' && i === block.startLine) {
      const touched = selectionStart(sel) <= range.end && selectionEnd(sel) >= range.start;
      if (!touched) {
        rows.push({
          kind: 'widget',
          line: i,
          block,
          source: doc.getText(range.start, range.end - range.start),
        });
        depths.skipBlock();
        i = block.endLine;
        continue;
      }
    }
    const listDepth = depths.next(lineText(i), tokensOf(i), block?.kind ?? null);
    rows.push({ kind: 'line', line: i, block, range: block ? range : null, listDepth });
  }
  return rows;
}
