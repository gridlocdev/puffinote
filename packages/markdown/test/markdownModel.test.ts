import { describe, expect, it } from 'vitest';
import { outlineOf, taskStateFromMark } from '../src/index.ts';
import { hasType, textsOfType, tokenize, typesIn } from './helpers.ts';

describe('block constructs', () => {
  it.each([
    ['# H1', '1'],
    ['### H3', '3'],
    ['###### H6', '6'],
  ])('reads %s as a heading of level %s', (md, level) => {
    const markers = tokenize(md)
      .model.allTokens()
      .filter((t) => t.type === 'headingMarker');
    expect(markers).toHaveLength(1);
    expect(markers[0]!.meta).toBe(level);
  });

  it('does not read seven hashes as a heading', () => {
    expect(hasType('####### too many', 'headingMarker')).toBe(false);
  });

  it('emits blockquote and list markers', () => {
    expect(textsOfType('> quoted', 'blockquoteMarker')).toEqual(['> ']);
    expect(textsOfType('- item', 'listMarker')).toEqual(['- ']);
    expect(textsOfType('1. a\n2. b', 'listMarker')).toEqual(['1. ', '2. ']);
  });

  it('emits task markers', () => {
    expect(textsOfType('- [ ] todo\n- [x] done', 'taskMarker')).toEqual(['[ ] ', '[x] ']);
  });

  it('reads all three task states and carries the state in meta', () => {
    const { model } = tokenize('- [ ] a\n- [/] b\n- [x] c');
    const states = [0, 1, 2].map((line) =>
      taskStateFromMark(model.lineTokens(line).find((t) => t.type === 'taskMarker')!.meta!),
    );
    expect(states).toEqual(['notStarted', 'inProgress', 'done']);
  });

  it('reads an uppercase X as done and normalizes meta', () => {
    const marker = tokenize('- [X] done')
      .model.lineTokens(0)
      .find((t) => t.type === 'taskMarker')!;
    expect(marker.meta).toBe('x');
  });

  it('does not read an unknown box character as a task', () => {
    for (const md of ['- [a] not a task', '- [] not a task', '- [?] blocked', '- [>] deferred', '- [-] x']) {
      expect(hasType(md, 'taskMarker')).toBe(false);
    }
  });

  it('reads horizontal rules', () => {
    for (const md of ['---', '***', '___']) expect(hasType(md, 'horizontalRule')).toBe(true);
  });

  it('carries fenced code state and does not tokenize inside it', () => {
    const md = '```python\nx = 1\ny = 2\n```';
    const { model } = tokenize(md);
    expect(textsOfType(md, 'codeText')).toEqual(['x = 1', 'y = 2']);
    expect(model.enteringState(0).kind).toBe('normal');
    expect(model.enteringState(1).kind).toBe('fencedCode');
    expect(model.enteringState(2).kind).toBe('fencedCode');
    expect(hasType('```\n**not bold**\n```', 'strong')).toBe(false);
  });

  it('captures the fence language', () => {
    const fence = tokenize('```rust\ncode\n```')
      .model.allTokens()
      .find((t) => t.type === 'codeFence')!;
    expect(fence.meta).toBe('rust');
  });

  it('extends an unterminated fence to the end of the file', () => {
    expect(textsOfType('```\nstill code\nmore code', 'codeText')).toEqual(['still code', 'more code']);
  });

  it('opens front matter only at the top', () => {
    const md = '---\ntitle: Hi\n---\n# Body';
    expect(tokenize(md).model.enteringState(1).kind).toBe('frontMatter');
    expect(hasType(md, 'frontMatterText')).toBe(true);
    expect(hasType('text\n---\nmore', 'frontMatterFence')).toBe(false);
  });

  it('recognizes a table only with a delimiter row', () => {
    const md = '| A | B |\n| - | - |\n| 1 | 2 |';
    expect(hasType(md, 'tableDelimiter')).toBe(true);
    expect(
      tokenize(md)
        .model.lineTokens(0)
        .some((t) => t.type === 'marker'),
    ).toBe(true);
    expect(hasType('a | b | c\njust text', 'tableDelimiter')).toBe(false);
  });

  it('treats CRLF lines like LF lines', () => {
    expect(textsOfType('# Title\r\n- item\r\n', 'listMarker')).toEqual(['- ']);
    expect(textsOfType('a **b**\r\n', 'strong')).toEqual(['b']);
  });
});

