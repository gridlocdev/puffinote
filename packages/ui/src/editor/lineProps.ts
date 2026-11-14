import type { Token } from '@puffinote/markdown';
import type { BlockKind } from './blocks.ts';
import type { LineReveal, ViewMode } from './reveal.ts';

export interface LineViewProps {
  /** The line number. Not compared: the surface renumbers shifted rows' `data-line` itself. */
  readonly index: number;
  readonly text: string;
  readonly start: number;
  readonly tokens: readonly Token[];
  readonly reveal: LineReveal;
  readonly mode: ViewMode;
  readonly block: BlockKind | null;
  /** How deeply a list item is nested (0 at the top level), which picks how its number is drawn. */
  readonly listDepth: number;
  readonly resolveImage: (url: string) => string;
}

/**
 * True when two props draw the same line. Tokens carry absolute offsets and the surface hands each render
 * fresh arrays, so they compare line-relative; the line's start and number don't matter. That is what lets a
 * keystroke near the top of a long note skip every line below it.
 */
export function lineViewPropsEqual(a: LineViewProps, b: LineViewProps): boolean {
  if (a.text !== b.text || a.mode !== b.mode || a.block !== b.block || a.listDepth !== b.listDepth) {
    return false;
  }
  if (!revealEqual(a.reveal, b.reveal)) return false;
  if ((a.tokens !== b.tokens || a.start !== b.start) && !tokensEqual(a.tokens, a.start, b.tokens, b.start)) {
    return false;
  }
  return a.resolveImage === b.resolveImage || !b.tokens.some((t) => t.type === 'imageUrl');
}

function revealEqual(a: LineReveal, b: LineReveal): boolean {
  if (a === b) return true;
  if (
    a.lineActive !== b.lineActive ||
    a.prefixActive !== b.prefixActive ||
    a.blockRevealed !== b.blockRevealed
  )
    return false;
  if (a.nodes.size !== b.nodes.size) return false;
  for (const n of a.nodes) if (!b.nodes.has(n)) return false;
  return true;
}

function tokensEqual(a: readonly Token[], aStart: number, b: readonly Token[], bStart: number): boolean {
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) {
    const x = a[i]!;
    const y = b[i]!;
    if (
      x.start - aStart !== y.start - bStart ||
      x.length !== y.length ||
      x.type !== y.type ||
      x.role !== y.role ||
      x.node !== y.node ||
      x.meta !== y.meta
    ) {
      return false;
    }
  }
  return true;
}
