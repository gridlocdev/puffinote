/** One search hit: the range `[start, start + length)`. */
export interface SearchMatch {
  readonly start: number;
  readonly length: number;
}

export interface SearchOptions {
  readonly regex: boolean;
  readonly caseSensitive: boolean;
}

/**
 * Find all non-overlapping matches of `query` in `text`, as plain text or as a JavaScript
 * regular expression. An invalid pattern finds nothing rather than throwing, and zero-width
 * regex matches are skipped so stepping through matches can't get stuck in one place.
 */
export function findAll(text: string, query: string, options: SearchOptions): SearchMatch[] {
  if (!query || !text) return [];
  if (!options.regex && options.caseSensitive) return findPlain(text, query);
  const pattern = options.regex ? query : escapeRegExp(query);
  let re: RegExp;
  try {
    re = new RegExp(pattern, options.caseSensitive ? 'g' : 'gi');
  } catch {
    return [];
  }
  const results: SearchMatch[] = [];
  for (const m of text.matchAll(re)) {
    if (m[0].length > 0) results.push({ start: m.index, length: m[0].length });
  }
  return results;
}

/** True when `query` is a valid regular expression. */
export function isValidRegex(query: string): boolean {
  try {
    new RegExp(query);
    return true;
  } catch {
    return false;
  }
}

export function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function findPlain(text: string, query: string): SearchMatch[] {
  const results: SearchMatch[] = [];
  for (let i = text.indexOf(query); i !== -1; i = text.indexOf(query, i + query.length)) {
    results.push({ start: i, length: query.length });
  }
  return results;
}

/**
 * What the find bar says about where it has got to: "3 of 17", "No results", or nothing while
 * there is no query. An empty query hasn't failed to find anything yet, so it says nothing.
 * `current` is 0-based, or -1 for none.
 */
export function describeSearchStatus(
  current: number,
  total: number,
  query: string | null | undefined,
): string {
  if (!query) return '';
  return total === 0 ? 'No results' : `${Math.max(0, current) + 1} of ${total}`;
}
