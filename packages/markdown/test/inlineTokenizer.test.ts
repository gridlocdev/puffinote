import { describe, expect, it } from 'vitest';
import { findNestedDelimiters } from '../src/index.ts';
import { hasType, textsOfType, tokenize } from './helpers.ts';

describe('inline tokenizer', () => {
  it('reads bold, italic and bold italic', () => {
    expect(textsOfType('a **bold** word', 'strong')).toEqual(['bold']);
    expect(textsOfType('*it* and _it2_', 'emphasis')).toEqual(['it', 'it2']);
    expect(textsOfType('***x***', 'strongEmphasis')).toEqual(['x']);
  });

  it('reads strikethrough and highlight', () => {
    expect(textsOfType('~~gone~~', 'strikethrough')).toEqual(['gone']);
    expect(textsOfType('==hot==', 'highlight')).toEqual(['hot']);
  });

  it('keeps an intra-word underscore literal', () => {
    expect(hasType('foo_bar_baz', 'emphasis')).toBe(false);
    expect(hasType('a snake_case name', 'emphasis')).toBe(false);
  });

  it('still emphasises underscores at word boundaries', () => {
    expect(textsOfType('an _it_ word', 'emphasis')).toEqual(['it']);
    expect(textsOfType('__name__', 'strong')).toEqual(['name']);
  });

  it('lets an asterisk emphasise inside a word', () => {
    expect(textsOfType('a*bc*d', 'emphasis')).toEqual(['bc']);
  });

  it('does not close on an escaped delimiter', () => {
    expect(textsOfType('*a\\*b*', 'emphasis')).toEqual(['a\\*b']);
    expect(textsOfType('~~x\\~~y~~', 'strikethrough')).toEqual(['x\\~~y']);
  });

  it('reads inline code, including a double-backtick fence', () => {
    expect(textsOfType('run `x = 1` now', 'inlineCode')).toEqual(['x = 1']);
    expect(textsOfType('``a `b` c``', 'inlineCode')).toEqual(['a `b` c']);
  });

  it('reads a link, with emphasis in its text', () => {
    expect(textsOfType('[home](https://x.com)', 'linkUrl')).toEqual(['https://x.com']);
    expect(hasType('[home](https://x.com)', 'text')).toBe(true);
    expect(textsOfType('[a **bold** b](u)', 'strong')).toEqual(['bold']);
  });

  it('reads an image', () => {
    expect(textsOfType('![alt](pic.png)', 'imageUrl')).toEqual(['pic.png']);
    expect(textsOfType('![alt](pic.png)', 'imageAlt')).toEqual(['alt']);
  });

  it('keeps a title out of the destination', () => {
    expect(textsOfType('[x](http://y "a title")', 'linkUrl')).toEqual(['http://y']);
    expect(textsOfType("![alt](pic.png 'caption')", 'imageUrl')).toEqual(['pic.png']);
  });

  it('reads bracketed and bare autolinks', () => {
    expect(textsOfType('<https://x.com>', 'autolink')).toEqual(['https://x.com']);
    expect(textsOfType('see https://example.com/path here', 'autolink')).toEqual([
      'https://example.com/path',
    ]);
    expect(textsOfType('go to https://example.com.', 'autolink')).toEqual(['https://example.com']);
  });

  it('renders an escape as a literal', () => {
    expect(hasType('\\*not italic\\*', 'emphasis')).toBe(false);
    expect(hasType('\\*not italic\\*', 'escape')).toBe(true);
  });

  it('reads wiki-links and embeds', () => {
    expect(textsOfType('see [[Note Title]]', 'wikiLink')).toEqual(['Note Title']);
    expect(textsOfType('![[image.png]]', 'embed')).toEqual(['image.png']);
  });

  it('reads hard breaks', () => {
    expect(hasType('line one  \nline two', 'hardBreak')).toBe(true);
    expect(hasType('line one\\\nline two', 'hardBreak')).toBe(true);
    expect(hasType('line one \nline two', 'hardBreak')).toBe(false);
    expect(hasType('# heading  \ntext', 'hardBreak')).toBe(false);
  });

  it('keeps malformed constructs literal (PRD §10.2)', () => {
    expect(hasType('a **unclosed bold', 'strong')).toBe(false);
    expect(hasType('a **unclosed bold', 'text')).toBe(true);
    expect(hasType('[not a link', 'linkUrl')).toBe(false);
    expect(hasType('a `unterminated', 'inlineCode')).toBe(false);
    expect(hasType('****', 'strong')).toBe(false);
  });

  it('reads an escaped trailing backslash as text, not a hard break', () => {
    expect(hasType('path C:\\\\\nnext', 'hardBreak')).toBe(false);
    expect(textsOfType('path C:\\\\\nnext', 'escape')).toEqual(['\\', '\\']);
    expect(hasType('odd\\\\\\\nnext', 'hardBreak')).toBe(true);
  });

  it('tokenizes long pathological lines in linear time', () => {
    // Each of these was quadratic: a failed closer or bracket search rescanned the rest of the line.
    for (const unit of ['*a ', '~~a ', '==a ', '[a', '[a](', '![a](', '[[a', '<a ', '*', '`a``']) {
      const small = tokenize(unit.repeat(50)).model.allTokens();
      const started = performance.now();
      const big = tokenize(unit.repeat(40000)).model.allTokens();
      expect(performance.now() - started, unit).toBeLessThan(1500);
      expect(new Set(big.map((t) => t.type)), unit).toEqual(new Set(small.map((t) => t.type)));
    }
  });
});

