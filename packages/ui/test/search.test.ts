import { afterEach, describe, expect, it, vi } from 'vitest';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import { NotebookSearch, searchCachePath, type SearchSource } from '../src/notebook/search.ts';

/** A notebook of `files` whose listing gives each a size and time, counting reads. */
function source(files: Record<string, string>, open: Record<string, string> = {}, store?: MemoryStore) {
  const times = new Map(Object.keys(files).map((p) => [p, 1]));
  const reads: string[] = [];
  const src: SearchSource = {
    files: () => Object.keys(files).map((path) => ({ path })),
    stat: (path) => ({ size: files[path]?.length ?? 0, modified: times.get(path) ?? 0 }),
    read: async (path) => {
      reads.push(path);
      const text = files[path];
      if (text === undefined) throw new Error('gone');
      return { bytes: new TextEncoder().encode(text), version: '1' };
    },
    openTexts: () => new Map(Object.entries(open)),
    ...(store ? { store } : {}),
  };
  const edit = (path: string, text: string, time = 2) => {
    files[path] = text;
    times.set(path, time);
  };
  return { src, reads, edit, times };
}

afterEach(() => {
  vi.useRealTimers();
});

describe('NotebookSearch', () => {
  it('puts notes whose name matches first, then hits in the text, unsaved text included', async () => {
    const { src } = source(
      { 'a.md': 'nothing', 'Plan.md': 'x', 'b.md': 'on disk', 'c.png': 'plan' },
      { 'a.md': 'a plan' },
    );
    const hits = await new NotebookSearch(src).find('plan');
    expect(hits.map((h) => [h.path, h.inTitle ?? false])).toEqual([
      ['Plan.md', true],
      ['a.md', false],
    ]);
    expect(hits[1]).toMatchObject({ line: 0, offset: 2, length: 4, preview: 'a plan', matchStart: 2 });
  });

  it('matches every word at a word start, in any order, ignoring accents', async () => {
    const { src } = source({
      'a.md': 'The March budget',
      'b.md': 'budget only',
      'c.md': 'Résumé for marching',
      'd.md': 'unbudgeted march',
    });
    const search = new NotebookSearch(src);
    expect(new Set((await search.find('budget march')).map((h) => h.path))).toEqual(new Set(['a.md']));
    expect((await search.find('resume march')).map((h) => h.path)).toEqual(['c.md']);
  });

  it('finds a quoted phrase only where it appears whole', async () => {
    const { src } = source({ 'a.md': 'next year', 'b.md': 'year next' });
    expect((await new NotebookSearch(src).find('"next year"')).map((h) => h.path)).toEqual(['a.md']);
  });

  it('matches words split between a note name and its text', async () => {
    const { src } = source({ 'Budget.md': 'for march', 'other.md': 'march' });
    const hits = await new NotebookSearch(src).find('budget march');
    expect(hits.map((h) => [h.path, h.inTitle ?? false])).toEqual([
      ['Budget.md', true],
      ['Budget.md', false],
    ]);
  });

  it('ranks notes with more hits first, then the more recently edited', async () => {
    const { src, times } = source({ 'a.md': 'cat', 'b.md': 'cat\ncat\ncat', 'c.md': 'cat' });
    times.set('c.md', 5);
    const hits = await new NotebookSearch(src).find('cat');
    expect([...new Set(hits.map((h) => h.path))]).toEqual(['b.md', 'c.md', 'a.md']);
  });

  it('shows each line once, marking its first match', async () => {
    const { src } = source({ 'a.md': 'plan the plan\nplan' });
    const hits = await new NotebookSearch(src).find('plan');
    expect(hits.map((h) => [h.line, h.offset])).toEqual([
      [0, 0],
      [1, 14],
    ]);
  });

  it('reads a note again only when its size or time changes, and drops notes that went away', async () => {
    const { src, reads, edit } = source({ 'a.md': 'apple', 'b.md': 'banana' });
    const search = new NotebookSearch(src);
    await search.find('apple');
    expect(reads.sort()).toEqual(['a.md', 'b.md']);
    reads.length = 0;
    await search.find('banana');
    expect(reads).toEqual([]);
    edit('b.md', 'cherry');
    expect(await search.find('banana')).toEqual([]);
    expect(reads).toEqual(['b.md']);
  });

  it('puts back a note’s saved text once its tab closes', async () => {
    const open: Record<string, string> = { 'a.md': 'draft words' };
    const { src } = source({ 'a.md': 'saved words' }, open);
    const search = new NotebookSearch(src);
    expect(await search.find('draft')).toHaveLength(1);
    delete open['a.md'];
    expect(await search.find('draft')).toEqual([]);
    expect(await search.find('saved')).toHaveLength(1);
  });

  it('skips a note it cannot read, and answers nothing once aborted', async () => {
    const { src } = source({ 'a.md': 'word' });
    const search = new NotebookSearch(src);
    const abort = new AbortController();
    abort.abort();
    expect(await search.find('word', { signal: abort.signal })).toEqual([]);
    expect(await search.notesContaining('word', ['a.md', 'missing.md'])).toEqual(new Set(['a.md']));
  });

  it('saves the index in the notebook and starts from it next time', async () => {
    vi.useFakeTimers();
    const store = new MemoryStore('N', {});
    const first = source({ 'a.md': 'apple' }, {}, store);
    await new NotebookSearch(first.src).find('apple');
    await vi.runAllTimersAsync();
    expect(JSON.parse(new TextDecoder().decode((await store.read(searchCachePath)).bytes))).toEqual({
      format: 1,
      notes: [['a.md', 1, 5, 'apple']],
    });
    expect(new TextDecoder().decode((await store.read('.puffinote/cache/.gitignore')).bytes)).toBe('*\n');

    const again = source({ 'a.md': 'apple' }, {}, store);
    expect(await new NotebookSearch(again.src).find('apple')).toHaveLength(1);
    expect(again.reads).toEqual([]);
  });

  it('builds the index again from a saved one it cannot use', async () => {
    const store = new MemoryStore('N', { [searchCachePath]: '{"format": 99}' });
    const { src, reads } = source({ 'a.md': 'apple' }, {}, store);
    expect(await new NotebookSearch(src).find('apple')).toHaveLength(1);
    expect(reads).toEqual(['a.md']);
  });

  it('writes nothing once the notebook has closed', async () => {
    vi.useFakeTimers();
    const store = new MemoryStore('N', {});
    const { src } = source({ 'a.md': 'apple' }, {}, store);
    const search = new NotebookSearch({ ...src, closed: () => true });
    await search.find('apple');
    await vi.runAllTimersAsync();
    await expect(store.read(searchCachePath)).rejects.toThrow();
  });
});
