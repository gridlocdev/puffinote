import { blocksOf, MarkdownModel } from '@puffinote/markdown';
import { caretAt, TextDocument, type Selection } from '@puffinote/text';
import { describe, expect, it } from 'vitest';
import { buildRows } from '../src/editor/rows.ts';
import type { ViewMode } from '../src/editor/reveal.ts';

function rows(md: string, sel: Selection, mode: ViewMode = 'live') {
  const doc = TextDocument.fromString(md);
  const model = MarkdownModel.build(doc);
  return buildRows(
    doc,
    blocksOf(model),
    mode,
    sel,
    (i) => doc.getLineText(i),
    (i) => model.lineTokensRelative(i),
  ).map((r) =>
    r.kind === 'widget'
      ? `widget ${r.line} ${JSON.stringify(r.source)}`
      : `line ${r.line} ${r.block?.kind ?? '-'} ${r.listDepth}`,
  );
}

describe('buildRows', () => {
  const md = '- a\n  - b\n| h |\n| - |\n| 1 |\n- c';

  it('draws an untouched table as one widget in place of its lines', () => {
    expect(rows(md, caretAt(0))).toEqual([
      'line 0 - 0',
      'line 1 - 1',
      'widget 2 "| h |\\n| - |\\n| 1 |"',
      'line 5 - 0',
    ]);
  });

  it('draws a table line by line when the selection touches it, or outside the live view', () => {
    const lines = [
      'line 0 - 0',
      'line 1 - 1',
      'line 2 table 0',
      'line 3 table 0',
      'line 4 table 0',
      'line 5 - 0',
    ];
    expect(rows(md, caretAt(md.indexOf('| 1')))).toEqual(lines);
    expect(rows(md, caretAt(0), 'raw')).toEqual(lines);
  });

  it('gives each line in a block the block’s whole range', () => {
    const doc = TextDocument.fromString('x\n```\ny\n```');
    const model = MarkdownModel.build(doc);
    const out = buildRows(
      doc,
      blocksOf(model),
      'live',
      caretAt(0),
      (i) => doc.getLineText(i),
      (i) => model.lineTokensRelative(i),
    );
    expect(out.map((r) => (r.kind === 'line' ? r.range : null))).toEqual([
      null,
      { start: 2, end: 11 },
      { start: 2, end: 11 },
      { start: 2, end: 11 },
    ]);
  });
});