describe('inline nodes', () => {
  const tokens = (md: string) => tokenize(md).model.allTokens();

  it('gives the markers and content of one construct one non-zero node', () => {
    const all = tokens('a **bold** b');
    const construct = all.filter((t) => t.type === 'marker' || t.type === 'strong');
    expect(construct.every((t) => t.node !== 0)).toBe(true);
    expect(new Set(construct.map((t) => t.node)).size).toBe(1);
    expect(all.filter((t) => t.type === 'text').every((t) => t.node === 0)).toBe(true);
  });

  it('gives two constructs on a line distinct nodes', () => {
    const all = tokens('**a** and *b*');
    const strong = all.find((t) => t.type === 'strong')!.node;
    const em = all.find((t) => t.type === 'emphasis')!.node;
    expect(strong).not.toBe(0);
    expect(em).not.toBe(0);
    expect(strong).not.toBe(em);
  });

  it('gives a link one node, distinct from emphasis nested in it', () => {
    const all = tokens('[a **b** c](http://x.com)');
    const link = all.find((t) => t.type === 'linkUrl')!.node;
    expect(link).not.toBe(0);
    expect(all.find((t) => t.type === 'marker')!.node).toBe(link);
    const bold = all.find((t) => t.type === 'strong')!.node;
    expect(bold).not.toBe(0);
    expect(bold).not.toBe(link);
  });

  it('leaves line-prefix and block markers at zero', () => {
    for (const md of ['# Heading', '- [ ] task', '> quote']) {
      const block = tokens(md).filter((t) =>
        ['headingMarker', 'listMarker', 'taskMarker', 'blockquoteMarker'].includes(t.type),
      );
      expect(block.length).toBeGreaterThan(0);
      expect(block.every((t) => t.node === 0)).toBe(true);
    }
  });

  it('restarts node ids on each line', () => {
    const nodes = tokens('*one*\n*two*')
      .filter((t) => t.type === 'emphasis')
      .map((t) => t.node);
    expect(nodes).toHaveLength(2);
    expect(nodes[0]).toBe(nodes[1]);
  });
});

describe('findNestedDelimiters', () => {
  it('finds the strike nested in strong-emphasis content', () => {
    expect(findNestedDelimiters('~~bold~~')).toEqual([
      { openStart: 0, innerEnd: 6, markerLen: 2, delim: '~' },
    ]);
  });

  it('finds constructs in order, one level deep', () => {
    const spans = findNestedDelimiters('a *b* and ==c==');
    expect(spans.map((s) => s.delim)).toEqual(['*', '=']);
  });

  it('ignores an unclosed or space-padded run', () => {
    expect(findNestedDelimiters('~~ open')).toEqual([]);
    expect(findNestedDelimiters('*open')).toEqual([]);
  });
});
