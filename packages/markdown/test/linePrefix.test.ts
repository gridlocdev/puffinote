import { describe, expect, it } from 'vitest';
import {
  computeEnter,
  continuationPrefix,
  extensionOf,
  isLazyContinuation,
  isMarkdownFile,
  parseLinePrefix,
  renumberOrderedList,
  stripDuplicateTaskMarker,
  taskCode,
  toggledTaskState,
} from '../src/index.ts';

const prefix = (line: string): string => continuationPrefix(parseLinePrefix(line));

describe('parseLinePrefix', () => {
  it.each(['-', '*', '+'])('reads the unordered marker %s', (bullet) => {
    const p = parseLinePrefix(`${bullet} item`);
    expect(p.list).toBe('unordered');
    expect(p.bullet).toBe(bullet);
    expect(p.length).toBe(2);
  });

  it('reads an ordered marker', () => {
    expect(parseLinePrefix('12. text')).toMatchObject({
      list: 'ordered',
      number: 12,
      delim: '.',
      digitCount: 2,
      length: 4,
    });
  });

  it('reads a task box', () => {
    const p = parseLinePrefix('- [x] done');
    expect(p).toMatchObject({ hasTask: true, taskChecked: true, task: 'done', length: 6, taskMarkColumn: 3 });
  });

  it('reads all three task states, and nothing else', () => {
    expect(parseLinePrefix('- [ ] a').task).toBe('notStarted');
    expect(parseLinePrefix('- [/] a').task).toBe('inProgress');
    expect(parseLinePrefix('- [x] a').task).toBe('done');
    expect(parseLinePrefix('- [a] a').hasTask).toBe(false);
  });

  it('counts only done as checked', () => {
    expect(parseLinePrefix('- [x] a').taskChecked).toBe(true);
    expect(parseLinePrefix('- [/] a').taskChecked).toBe(false);
    expect(parseLinePrefix('- [ ] a').taskChecked).toBe(false);
  });

  it('reads a blockquote and indent', () => {
    expect(parseLinePrefix('  > > quote')).toMatchObject({ indent: 2, quote: '> > ', hasList: false });
  });

  it('captures a tab indent verbatim', () => {
    expect(parseLinePrefix('\t- [ ] todo')).toMatchObject({
      list: 'unordered',
      hasTask: true,
      indent: 1,
      indentText: '\t',
    });
  });
});

describe('continuationPrefix', () => {
  it('increments ordered items and unchecks tasks', () => {
    expect(prefix('- a')).toBe('- ');
    expect(prefix('2. a')).toBe('3. ');
    expect(prefix('- [x] a')).toBe('- [ ] ');
    expect(prefix('  > a')).toBe('  > ');
    expect(prefix('- [/] a')).toBe('- [ ] ');
    expect(prefix('- [-] a')).toBe('- ');
  });

  it('keeps a tab indent', () => {
    expect(prefix('\t- item')).toBe('\t- ');
    expect(prefix('\t- [ ] item')).toBe('\t- [ ] ');
    expect(prefix('\t1. item')).toBe('\t2. ');
  });
});

describe('computeEnter', () => {
  it('continues a list', () => {
    expect(computeEnter('- apple', 7)).toMatchObject({ kind: 'continue', text: '\n- ' });
  });

  it('exits an empty item', () => {
    expect(computeEnter('- ', 2)).toMatchObject({ kind: 'exit', exitStart: 0, exitLength: 2 });
  });

  it('keeps the outer quote when exiting a nested empty item', () => {
    expect(computeEnter('> - ', 4)).toMatchObject({ kind: 'exit', exitStart: 2, exitLength: 2 });
  });

  it('carries a plain line indent down', () => {
    expect(computeEnter('    code', 8)).toMatchObject({ kind: 'continue', text: '\n    ' });
    expect(computeEnter('\tplain text', 11)).toMatchObject({ kind: 'continue', text: '\n\t' });
    expect(computeEnter('\t- item', 7)).toMatchObject({ kind: 'continue', text: '\n\t- ' });
  });

  it('inserts a plain newline when the line has no structure', () => {
    expect(computeEnter('hello', 5)).toMatchObject({ kind: 'plain', text: '\n' });
  });

  it('splits an item and continues it', () => {
    expect(computeEnter('- abcdef', 4)).toMatchObject({ kind: 'continue', text: '\n- ' });
  });

  it.each([
    ['- task', 0, '- \n'],
    ['- [ ] task', 0, '- [ ] \n'],
    ['- [x] task', 3, '- [ ] \n'],
    ['1. task', 0, '2. \n'],
    ['  - task', 2, '  - \n'],
    ['- [x] task', 6, '- [ ] \n'],
    ['1. task', 3, '2. \n'],
  ])(
    'opens an empty item above %j when the caret is in its marker or at its text start',
    (line, caret, text) => {
      expect(computeEnter(line, caret)).toMatchObject({ kind: 'above', text });
    },
  );
});

