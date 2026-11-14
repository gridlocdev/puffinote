import type { TextDocument } from '@puffinote/text';

/**
 * The line texts the editor has read from one document snapshot, filled in as lines are drawn. Reading a line
 * out of the piece table costs more than comparing it, so the cache is carried across edits and only the
 * edited lines are read again.
 */
export interface LineCache {
  readonly doc: TextDocument;
  /** The text of each line read so far; a hole is a line not read yet. */
  readonly texts: (string | undefined)[];
}

/** The text of `line`, read from the document the first time. */
export function lineTextOf(cache: LineCache, line: number): string {
  return (cache.texts[line] ??= cache.doc.getLineText(line));
}

/**
 * The cache for `doc`, carried over from `cache`: when `doc` was made from the cached document by one edit,
 * the lines above and below the edit keep their texts (shifted by the lines it added or removed) and the
 * edited lines are left to be read again. Any other document starts an empty cache.
 */
export function carryLineCache(cache: LineCache, doc: TextDocument): LineCache {
  if (cache.doc === doc) return cache;
  const change = doc.changeSince(cache.doc);
  if (!change) return { doc, texts: [] };
  const old = cache.doc;
  const first = old.getPosition(change.start).line;
  const lastOld = old.getPosition(change.start + change.removedLength).line;
  const delta = doc.lineCount - old.lineCount;
  const prev = cache.texts;
  const texts = new Array<string | undefined>(doc.lineCount);
  for (let i = 0; i < first && i < prev.length; i++) texts[i] = prev[i];
  for (let i = lastOld + 1; i < prev.length; i++) texts[i + delta] = prev[i];
  return { doc, texts };
}
