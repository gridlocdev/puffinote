import { describe, expect, it } from 'vitest';
import {
  countGraphemes,
  describeSearchStatus,
  findAll,
  isValidRegex,
  nextBoundary,
  previousBoundary,
} from '../src/index.ts';

describe('grapheme boundaries', () => {
  it('moves one code unit over ASCII', () => {
    expect(nextBoundary('abc', 0)).toBe(1);
    expect(previousBoundary('abc', 2)).toBe(1);
    expect(previousBoundary('abc', 3)).toBe(2);
  });

  it('moves over a surrogate pair as one unit', () => {
    const s = 'a\u{1F600}b';
    expect(nextBoundary(s, 0)).toBe(1);
    expect(nextBoundary(s, 1)).toBe(3);
    expect(previousBoundary(s, 3)).toBe(1);
  });

  it('moves over a base letter and its combining mark as one unit', () => {
    const s = 'éx';
    expect(nextBoundary(s, 0)).toBe(2);
    expect(previousBoundary(s, 2)).toBe(0);
  });

  it('treats CRLF as one grapheme', () => {
    expect(nextBoundary('a\r\nb', 1)).toBe(3);
  });

  it('clamps at the ends', () => {
    expect(previousBoundary('abc', 0)).toBe(0);
    expect(nextBoundary('abc', 3)).toBe(3);
    expect(nextBoundary('abc', 99)).toBe(3);
  });

  it('counts clusters', () => {
    expect(countGraphemes('a\u{1F600}b')).toBe(3);
    expect(countGraphemes('é')).toBe(1);
    expect(countGraphemes('')).toBe(0);
  });
});

describe('findAll', () => {
  it('finds non-overlapping plain matches', () => {
    const m = findAll('ababab', 'ab', { regex: false, caseSensitive: true });
    expect(m.map((x) => x.start)).toEqual([0, 2, 4]);
    expect(m.every((x) => x.length === 2)).toBe(true);
  });

  it('ignores case on request', () => {
    expect(findAll('Hello', 'hello', { regex: false, caseSensitive: false })).toHaveLength(1);
    expect(findAll('Hello', 'hello', { regex: false, caseSensitive: true })).toHaveLength(0);
  });

  it('treats plain queries literally', () => {
    expect(findAll('a.b axb', 'a.b', { regex: false, caseSensitive: false })).toEqual([
      { start: 0, length: 3 },
    ]);
  });

  it('matches regular expressions', () => {
    const m = findAll('a1 b22 c333', '\\d+', { regex: true, caseSensitive: true });
    expect(m).toHaveLength(3);
    expect(m[2]!.length).toBe(3);
  });

  it('finds nothing for an invalid pattern instead of throwing', () => {
    expect(findAll('text', '(', { regex: true, caseSensitive: true })).toEqual([]);
    expect(isValidRegex('(')).toBe(false);
    expect(isValidRegex('\\d+')).toBe(true);
  });

  it('skips zero-width matches', () => {
    const m = findAll('baa', 'a*', { regex: true, caseSensitive: true });
    expect(m.every((x) => x.length > 0)).toBe(true);
  });

  it('finds nothing for an empty query or text', () => {
    expect(findAll('text', '', { regex: false, caseSensitive: true })).toEqual([]);
    expect(findAll('', 'x', { regex: false, caseSensitive: true })).toEqual([]);
  });
});

describe('describeSearchStatus', () => {
  it('says nothing for an empty query rather than "No results"', () => {
    expect(describeSearchStatus(-1, 0, '')).toBe('');
    expect(describeSearchStatus(-1, 0, null)).toBe('');
  });

  it('says when nothing matched', () => {
    expect(describeSearchStatus(-1, 0, 'zzz')).toBe('No results');
  });

  it('counts the current match from one', () => {
    expect(describeSearchStatus(0, 17, 'the')).toBe('1 of 17');
    expect(describeSearchStatus(2, 17, 'the')).toBe('3 of 17');
    expect(describeSearchStatus(16, 17, 'the')).toBe('17 of 17');
    expect(describeSearchStatus(-1, 17, 'the')).toBe('1 of 17');
  });
});
