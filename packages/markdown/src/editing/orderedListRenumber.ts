import { parseLinePrefix } from './linePrefix.ts';

/** The renumbered slice `[startLine, endLineExclusive)` and its rewritten lines. */
export interface RenumberResult {
  readonly startLine: number;
  readonly endLineExclusive: number;
  readonly lines: readonly string[];
}

/**
 * Renumber the run of same-level ordered items starting at `startLine`, sequentially from that item's own
 * number, so inserting or removing an item keeps the numbering 1, 2, 3 (PRD §10.1). The run ends at the first
 * line that isn't an ordered item at the same indent and quote depth. Returns null when `startLine` isn't an
 * ordered item or nothing needs changing.
 */
export function renumberOrderedList(lines: readonly string[], startLine: number): RenumberResult | null {
  const firstLine = lines[startLine];
  if (firstLine === undefined) return null;
  const first = parseLinePrefix(firstLine);
  if (first.list !== 'ordered') return null;

  const rewritten: string[] = [];
  let changed = false;
  let expected = first.number;
  let line = startLine;
  for (; line < lines.length; line++) {
    const original = lines[line]!;
    const p = parseLinePrefix(original);
    if (p.list !== 'ordered' || p.indent !== first.indent || p.quote !== first.quote) break;
    if (p.number !== expected) {
      rewritten.push(
        original.slice(0, p.markerStart) + String(expected) + original.slice(p.markerStart + p.digitCount),
      );
      changed = true;
    } else {
      rewritten.push(original);
    }
    expected++;
  }
  return changed ? { startLine, endLineExclusive: line, lines: rewritten } : null;
}
