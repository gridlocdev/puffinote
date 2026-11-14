import { describe, expect, it } from 'vitest';
import type { SearchHit } from '../src/notebook/notebook.ts';
import { runSearch, type SearchResult } from '../src/shell/panels.tsx';

const hit: SearchHit = { path: 'a.md', line: 0, offset: 0, length: 1, preview: 'a', matchStart: 0 };

describe('the Search panel', () => {
  it('reports the hits of a search, tagged with its query', async () => {
    const got: SearchResult[] = [];
    await runSearch(
      async () => [hit],
      'a',
      new AbortController().signal,
      (r) => got.push(r),
    );
    expect(got).toEqual([{ query: 'a', hits: [hit] }]);
  });

  it('reports a failed search instead of searching forever', async () => {
    const got: SearchResult[] = [];
    const fail = () => Promise.reject(new Error('The folder is gone'));
    await runSearch(fail, 'a', new AbortController().signal, (r) => got.push(r));
    expect(got).toEqual([{ query: 'a', error: 'The folder is gone' }]);
  });

  it('says nothing for a search a newer query replaced', async () => {
    const got: SearchResult[] = [];
    const abort = new AbortController();
    const search = () => {
      abort.abort();
      return Promise.reject(new DOMException('Aborted', 'AbortError'));
    };
    await runSearch(search, 'a', abort.signal, (r) => got.push(r));
    expect(got).toEqual([]);
  });
});
