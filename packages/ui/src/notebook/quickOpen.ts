import { compact, inOrder } from '../commands/command.ts';
import { baseName } from '../platform/paths.ts';

/**
 * Quick Open's ranking (PRD §6.4): the name starting with the query, then containing it, then the path
 * containing it, then the name and then the path holding the query's letters in order. Ties go to the more
 * recently opened file, then to the path. With no query, recent files first, then the rest by path.
 */
export function rankFiles(paths: readonly string[], query: string, recent: readonly string[]): string[] {
  const q = compact(query);
  const recency = new Map(recent.map((p, i) => [p, i]));
  const rec = (p: string) => recency.get(p) ?? Number.MAX_SAFE_INTEGER;
  const scored: [string, number][] = [];
  for (const path of paths) {
    const name = compact(baseName(path));
    const full = compact(path);
    const score = !q
      ? 0
      : name.startsWith(q)
        ? 0
        : name.includes(q)
          ? 1
          : full.includes(q)
            ? 2
            : inOrder(q, name)
              ? 3
              : inOrder(q, full)
                ? 4
                : -1;
    if (score >= 0) scored.push([path, score]);
  }
  return scored
    .sort((a, b) => a[1] - b[1] || rec(a[0]) - rec(b[0]) || collator.compare(a[0], b[0]))
    .map(([p]) => p);
}

const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });
