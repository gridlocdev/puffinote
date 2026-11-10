import { MarkdownModel } from '@puffinote/markdown';
import { caretAt, TextDocument, type Selection } from '@puffinote/text';
import { describe, expect, it } from 'vitest';
import {
  blockAtLine,
  findBlocks,
  isInsideConcealed,
  lineReveal,
  renderLine,
  snapOutOfConcealed,
  type LineRender,
  type ViewMode,
} from '../src/index.ts';

/** Render `line` of `md` with the given selection, as the surface would. */
function render(
  md: string,
  sel: Selection,
  line = 0,
  mode: ViewMode = 'live',
  shownPicture: number | null = null,
): LineRender {
  const doc = TextDocument.fromString(md);
  const model = MarkdownModel.build(doc);
  const blocks = findBlocks(doc, model);
  const block = blockAtLine(blocks, line);
  const range = block
    ? { start: doc.getLineStart(block.startLine), end: doc.getLineEnd(block.endLine) }
    : null;
  const start = doc.getLineStart(line);
  const tokens = model.lineTokens(line);
  const reveal = lineReveal(tokens, start, doc.getLineEnd(line), sel, range, shownPicture);
  return renderLine(doc.getLineText(line), start, tokens, reveal, mode, block?.kind ?? null);
}

/** The text the line shows: visible runs, with decorations as `<kind>`. */
function visible(
  md: string,
  sel: Selection,
  line = 0,
  mode: ViewMode = 'live',
  shownPicture: number | null = null,
): string {
  const text = TextDocument.fromString(md).getLineText(line);
  return render(md, sel, line, mode, shownPicture)
    .segments.map((s) => (s.concealed ? (s.deco ? `<${s.deco.kind}>` : '') : text.slice(s.from, s.to)))
    .join('');
}

const elsewhere = caretAt(10_000);
const far = (md: string): Selection => caretAt(md.length);