describe('renumberOrderedList', () => {
  it('resequences the following items', () => {
    expect(renumberOrderedList(['1. a', '1. b', '1. c'], 0)).toEqual({
      startLine: 0,
      endLineExclusive: 3,
      lines: ['1. a', '2. b', '3. c'],
    });
  });

  it('starts from the given line number', () => {
    const r = renumberOrderedList(['1. a', '2. b', '3. inserted', '3. c', '4. d'], 2);
    expect(r?.lines).toEqual(['3. inserted', '4. c', '5. d']);
    expect(r?.startLine).toBe(2);
  });

  it('stops at a different level or a non-list line', () => {
    expect(renumberOrderedList(['1. a', '2. b', 'text', '1. other'], 0)).toBeNull();
  });

  it('returns null when already sequential or not a list', () => {
    expect(renumberOrderedList(['1. a', '2. b', '3. c'], 0)).toBeNull();
    expect(renumberOrderedList(['text'], 0)).toBeNull();
    expect(renumberOrderedList(['1. a'], 5)).toBeNull();
  });

  it('renumbers tab-nested items by level', () => {
    expect(renumberOrderedList(['\t1. a', '\t1. b'], 0)?.lines).toEqual(['\t1. a', '\t2. b']);
  });
});

describe('isLazyContinuation', () => {
  it.each([
    'wrapped onto a second line',
    '1 is not a list marker without its dot',
    '[ ] a box needs a list marker to be live',
  ])('reads %j as prose', (line) => {
    expect(isLazyContinuation(line)).toBe(true);
  });

  it.each(['', '   ', '- bullet', '2. item', '> quote', '## heading', '---', '```', '    indented code'])(
    'reads %j as a block opener',
    (line) => {
      expect(isLazyContinuation(line)).toBe(false);
    },
  );
});

describe('stripDuplicateTaskMarker', () => {
  it.each([
    ['- [ ] Buy milk', 'Buy milk'],
    ['- [x] Done thing', 'Done thing'],
    ['  * [/] Nested', 'Nested'],
    ['3. [ ] Numbered', 'Numbered'],
    ['- [ ] first\n- [ ] second', 'first\n- [ ] second'],
  ])('drops the incoming marker from %j', (pasted, expected) => {
    expect(stripDuplicateTaskMarker(pasted, '- [ ] ', 6)).toBe(expected);
  });

  it.each([
    ['- [ ] Buy milk', '- item', 6],
    ['- [ ] Buy milk', 'plain text', 5],
    ['- [ ] Buy milk', '- [ ] ', 0],
    ['- bullet', '- [ ] ', 6],
    ['plain paste', '- [ ] ', 6],
    ['', '- [ ] ', 6],
  ])('pastes %j into %j unchanged', (pasted, target, caret) => {
    expect(stripDuplicateTaskMarker(pasted, target, caret)).toBe(pasted);
  });
});

describe('task vocabulary', () => {
  it('toggles to done and back, never through in progress', () => {
    expect(toggledTaskState('notStarted')).toBe('done');
    expect(toggledTaskState('inProgress')).toBe('done');
    expect(toggledTaskState('done')).toBe('notStarted');
    expect(taskCode('inProgress')).toBe('[/]');
  });
});

describe('markdown files', () => {
  it('recognizes note extensions case-insensitively', () => {
    expect(isMarkdownFile('notes/Plan.MD')).toBe(true);
    expect(isMarkdownFile('C:\\notes\\todo.txt')).toBe(true);
    expect(isMarkdownFile('photo.png')).toBe(false);
    expect(isMarkdownFile('README')).toBe(false);
    expect(extensionOf('a.dir/file')).toBe('');
  });
});
