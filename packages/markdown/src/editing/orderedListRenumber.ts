import { parseLinePrefix, type LinePrefix } from './linePrefix.ts';

/** The renumbered slice `[startLine, endLineExclusive)` and its rewritten lines. */
export interface RenumberResult {
  readonly startLine: number;
  readonly endLineExclusive: number;
  readonly lines: readonly string[];
}

/**
 * Renumber the run of same-level ordered items starting at `startLine`, sequentially from `from` (by default
 * that item's own number), so inserting or removing an item keeps the numbering 1, 2, 3 (PRD §10.1). The run ends at the first
 * line that isn't an ordered item at the same indent and quote depth, a line nested under one, or blank lines
 * followed by one of those. Returns null when `startLine` isn't an
 * ordered item or nothing needs changing.
 */
export function renumberOrderedList(
  lines: readonly string[],
  startLine: number,
  from?: number,
): RenumberResult | null {
  const firstLine = lines[startLine];
  if (firstLine === undefined) return null;
  const first = parseLinePrefix(firstLine);
  if (first.list !== 'ordered') return null;

  const rewritten: string[] = [];
  let changed = false;
  let expected = from ?? first.number;
  let line = startLine;
  for (; line < lines.length; line++) {
    const original = lines[line]!;
    if (isBlankWithin(original, first)) {
      // Blank lines between items keep one list, as the export renders it: carry on when the next
      // non-blank line is another item of this run or nested under one; otherwise the list ends here.
      let next = line + 1;
      while (next < lines.length && isBlankWithin(lines[next]!, first)) next++;
      const after = lines[next];
      if (after === undefined) break;
      const q = parseLinePrefix(after);
      const continues =
        nestedUnder(q, first, after) ||
        (q.list === 'ordered' && q.indent === first.indent && q.quote === first.quote);
      if (!continues) break;
      for (; line < next; line++) rewritten.push(lines[line]!);
      line--; // the for loop's increment lands on `next`
      continue;
    }
    const p = parseLinePrefix(original);
    if (nestedUnder(p, first, original)) {
      rewritten.push(original); // a line nested under the item above: the run carries on past it
      continue;
    }
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

/**
 * Whether `line` is blank inside `item`'s container: empty or whitespace, or, in a quote, only the quote's
 * own `>` marks (`>` between `> 1. a` and `> 2. b`).
 */
function isBlankWithin(line: string, item: LinePrefix): boolean {
  const p = parseLinePrefix(line);
  if (line.slice(p.markerStart).trim().length > 0) return false;
  return p.quote.replace(/[ \t]/g, '') === item.quote.replace(/[ \t]/g, '');
}

/** Whether `line` (parsed as `p`) is non-blank and indented past the marker of `item`, so nested inside it. */
export function nestedUnder(p: LinePrefix, item: LinePrefix, line: string): boolean {
  return (
    p.quote === item.quote && p.markerStart > item.markerStart && line.slice(p.markerStart).trim().length > 0
  );
}
