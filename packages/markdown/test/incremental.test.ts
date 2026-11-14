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

  it('matches as a table header gains or loses the cell its delimiter row needs', () => {
    expectIncrementalMatchesFull('a | b\n--- | ---\n1 | 2', 5, 0, ' | c');
    expectIncrementalMatchesFull('a | b | c\n--- | ---\n1 | 2', 5, 4, '');
    expectIncrementalMatchesFull('a | b\n--- | ---\n1 | 2', 15, 0, ' | ---');
  });

  it('matches while a list item is typed under a paragraph or a pipe row', () => {
    expectIncrementalMatchesFull('Groceries:\n', 11, 0, '-');
    expectIncrementalMatchesFull('Groceries:\n-', 12, 0, ' ');
    expectIncrementalMatchesFull('name | price\n', 13, 0, '-');
    expectIncrementalMatchesFull('name | price\n-', 14, 0, '-');
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

  it('matches when a front matter closing fence is typed or deleted far below line 0', () => {
    const open = '---\ntitle: x\nmore: y\n';
    expectIncrementalMatchesFull(open, open.length, 0, '---');
    expectIncrementalMatchesFull(open + '---\nbody', open.length, 3, '');
    expectIncrementalMatchesFull(open + '---\nbody', open.length + 1, 1, '');
    expectIncrementalMatchesFull(open + '...\nbody', open.length, 3, 'x');
  });

  /** Every line's entering state and tokens, as a full rebuild must also give them. */
  const snapshot = (doc: TextDocument, model: MarkdownModel): unknown[] =>
    Array.from({ length: doc.lineCount }, (_, i) => [model.enteringState(i), model.lineTokens(i)]);

  it('matches a full rebuild, entering states included, across chained random edits', () => {
    let s = 0xc0ffee >>> 0;
    const next = (n: number): number => {
      s = (s * 1664525 + 1013904223) >>> 0;
      return s % n;
    };
    const pieces = [
      'a',
      'b c',
      '|',
      '| a | b |',
      '|-|-|',
      '---',
      '===',
      '```',
      '```js',
      '~~~',
      '    x',
      '',
      '> q',
      '- i',
      '1. n',
      '# h',
      '...',
      '+++',
      '\t\tt',
      '-',
      '=',
      '\n',
      '\n\n',
      '**x**',
      '[l](u)',
      '\r\n',
      'x  ',
      '\\',
    ];
    for (let run = 0; run < 150; run++) {
      const lineCount = 1 + next(12);
      let doc = TextDocument.fromString(
        Array.from({ length: lineCount }, () => pieces[next(pieces.length)]!).join('\n'),
      );
      let model = MarkdownModel.build(doc);
      for (let step = 0; step < 30; step++) {
        const offset = next(doc.length + 1);
        const span = next(Math.min(8, doc.length - offset) + 1);
        let insert = next(4) === 0 ? '' : pieces[next(pieces.length)]!;
        if (next(3) === 0) insert += '\n' + pieces[next(pieces.length)]!;
        const line = doc.getPosition(offset).line;
        doc = doc.replace(offset, span, insert);
        model = model.update(doc, line).model;
        // Read some lines mid-chain, so later updates carry tokens that were already shifted.
        if (next(5) === 0) model.lineTokens(next(doc.lineCount));
        expect(snapshot(doc, model)).toEqual(snapshot(doc, MarkdownModel.build(doc)));
      }
    }
  });

  it('matches a full rebuild across random edits to nested lists and the code under them', () => {
    let s = 0x115715 >>> 0;
    const next = (n: number): number => {
      s = (s * 1664525 + 1013904223) >>> 0;
      return s % n;
    };
    const pieces = [
      '- a',
      '  - b',
      '    - c',
      '\t- d',
      '1. e',
      '   2. f',
      '      code',
      '    x',
      '  ',
      '\t',
      '',
      'p',
      '```',
      '    ```',
      '> - q',
      '\n',
      '\n\n',
      '- [ ] t',
    ];
    for (let run = 0; run < 150; run++) {
      const lineCount = 1 + next(12);
      let doc = TextDocument.fromString(
        Array.from({ length: lineCount }, () => pieces[next(pieces.length)]!).join('\n'),
      );
      let model = MarkdownModel.build(doc);
      for (let step = 0; step < 30; step++) {
        const offset = next(doc.length + 1);
        const span = next(Math.min(6, doc.length - offset) + 1);
        let insert = next(4) === 0 ? '' : pieces[next(pieces.length)]!;
        if (next(3) === 0) insert += '\n' + pieces[next(pieces.length)]!;
        const line = doc.getPosition(offset).line;
        doc = doc.replace(offset, span, insert);
        model = model.update(doc, line).model;
        expect(snapshot(doc, model)).toEqual(snapshot(doc, MarkdownModel.build(doc)));
      }
    }
  });

  it('matches a full rebuild when the new document was not made from the old one by one edit', () => {
    const before = '# T\n\n```\ncode\n```\n\n| a | b |\n|-|-|\n| 1 | 2 |\n\nText\n===\n';
    const model = MarkdownModel.build(TextDocument.fromString(before));
    for (const after of [
      before.replace('```\n\n', '\n\n'),
      before.replace('Text', 'Other'),
      '---\nx: 1\n---\n' + before,
    ]) {
      const doc = TextDocument.fromString(after);
      const line = doc.getPosition([...after].findIndex((c, i) => c !== before[i])).line;
      expect(snapshot(doc, model.update(doc, line).model)).toEqual(snapshot(doc, MarkdownModel.build(doc)));
    }
  });

  it('stops re-tokenizing once the block state converges (§11.2)', () => {
    let doc = TextDocument.fromString(
      Array.from({ length: 20000 }, (_, i) => `- item ${i} with **bold** and [link](u${i})`).join('\n'),
    );
    let model = MarkdownModel.build(doc);
    const started = performance.now();
    MarkdownModel.build(doc);
    const oneBuild = performance.now() - started;
    const typing = performance.now();
    for (let k = 0; k < 100; k++) {
      doc = doc.insert(2, 'a');
      model = model.update(doc, 0).model;
    }
    // Each keystroke re-ran the whole note before; now 100 of them cost less than two full builds.
    expect(performance.now() - typing).toBeLessThan(2 * oneBuild + 20);
    expect(model.lineTokens(19999)).toEqual(MarkdownModel.build(doc).lineTokens(19999));
  });
});
