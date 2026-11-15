import { describe, expect, it } from 'vitest';
import { NotebookSearch, type SearchSource } from '../src/notebook/search.ts';

function source(files: Record<string, string>, open: Record<string, string> = {}): SearchSource {
  return {
    files: () => Object.keys(files).map((path) => ({ path })),
    read: async (path) => {
      const text = files[path];
      if (text === undefined) throw new Error('gone');
      return { bytes: new TextEncoder().encode(text), version: '1' };
    },
    openTexts: () => new Map(Object.entries(open)),
  };
}

describe('NotebookSearch', () => {
  it('puts notes whose name matches first, then hits in the text, unsaved text included', async () => {
    const search = new NotebookSearch(
      source({ 'a.md': 'nothing', 'Plan.md': 'x', 'b.md': 'on disk', 'c.png': 'plan' }, { 'a.md': 'a plan' }),
    );
    const hits = await search.find('plan');
    expect(hits.map((h) => [h.path, h.inTitle ?? false])).toEqual([
      ['Plan.md', true],
      ['a.md', false],
    ]);
    expect(hits[1]).toMatchObject({ line: 0, offset: 2, length: 4, preview: 'a plan', matchStart: 2 });
  });

  it('skips a note it cannot read, and stops when aborted', async () => {
    const search = new NotebookSearch(source({ 'a.md': 'word' }));
    const abort = new AbortController();
    abort.abort();
    expect(await search.find('word', { signal: abort.signal })).toEqual([]);
    expect(await search.notesContaining('word', ['a.md', 'missing.md'])).toEqual(new Set(['a.md']));
  });
});
