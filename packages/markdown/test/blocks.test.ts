import { TextDocument } from '@puffinote/text';
import { describe, expect, it } from 'vitest';
import { blockAtLine, blocksOf, MarkdownModel, renderHtmlBody, updateBlocks } from '../src/index.ts';

const model = (md: string) => MarkdownModel.build(TextDocument.fromString(md));
const spans = (md: string) => blocksOf(model(md)).map((b) => [b.kind, b.startLine, b.endLine]);

describe('blocksOf', () => {
  it('finds front matter, fenced code and tables, in order', () => {
    const md = [
      '---',
      'a: 1',
      '---',
      'text',
      '```js',
      'x',
      '```',
      '| a | b |',
      '| - | - |',
      '| 1 | 2 |',
      'after',
    ];
    expect(spans(md.join('\n'))).toEqual([
      ['frontMatter', 0, 2],
      ['fencedCode', 4, 6],
      ['table', 7, 9],
    ]);
  });

  it('runs an unclosed fence to the end, and ends a table at a row without a pipe', () => {
    expect(spans('```\ncode\nmore')).toEqual([['fencedCode', 0, 2]]);
    expect(spans('| a |\n| - |\nplain')).toEqual([['table', 0, 1]]);
  });

  it('ends a table where the model does, and the export with it', () => {
    // An escaped pipe splits no cells, so the row after the table is prose, in the block list and the export.
    const md = '| a | b |\n| - | - |\n| 1 | 2 |\nx \\| y\n| 3 |';
    expect(spans(md)).toEqual([['table', 0, 2]]);
    const html = renderHtmlBody(TextDocument.fromString(md));
    expect(html.match(/<tr>/g)).toHaveLength(2);
    expect(html).toContain('<p>x');
  });

  it('finds the block covering a line', () => {
    const blocks = blocksOf(model('x\n```\ny\n```\nz'));
    expect(blockAtLine(blocks, 2)?.kind).toBe('fencedCode');
    expect(blockAtLine(blocks, 4)).toBeNull();
  });

  it('gives the same blocks after an edit as a fresh scan', () => {
    const before = TextDocument.fromString('a\n```\nb\n```\n| a |\n| - |\n| 1 |\nc');
    const m = MarkdownModel.build(before);
    const blocks = blocksOf(m);
    const after = TextDocument.fromString('a\n```\nb\n```\n| a |\n| - |\n| 1 |\n| 2 |');
    const u = m.update(after, 7);
    expect(updateBlocks(blocks, u.model, u.dirtyFromLine)).toEqual(blocksOf(MarkdownModel.build(after)));
  });
});
