import { describe, expect, it } from 'vitest';
import {
  countWords,
  foldString,
  foldWithOffsets,
  parseWordQuery,
  WordIndex,
  wordMatches,
} from '../src/index.ts';

const marked = (text: string, query: string, partial = false) =>
  wordMatches(text, parseWordQuery(query), { partial }).map((m) => text.slice(m.start, m.start + m.length));

describe('folding', () => {
  it('lowers case and takes accents off', () => {
    expect(foldString('Café ÉTÉ Ångström')).toBe('cafe ete angstrom');
  });

  it('maps folded offsets back to the original text', () => {
    const text = 'éclair Œuf';
    const f = foldWithOffsets(text);
    expect(f.text).toBe('eclair œuf');
    expect(f.starts[f.text.indexOf('œuf')]).toBe(text.indexOf('Œuf'));
    expect(f.starts[f.text.length]).toBe(text.length);
  });

  it('counts words of letters and digits', () => {
    expect([...countWords('Re-plan the PLAN, 2 plans')]).toEqual([
      ['re', 1],
      ['plan', 2],
      ['the', 1],
      ['2', 1],
      ['plans', 1],
    ]);
  });
});

describe('parseWordQuery', () => {
  it('splits words and quoted phrases, folded and without repeats', () => {
    expect(parseWordQuery('Budget "next  Year" budget "" "!!"')).toEqual({
      words: ['budget'],
      phrases: ['next year'],
    });
  });

  it('runs an unclosed quote to the end', () => {
    expect(parseWordQuery('a "b c')).toEqual({ words: ['a'], phrases: ['b c'] });
  });
});

describe('wordMatches', () => {
  it('matches each word at the start of a word, ignoring case and accents', () => {
    expect(marked('Planning the café; replan', 'plan CAFE')).toEqual(['Plan', 'café']);
  });

  it('needs every word and phrase unless partial', () => {
    expect(marked('only budget here', 'budget march')).toEqual([]);
    expect(marked('only budget here', 'budget march', true)).toEqual(['budget']);
    expect(marked('a b c', '"c b"')).toEqual([]);
  });

  it('matches a phrase across any whitespace and marks it whole', () => {
    expect(marked('see next\n  year now', '"next year"')).toEqual(['next\n  year']);
  });

  it('marks the longer word when two query words share a start', () => {
    expect(marked('planet plan', 'plan planet')).toEqual(['planet', 'plan']);
  });
});

describe('WordIndex', () => {
  const index = new WordIndex();
  index.set('a', 'budget for march');
  index.set('b', 'budget budget budget april');
  index.set('c', 'marching band');

  it('finds documents with every word as a word start', () => {
    expect([...index.candidates(parseWordQuery('march'))].map(([id]) => id).sort()).toEqual(['a', 'c']);
    expect([...index.candidates(parseWordQuery('budget march'))].map(([id]) => id)).toEqual(['a']);
    expect(index.candidates(parseWordQuery('zebra')).size).toBe(0);
  });

  it('scores more hits higher', () => {
    const s = index.candidates(parseWordQuery('budget'));
    expect(s.get('b')!).toBeGreaterThan(s.get('a')!);
  });

  it('forgets a document set again or deleted', () => {
    index.set('c', 'nothing');
    index.delete('a');
    expect(index.candidates(parseWordQuery('march')).size).toBe(0);
    expect(index.size).toBe(2);
  });
});