describe('indented code (CommonMark §4.4)', () => {
  it('opens after a blank line and at the top of the document', () => {
    expect(textsOfType('para\n\n    code here\nback to prose', 'codeText')).toEqual(['    code here']);
    expect(textsOfType('    int x = 1;\nprose', 'codeText')).toEqual(['    int x = 1;']);
  });

  it('cannot interrupt a paragraph', () => {
    expect(hasType('a paragraph\n    still the paragraph', 'codeText')).toBe(false);
  });

  it('stays open across blank and whitespace-only lines', () => {
    expect(textsOfType('x\n\n    line one\n\n    line three\nprose', 'codeText')).toEqual([
      '    line one',
      '    line three',
    ]);
    const md = 'x\n\n    line one\n  \n    line three\nprose';
    expect(textsOfType(md, 'codeText')).toEqual(['    line one', '    line three']);
    expect(tokenize(md).model.enteringState(4).kind).toBe('indentedCode');
  });

  it('opens after a whitespace-only blank line', () => {
    expect(textsOfType('para\n  \n    code\n', 'codeText')).toEqual(['    code']);
  });

  it('ends at a dedent', () => {
    const md = 'x\n\n    code\n  not code (two spaces)';
    expect(textsOfType(md, 'codeText')).toEqual(['    code']);
    expect(typesIn(md)).toContain('text');
    expect(tokenize('x\n\n    code\nprose').model.enteringState(3).kind).toBe('indentedCode');
  });
});

describe('tab indentation', () => {
  it('reads tab-nested list items and tasks', () => {
    expect(hasType('- a\n\t- b', 'listMarker')).toBe(true);
    expect(hasType('- a\n\t- [ ] b', 'taskMarker')).toBe(true);
  });

  it('opens indented code with one tab at a fresh position', () => {
    expect(textsOfType('x\n\n\tcode\nprose', 'codeText')).toEqual(['\tcode']);
  });
});

describe('setext headings', () => {
  const first = (md: string, line: number) => tokenize(md).model.lineTokens(line)[0];

  it.each([
    ['Title\n=====', '1'],
    ['Title\n-----', '2'],
    ['Title\n=', '1'],
    ['Title\n-', '2'],
    ['Title\n===   ', '1'],
    ['  Title\n  ===', '1'],
  ])('reads %j as a heading of level %s', (md, level) => {
    const marker = first(md, 0)!;
    expect(marker.type).toBe('headingMarker');
    expect(marker.meta).toBe(level);
    expect(marker.length).toBe(0);
    expect(first(md, 1)!.type).toBe('setextUnderline');
  });

  it('takes a dashed underline over a horizontal rule', () => {
    expect(first('Title\n---', 0)).toMatchObject({ type: 'headingMarker', meta: '2' });
    expect(first('Title\n---', 1)!.type).toBe('setextUnderline');
  });

  it('keeps a lone rule a rule', () => {
    expect(first('Title\n\n---\n', 2)!.type).toBe('horizontalRule');
  });

  it.each(['# ATX\n---', '> quote\n---', '- item\n---', '\n---', '```\ncode\n```\n---'])(
    'gives no underline to non-paragraph text in %j',
    (md) => {
      const { model } = tokenize(md);
      expect(model.lineTokens(model.lineCount - 1)[0]!.type).not.toBe('setextUnderline');
    },
  );

  it('does not read front matter as a setext heading', () => {
    expect(first('---\ntitle: x\n---\nbody\n', 0)!.type).toBe('frontMatterFence');
  });
});

describe('blockquote depth', () => {
  it.each([
    ['> one', '1'],
    ['>> two', '2'],
    ['> > two', '2'],
    ['>>> three', '3'],
  ])('reads %j at depth %s', (md, depth) => {
    expect(tokenize(md).model.lineTokens(0)[0]).toMatchObject({ type: 'blockquoteMarker', meta: depth });
  });
});

describe('outline', () => {
  const outline = (md: string) => {
    const { doc, model } = tokenize(md);
    return outlineOf(doc, model);
  };

  it('strips closing sequences and inline markup from heading text', () => {
    const items = outline(
      '# A [link](http://x) and **b** ##\n## C #not closing\n### D\\#\n#### E   #####   ',
    );
    expect(items.map((o) => [o.level, o.text, o.offset])).toEqual([
      [1, 'A link and b', 2],
      [2, 'C #not closing', 37],
      [3, 'D\\#', 56],
      [4, 'E', 65],
    ]);
  });

  it('reads long runs of blanks or brackets in a heading in linear time', () => {
    const started = performance.now();
    expect(outline('# a' + ' '.repeat(40000) + '#')[0]!.text).toBe('a');
    expect(outline('# a' + ' '.repeat(40000) + 'x')[0]!.text).toHaveLength(40002);
    expect(outline('# ' + '['.repeat(40000))[0]!.text).toHaveLength(40000);
    expect(outline('# ' + '[a]('.repeat(10000))[0]!.text).toHaveLength(40000);
    expect(performance.now() - started).toBeLessThan(1500);
  });
});
