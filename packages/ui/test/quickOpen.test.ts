import { describe, expect, it } from 'vitest';
import { rankFiles } from '../src/notebook/quickOpen.ts';

const files = [
  'Ideas.md',
  'Projects/Roadmap.md',
  'Projects/Meeting notes.md',
  'Journal/road trip.md',
  'Notes/x.md',
];

describe('rankFiles', () => {
  it('lists recent files first, then the rest by path, for an empty query', () => {
    expect(rankFiles(files, '', ['Notes/x.md'])).toEqual([
      'Notes/x.md',
      'Ideas.md',
      'Journal/road trip.md',
      'Projects/Meeting notes.md',
      'Projects/Roadmap.md',
    ]);
  });

  it('ranks a name prefix over a name match over a path match', () => {
    expect(rankFiles(files, 'road', [])).toEqual(['Journal/road trip.md', 'Projects/Roadmap.md']);
    expect(rankFiles(files, 'notes', [])).toEqual(['Projects/Meeting notes.md', 'Notes/x.md']);
  });

  it('matches letters in order and breaks ties by recency', () => {
    expect(rankFiles(files, 'rdmp', [])).toEqual(['Projects/Roadmap.md']);
    expect(rankFiles(files, 'road', ['Projects/Roadmap.md'])[0]).toBe('Projects/Roadmap.md');
    expect(rankFiles(['a/one.md', 'b/one.md'], 'one', ['b/one.md'])).toEqual(['b/one.md', 'a/one.md']);
  });

  it('drops files that do not match', () => {
    expect(rankFiles(files, 'zzz', [])).toEqual([]);
  });
});