describe('concealment (PRD §10.2)', () => {
  it('hides inline markers until the caret touches the node', () => {
    const md = 'a **bold** b\nnext';
    expect(visible(md, far(md))).toBe('a bold b');
    expect(visible(md, caretAt(6))).toBe('a **bold** b'); // inside
    expect(visible(md, caretAt(10))).toBe('a **bold** b'); // just after: touching reveals
    expect(visible(md, caretAt(0))).toBe('a bold b'); // on the line, but not at the node
  });

  it('keeps a picture drawn wherever the caret is, and shows its link text only for View Link Text', () => {
    const md = 'a ![cat](c.png) b';
    expect(visible(md, caretAt(15))).toBe('a <image> b'); // just after it, as after a paste
    expect(visible(md, caretAt(2))).toBe('a <image> b'); // just before it
    expect(visible(md, { anchor: 0, caret: md.length })).toBe('a <image> b'); // selected with the text
    expect(visible(md, { anchor: 2, caret: 15 }, 0, 'live', 2)).toBe('a ![cat](c.png) b');
    expect(visible(md, caretAt(8), 0, 'live', 2)).toBe('a ![cat](c.png) b'); // editing it
    expect(visible(md, caretAt(17), 0, 'live', 2)).toBe('a <image> b'); // the caret left it
  });

  it('reveals only the node the caret is in', () => {
    const md = '*a* and *b*';
    expect(visible(md, caretAt(1))).toBe('*a* and b');
  });

  it('hides a heading marker unless the caret is on the line', () => {
    const md = '## Title\n\nbody';
    expect(visible(md, far(md))).toBe('Title');
    expect(visible(md, caretAt(5))).toBe('## Title');
    expect(render(md, far(md)).headingLevel).toBe(2);
  });

  it('draws list and task markers as decorations', () => {
    expect(visible('- item\nx', elsewhere)).toBe('<bullet>item');
    expect(visible('1. one\nx', elsewhere)).toBe('<number>one');
    expect(visible('- [x] done\nx', elsewhere)).toBe('<checkbox>done');
    expect(visible('- [ ] todo', caretAt(3))).toBe('- [ ] todo');
  });

  it("keeps a task's checkbox while the caret edits its text, and shows the marker when it touches it", () => {
    const md = '- [ ] todo';
    expect(visible(md, caretAt(7))).toBe('<checkbox>todo'); // in the text
    expect(visible(md, caretAt(6))).toBe('<checkbox>todo'); // at the start of the text
    expect(visible(md, caretAt(5))).toBe('- [ ] todo'); // right after `]`
    expect(visible(md, caretAt(0))).toBe('- [ ] todo'); // before the bullet
    expect(visible('  - [ ] sub', caretAt(9))).toBe('<indent><checkbox>sub');
  });

  it("keeps a task's checkbox when a selection runs across or up to its marker", () => {
    const md = 'a\n- [ ] todo\nb';
    const sel = (anchor: number, caret: number): Selection => ({ anchor, caret });
    expect(visible(md, sel(0, 2), 1)).toBe('<checkbox>todo'); // dragged to the left of the checkbox
    expect(visible(md, sel(0, 13), 1)).toBe('<checkbox>todo'); // across the whole item
    expect(visible(md, sel(13, 2), 1)).toBe('<checkbox>todo'); // back up to the line start
    expect(visible(md, sel(4, 9), 1)).toBe('- [ ] todo'); // from inside the marker
  });

  it("keeps a list item's bullet or number while the caret edits its text", () => {
    expect(visible('- plain', caretAt(4))).toBe('<bullet>plain');
    expect(visible('- plain', caretAt(2))).toBe('<bullet>plain'); // at the start of the text
    expect(visible('- plain', caretAt(1))).toBe('- plain'); // right after the bullet
    expect(visible('10. ten', caretAt(6))).toBe('<number>ten');
    expect(visible('10. ten', caretAt(3))).toBe('10. ten');
    expect(visible('  - sub', caretAt(0))).toBe('  - sub'); // the line start of a nested item
    expect(visible('# Head', caretAt(4))).toBe('# Head'); // other prefixes show on the whole line
  });

  it('carries the task state on the checkbox and the line', () => {
    const r = render('- [/] doing\nx', elsewhere);
    expect(r.task).toBe('inProgress');
    expect(r.segments.find((s) => s.deco?.kind === 'checkbox')?.deco?.task).toBe('inProgress');
    expect(render('- [x] done\nx', elsewhere).className).toContain('pn-done');
  });

  it('indents a nested item by its leading whitespace', () => {
    const r = render('- a\n    - b\nx', elsewhere, 1);
    expect(r.segments[0]).toMatchObject({ concealed: true, deco: { kind: 'indent', columns: 4 } });
  });

  it('collapses fences until the selection enters the block', () => {
    const md = 'x\n```js\ncode\n```\nafter';
    expect(render(md, caretAt(0), 1).collapsed).toBe(true);
    expect(render(md, caretAt(0), 2).className).toContain('pn-codeblock');
    expect(render(md, caretAt(9), 1).collapsed).toBe(false); // caret on the code line reveals the fences
    expect(render(md, caretAt(9), 3).collapsed).toBe(false);
  });

  it('hides a setext underline off the line', () => {
    const md = 'Title\n=====\n\nbody';
    expect(render(md, far(md), 1).collapsed).toBe(true);
    expect(render(md, caretAt(8), 1).collapsed).toBe(false);
    expect(render(md, far(md), 0).headingLevel).toBe(1);
  });

  it('shows a link as its text, with the target for hover', () => {
    const md = '[home](https://x.com) end';
    const r = render(md, far(md));
    expect(visible(md, far(md))).toBe('home end');
    expect(r.segments.find((s) => !s.concealed && s.href)?.href).toBe('https://x.com');
    expect(visible(md, caretAt(2))).toBe(md);
  });

  it('draws an image in place of its source', () => {
    const md = '![alt](pic.png)';
    const r = render(md, elsewhere);
    expect(visible(md, elsewhere)).toBe('<image>');
    expect(r.segments.find((s) => s.deco?.kind === 'image')?.deco?.url).toBe('pic.png');
  });

  it('conceals markers nested in emphasis with the outer node', () => {
    const md = '***~~x~~***';
    expect(visible(md, elsewhere)).toBe('x');
    const x = render(md, elsewhere).segments.find((s) => !s.concealed)!;
    expect(x.className).toContain('pn-strike');
    expect(x.className).toContain('pn-strong');
  });

  it('shows every marker in source mode and plain text in raw mode', () => {
    const md = '# **T**';
    expect(visible(md, elsewhere, 0, 'source')).toBe(md);
    const raw = render(md, elsewhere, 0, 'raw');
    expect(raw.segments).toHaveLength(1);
    expect(raw.className).toContain('pn-raw');
  });

  it('tiles the line with segments in order', () => {
    const md = 'a **b** [c](d) `e` ==f== ~~g~~ <https://h.io> \\* x  ';
    for (const sel of [elsewhere, caretAt(3)]) {
      const r = render(md, sel);
      let pos = 0;
      for (const s of r.segments) {
        expect(s.from).toBe(pos);
        expect(s.to).toBeGreaterThan(s.from);
        pos = s.to;
      }
      expect(pos).toBe(md.length);
    }
  });

  it('ignores a trailing CR', () => {
    expect(render('# T\r\nx', elsewhere).length).toBe(3);
  });
});

describe('atomic concealed runs', () => {
  it('steps out of a concealed marker in the direction of travel', () => {
    const segs = render('## Title\n\nx', elsewhere).segments;
    expect(isInsideConcealed(segs, 1)).toBe(true);
    expect(snapOutOfConcealed(segs, 1, 1)).toBe(3);
    expect(snapOutOfConcealed(segs, 1, -1)).toBe(0);
    expect(snapOutOfConcealed(segs, 5, 1)).toBe(5);
  });
});

describe('blocks', () => {
  it('finds fenced code, front matter and tables', () => {
    const md = '---\na: 1\n---\n```\nx\n```\n| A | B |\n| - | - |\n| 1 | 2 |\npara';
    const doc = TextDocument.fromString(md);
    const blocks = findBlocks(doc, MarkdownModel.build(doc));
    expect(blocks).toEqual([
      { kind: 'frontMatter', startLine: 0, endLine: 2 },
      { kind: 'fencedCode', startLine: 3, endLine: 5 },
      { kind: 'table', startLine: 6, endLine: 8 },
    ]);
    expect(blockAtLine(blocks, 4)?.kind).toBe('fencedCode');
    expect(blockAtLine(blocks, 9)).toBeNull();
  });

  it('runs an unterminated fence to the end', () => {
    const doc = TextDocument.fromString('```\na\nb');
    expect(findBlocks(doc, MarkdownModel.build(doc))).toEqual([
      { kind: 'fencedCode', startLine: 0, endLine: 2 },
    ]);
  });
});
