import type { MarkdownModel } from '@puffinote/markdown';
import type { TextDocument } from '@puffinote/text';

/** A multi-line construct that conceals or reveals as one unit (PRD §10.2). */
export type BlockKind = 'fencedCode' | 'frontMatter' | 'table';

export interface Block {
  readonly kind: BlockKind;
  readonly startLine: number;
  /** Inclusive. */
  readonly endLine: number;
}

/** The document's multi-line blocks in order, found from the tokens the model already produced. */
export function findBlocks(doc: TextDocument, model: MarkdownModel): Block[] {
  return scanBlocks(doc, model, 0, []);
}

/**
 * The blocks after an edit, reusing every block of `previous` that ends well above `dirtyFromLine` (the first
 * line the model re-tokenized). Lines and states above that line are unchanged, and a block can only grow
 * downward by one line (a table body row), so a block ending two or more lines above it is still exact. The
 * scan resumes right after the last kept block, which is a position the full scan also starts from.
 */
export function updateBlocks(
  previous: readonly Block[],
  doc: TextDocument,
  model: MarkdownModel,
  dirtyFromLine: number,
): Block[] {
  let keep = 0;
  while (keep < previous.length && previous[keep]!.endLine + 1 < dirtyFromLine) keep++;
  const kept = previous.slice(0, keep);
  const from = keep > 0 ? kept[keep - 1]!.endLine + 1 : 0;
  return scanBlocks(doc, model, from, kept);
}

function scanBlocks(doc: TextDocument, model: MarkdownModel, from: number, blocks: Block[]): Block[] {
  const n = Math.min(model.lineCount, doc.lineCount);
  let i = from;
  while (i < n) {
    const first = model.lineTokensRelative(i)[0];
    if (first?.type === 'frontMatterFence' && i === 0) {
      let end = 1;
      while (end < n && model.lineTokensRelative(end)[0]?.type !== 'frontMatterFence') end++;
      blocks.push({ kind: 'frontMatter', startLine: 0, endLine: Math.min(end, n - 1) });
      i = end + 1;
      continue;
    }
    if (first?.type === 'codeFence' && model.enteringState(i).kind !== 'fencedCode') {
      let end = i + 1;
      while (end < n && !(model.enteringState(end).kind === 'fencedCode' && isFenceLine(model, end))) end++;
      blocks.push({ kind: 'fencedCode', startLine: i, endLine: Math.min(end, n - 1) });
      i = end + 1;
      continue;
    }
    if (i + 1 < n && model.lineTokensRelative(i + 1)[0]?.type === 'tableDelimiter') {
      let end = i + 2;
      while (end < n && isTableBodyRow(doc, model, end)) end++;
      blocks.push({ kind: 'table', startLine: i, endLine: end - 1 });
      i = end;
      continue;
    }
    i++;
  }
  return blocks;
}

function isFenceLine(model: MarkdownModel, line: number): boolean {
  return model.lineTokensRelative(line)[0]?.type === 'codeFence';
}

function isTableBodyRow(doc: TextDocument, model: MarkdownModel, line: number): boolean {
  return model.enteringState(line).kind === 'normal' && /(^|[^\\])\|/.test(doc.getLineText(line));
}

/** The block covering `line`, if any. `blocks` is in document order. */
export function blockAtLine(blocks: readonly Block[], line: number): Block | null {
  let lo = 0;
  let hi = blocks.length - 1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    const b = blocks[mid]!;
    if (line < b.startLine) hi = mid - 1;
    else if (line > b.endLine) lo = mid + 1;
    else return b;
  }
  return null;
}
