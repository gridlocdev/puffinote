import { TextDocument } from '@puffinote/text';
import { describe, expect, it } from 'vitest';
import { carryLineCache, lineTextOf, type LineCache } from '../src/editor/lineCache.ts';

function filled(doc: TextDocument): LineCache {
  const cache: LineCache = { doc, texts: [] };
  for (let i = 0; i < doc.lineCount; i++) lineTextOf(cache, i);
  return cache;
}

function expectMatches(cache: LineCache): void {
  const doc = cache.doc;
  for (let i = 0; i < doc.lineCount; i++) expect(lineTextOf(cache, i)).toBe(doc.getLineText(i));
}

describe('carryLineCache', () => {
  const base = TextDocument.fromString('zero\none\ntwo\nthree\nfour');

  it('keeps the texts of lines an edit did not touch, and re-reads only the edited lines', () => {
    const next = base.insert(base.getLineStart(2) + 1, 'X');
    const cache = carryLineCache(filled(base), next);
    expect(cache.texts.filter((t) => t !== undefined)).toEqual(['zero', 'one', 'three', 'four']);
    expectMatches(cache);
  });

  it('shifts the lines below an edit that adds or removes lines', () => {
    const added = base.insert(base.getLineStart(1), 'a\nb\n');
    const cache = carryLineCache(filled(base), added);
    expect(cache.texts[5]).toBe('three');
    expectMatches(cache);
    const removed = base.delete(base.getLineStart(1), base.getLineStart(3) - base.getLineStart(1));
    const shrunk = carryLineCache(filled(base), removed);
    expect(shrunk.texts[2]).toBe('four');
    expectMatches(shrunk);
  });

  it('starts over for a document it cannot relate to the cached one', () => {
    const other = TextDocument.fromString('zero\nother');
    const cache = carryLineCache(filled(base), other);
    expect(cache.texts).toEqual([]);
    expectMatches(cache);
  });

  it('agrees with the document after a run of random edits', () => {
    let doc = TextDocument.fromString('a\nbb\n\nccc\nd\ne\n');
    let cache = filled(doc);
    let seed = 7;
    const rand = (n: number) => (seed = (seed * 1103515245 + 12345) % 2147483648) % n;
    for (let k = 0; k < 300; k++) {
      const at = rand(doc.length + 1);
      const len = rand(Math.min(4, doc.length - at) + 1);
      const text = ['', 'x', '\n', 'y\nz', '\n\n'][rand(5)]!;
      doc = doc.replace(at, len, text);
      cache = carryLineCache(cache, doc);
      expectMatches(cache);
    }
  });
});
