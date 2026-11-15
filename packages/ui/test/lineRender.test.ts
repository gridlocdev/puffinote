import { blockAtLine, blocksOf, MarkdownModel } from '@puffinote/markdown';
import { caretAt, TextDocument, type Selection } from '@puffinote/text';
import { describe, expect, it } from 'vitest';
import {
  ListDepthTracker,
  listOrdinal,
  pictureExtent,
  pictureWithUrlAt,
  isInsideConcealed,
  lineReveal,
  keepReveal,
  checkboxLabel,
  lineAnnouncement,
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
  const blocks = blocksOf(model);
  const block = blockAtLine(blocks, line);
  const range = block
    ? { start: doc.getLineStart(block.startLine), end: doc.getLineEnd(block.endLine) }
    : null;
  const start = doc.getLineStart(line);
  const tokens = model.lineTokensRelative(line);
  const reveal = lineReveal(tokens, start, doc.getLineEnd(line), sel, range, shownPicture);
  return renderLine(doc.getLineText(line), tokens, reveal, mode, block?.kind ?? null);
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

  it('draws a heading inside a quote as a heading, hiding both markers off the line', () => {
    const md = '> ## Note ##\n\nbody';
    expect(visible(md, far(md))).toBe('Note');
    expect(visible(md, caretAt(4))).toBe('> ## Note ##');
    const r = render(md, far(md));
    expect(r.headingLevel).toBe(2);
    expect(r.className).toContain('pn-h2');
    expect(r.className).toContain('pn-quote');
    expect(render('> #hashtag', far(md)).headingLevel).toBe(0);
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

  it('counts a tab in a nested item’s indent to the next tab stop, as the tokenizer does', () => {
    const indent = (md: string) => render(md, elsewhere, 1).segments[0]?.deco;
    expect(indent('- a\n\t- b\nx')).toMatchObject({ kind: 'indent', columns: 4 });
    expect(indent('- a\n \t- b\nx')).toMatchObject({ kind: 'indent', columns: 4 });
    expect(indent('- a\n  \t- b\nx')).toMatchObject({ kind: 'indent', columns: 4 });
    expect(indent('- a\n\t  - b\nx')).toMatchObject({ kind: 'indent', columns: 6 });
    expect(indent('- a\n    \t- b\nx')).toMatchObject({ kind: 'indent', columns: 8 });
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
    const blocks = blocksOf(MarkdownModel.build(doc));
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
    expect(blocksOf(MarkdownModel.build(doc))).toEqual([{ kind: 'fencedCode', startLine: 0, endLine: 2 }]);
  });
});

describe('nested numbered lists (PRD §10.2)', () => {
  it('counts 1, 2, 3, then a, b, c, then i, ii, iii by depth', () => {
    expect([1, 2, 10].map((n) => listOrdinal(n, 0))).toEqual(['1', '2', '10']);
    expect([1, 3, 26, 27, 28].map((n) => listOrdinal(n, 1))).toEqual(['a', 'c', 'z', 'aa', 'ab']);
    expect([1, 4, 9, 14, 40].map((n) => listOrdinal(n, 2))).toEqual(['i', 'iv', 'ix', 'xiv', 'xl']);
    expect(listOrdinal(2, 3)).toBe('2');
  });

  it('draws a nested item number as a letter, keeping its delimiter', () => {
    const doc = TextDocument.fromString('1. a\n    1. b\n    2) c');
    const model = MarkdownModel.build(doc);
    const deco = (line: number, depth: number) => {
      const start = doc.getLineStart(line);
      const tokens = model.lineTokensRelative(line);
      const reveal = lineReveal(tokens, start, doc.getLineEnd(line), elsewhere, null, null);
      const r = renderLine(doc.getLineText(line), tokens, reveal, 'live', null, depth);
      return r.segments.find((s) => s.deco?.kind === 'number')?.deco?.text;
    };
    expect(deco(0, 0)).toBe('1.');
    expect(deco(1, 1)).toBe('a.');
    expect(deco(2, 1)).toBe('b)');
  });

  it('draws a bullet as a disc, a circle, then a square by depth', () => {
    const doc = TextDocument.fromString('- a');
    const model = MarkdownModel.build(doc);
    const tokens = model.lineTokensRelative(0);
    const reveal = lineReveal(tokens, 0, doc.getLineEnd(0), elsewhere, null, null);
    const glyph = (depth: number) =>
      renderLine('- a', tokens, reveal, 'live', null, depth).segments.find((s) => s.deco?.kind === 'bullet')
        ?.deco?.text;
    expect([0, 1, 2, 3].map(glyph)).toEqual(['•', '◦', '▪', '•']);
  });

  /** Each line's depth, as the editor walks the note: a table drawn whole is skipped. */
  const depths = (lines: string[], skipTables = false) => {
    const doc = TextDocument.fromString(lines.join('\n'));
    const model = MarkdownModel.build(doc);
    const blocks = blocksOf(model);
    const t = new ListDepthTracker();
    const out: number[] = [];
    for (let i = 0; i < doc.lineCount; i++) {
      const block = blockAtLine(blocks, i);
      if (skipTables && block?.kind === 'table') {
        t.skipBlock();
        out.push(-1);
        continue;
      }
      out.push(t.next(doc.getLineText(i), model.lineTokensRelative(i), block?.kind ?? null));
    }
    return out;
  };

  it('tracks how deep each item sits', () => {
    const lines = ['1. a', '    1. b', '        - c', '', '    2. d', '2. e', 'para', '    1. f'];
    expect(depths(lines)).toEqual([0, 1, 2, 0, 1, 0, 0, 0]);
  });

  it('counts no line inside a block as an item, and ends lists at a block drawn whole', () => {
    expect(depths(['- a', '  - b', '```', '- x', '```', '  - c'])).toEqual([0, 1, 0, 0, 0, 0]);
    expect(depths(['- a', '| h |', '| - |', '  - b'], true)).toEqual([0, -1, -1, 0]);
  });

  it('measures depth in columns, so a tab and four spaces nest alike', () => {
    expect(depths(['- a', '\t- b', '    - c', '\t\t- d', ' \t- e'])).toEqual([0, 1, 1, 2, 1]);
    expect(depths(['- a', '\t- b', '  - c'])).toEqual([0, 1, 1]);
  });
});

describe('shared reveals', () => {
  const reveals = (md: string, sels: Selection[]) => {
    const doc = TextDocument.fromString(md);
    const model = MarkdownModel.build(doc);
    const tokens = model.lineTokensRelative(0);
    return sels.map((sel) => lineReveal(tokens, 0, doc.getLineEnd(0), sel, null, null));
  };

  it('gives an active line with nothing inline to reveal a shared object', () => {
    const [a, b] = reveals('plain text', [caretAt(2), { anchor: 0, caret: 10 }]);
    expect(a).toBe(b);
  });

  it('keeps the previous reveal while the selection reveals the same markers', () => {
    const [a, b, c] = reveals('a *b* c', [{ anchor: 0, caret: 7 }, { anchor: 0, caret: 6 }, caretAt(0)]);
    expect(a).not.toBe(b);
    expect(keepReveal(a, b!)).toBe(a);
    expect(keepReveal(a, c!)).toBe(c);
    expect(keepReveal(undefined, c!)).toBe(c);
  });
});

describe('screen reader text', () => {
  it("names a checkbox after its task's text and state", () => {
    expect(checkboxLabel('- [ ] Buy milk', 'notStarted')).toBe('Buy milk, not done');
    expect(checkboxLabel('    - [x] **Ship** it', 'done')).toBe('**Ship** it, done');
    expect(checkboxLabel('> - [/] half', 'inProgress')).toBe('half, in progress');
    expect(checkboxLabel('- [ ] ', 'notStarted')).toBe('Empty task, not done');
  });

  it('announces a line by its text, and a blank line as blank', () => {
    expect(lineAnnouncement('## Plans', 3)).toBe('Line 4: ## Plans');
    expect(lineAnnouncement('   ', 0)).toBe('Line 1: blank');
  });
});

describe('pictures in the source', () => {
  const md = 'intro\nsee ![a puffin](birds/puffin.png "Puffin") and ![b](<odd name.png>) done';
  const doc = TextDocument.fromString(md);
  const tokens = MarkdownModel.build(doc).lineTokens(1);
  const url = (n: number) => tokens.filter((t) => t.type === 'imageUrl')[n]!.start;

  it('finds the whole picture from where its link starts', () => {
    const first = pictureWithUrlAt(tokens, url(0), doc)!;
    expect(md.slice(first.start, first.end)).toBe('![a puffin](birds/puffin.png "Puffin")');
    expect(first.url).toBe('birds/puffin.png');
    const second = pictureWithUrlAt(tokens, url(1), doc)!;
    expect(md.slice(second.start, second.end)).toBe('![b](<odd name.png>)');
    expect(pictureWithUrlAt(tokens, url(0) + 1, doc)).toBeNull();
  });

  it('gives the same range as the picture covering an offset', () => {
    const first = pictureWithUrlAt(tokens, url(0), doc)!;
    expect(pictureExtent(tokens, first.start + 3)).toEqual({ start: first.start, end: first.end });
    expect(pictureExtent(tokens, doc.getLineStart(1))).toBeNull();
  });
});

describe('wiki embeds of pictures (PRD §10.6)', () => {
  const far = { anchor: 99, caret: 99 };
  it('draws a picture embed as a picture, with the width it asks for', () => {
    const md = 'x\n![[Pasted image 1.png|300]]';
    const deco = render(md, { anchor: 0, caret: 0 }, 1).segments.find((s) => s.deco?.kind === 'image')?.deco;
    expect(deco).toEqual({ kind: 'image', text: '', url: '[[Pasted image 1.png]]', width: 300 });
  });

  it('leaves an embed of a note as styled text', () => {
    const r = render('![[Other note]]', far);
    expect(r.segments.some((s) => s.deco?.kind === 'image')).toBe(false);
  });

  it('finds the embed under the caret like any picture', () => {
    const md = '![[a.png]]';
    const doc = TextDocument.fromString(md);
    const tokens = MarkdownModel.build(doc).lineTokens(0);
    expect(pictureExtent(tokens, 4)).toEqual({ start: 0, end: md.length });
    expect(pictureWithUrlAt(tokens, 3, doc)).toEqual({ start: 0, end: md.length, url: '[[a.png]]' });
  });
});
