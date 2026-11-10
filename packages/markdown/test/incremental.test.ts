import { caretAt, DocumentBuffer, TextDocument } from '@puffinote/text';
import { describe, expect, it } from 'vitest';
import { MarkdownModel } from '../src/index.ts';

/** An incremental update must always equal a full rebuild (PRD §11.2). */
function expectIncrementalMatchesFull(
  before: string,
  offset: number,
  deleteLen: number,
  insert: string,
): void {
  const doc1 = TextDocument.fromString(before);
  const model1 = MarkdownModel.build(doc1);
  const firstChangedLine = doc1.getPosition(offset).line;
  const doc2 = doc1.replace(offset, deleteLen, insert);
  expect(model1.update(doc2, firstChangedLine).model.allTokens()).toEqual(
    MarkdownModel.build(doc2).allTokens(),
  );
}

describe('incremental tokenizing', () => {
  it('matches a full rebuild for an edit within a paragraph', () => {
    expectIncrementalMatchesFull('hello world\nsecond line\nthird', 6, 5, 'there');
  });

  it('matches for typed bold markers', () => {
    expectIncrementalMatchesFull('make this bold\nnext', 0, 0, '**');
  });

  it('matches when a newline shifts the lines', () => {
    expectIncrementalMatchesFull('one two three\nfour', 3, 1, '\n');
  });

  it('matches when an opening fence at the top flips the rest to code', () => {
    expectIncrementalMatchesFull('para one\npara **two**\npara three', 0, 0, '```\n');
  });

  it('matches when a closing fence is deleted', () => {
    expectIncrementalMatchesFull('```\ncode\n```\nafter', 9, 3, '');
  });

  it('matches for edits inside and above a table', () => {
    expectIncrementalMatchesFull('| A | B |\n| - | - |\n| 1 | 2 |', 24, 1, '9');
    expectIncrementalMatchesFull('intro\n| A | B |\n| - | - |\n| 1 | 2 |', 0, 5, '# Title');
  });

  it.each([
    ['Title\nX', 1, '==='],
    ['Title\n===', 1, ''],
    ['Title\n===\nbody', 2, 'more'],
    ['a\n===\nb\n===', 2, 'c'],
  ])('matches for setext edits in %j', (source, editLine, replacement) => {
    const doc = TextDocument.fromString(source);
    const start = doc.getLineStart(editLine);
    expectIncrementalMatchesFull(source, start, doc.getLineEnd(editLine) - start, replacement);
  });

  it('records a checkpoint for every line', () => {
    const doc = TextDocument.fromString('# H\n\n```\ncode\n```\n\n- list\n- items\n\npara');
    const model = MarkdownModel.build(doc);
    expect(model.lineCount).toBe(doc.lineCount);
    expect(model.enteringState(3).kind).toBe('fencedCode');
  });

  it('matches a full rebuild when driven by a buffer, through undo and redo', () => {
    const buffer = new DocumentBuffer('');
    let model = MarkdownModel.build(buffer.document);
    const apply = (edit: () => unknown): void => {
      edit();
      model = model.update(buffer.document, buffer.lastChangedLine).model;
      expect(model.allTokens()).toEqual(MarkdownModel.build(buffer.document).allTokens());
    };

    apply(() => buffer.insert('# Title'));
    apply(() => buffer.insert('\n\nA paragraph with **bold** and `code`.'));
    apply(() => buffer.insert('\n\n- item one\n- item two'));
    buffer.selection = caretAt(2);
    apply(() => buffer.insert('# '));
    buffer.selection = caretAt(0);
    apply(() => buffer.insert('```\n'));
    apply(() => buffer.undo());
    apply(() => buffer.redo());
    apply(() => buffer.undo());
  });

  it('matches a full rebuild across random edits', () => {
    let s = 0x5eed >>> 0;
    const next = (n: number): number => {
      s = (s * 1664525 + 1013904223) >>> 0;
      return s % n;
    };
    const pieces = [
      '\n',
      '# ',
      '- ',
      '```',
      '**',
      '|',
      ' | ',
      '---',
      '===',
      '> ',
      '    ',
      'word',
      '[x](y)',
      '\n\n',
    ];
    let doc = TextDocument.fromString(
      '# Title\n\nText with **bold**.\n\n- a\n- b\n\n| A | B |\n| - | - |\n| 1 | 2 |',
    );
    let model = MarkdownModel.build(doc);
    for (let step = 0; step < 400; step++) {
      const offset = next(doc.length + 1);
      const span = next(Math.min(5, doc.length - offset) + 1);
      const insert = next(3) === 0 ? '' : pieces[next(pieces.length)]!;
      const line = doc.getPosition(offset).line;
      doc = doc.replace(offset, span, insert);
      model = model.update(doc, line).model;
      expect(model.allTokens()).toEqual(MarkdownModel.build(doc).allTokens());
    }
  });
});
