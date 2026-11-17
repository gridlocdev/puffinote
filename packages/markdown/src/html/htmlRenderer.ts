import type { TextDocument } from '@puffinote/text';
import { listBase } from '../markdown/blockState.ts';
import { blockAtLine, blocksOf, type Block, type BlockKind } from '../markdown/blocks.ts';
import { isAsciiDigit } from '../markdown/chars.ts';
import { embedTarget, embedWidth } from '../markdown/embed.ts';
import { MarkdownModel } from '../markdown/markdownModel.ts';
import { taskLabel, taskStateFromMark, type TaskState } from '../markdown/taskState.ts';
import type { Token } from '../markdown/token.ts';

/** Options for an exported document. */
export interface HtmlOptions {
  /**
   * The `src` to write for an image whose Markdown destination is `src` (a picture read into a `data:` URL,
   * say), or null/undefined to keep it. The result is checked like any other image URL (PRD §10.5).
   */
  readonly image?: (src: string) => string | null | undefined;
  /**
   * The `src` for the picture a `![[target]]` wiki embed names, or null/undefined to export the embed as its
   * text. The result is checked like any other image URL (PRD §10.5).
   */
  readonly embed?: (target: string) => string | null | undefined;
}

/**
 * Renders a document to self-contained HTML (PRD §10.5). Pure and dependency-free: it walks the same token
 * stream the editor uses, so what the CLI exports matches what the editor shows. No external CSS, no network.
 */
export function renderHtmlDocument(
  document: TextDocument,
  title?: string | null,
  options: HtmlOptions = {},
): string {
  return (
    '<!DOCTYPE html>\n<html lang="en">\n<head>\n' +
    '<meta charset="utf-8">\n' +
    '<meta name="viewport" content="width=device-width, initial-scale=1">\n' +
    `<title>${escapeHtml(title ?? 'Document')}</title>\n` +
    `<style>\n${defaultCss}\n</style>\n` +
    '</head>\n<body>\n' +
    renderHtmlBody(document, options) +
    '</body>\n</html>\n'
  );
}

/** Just the body markup, without the `<html>` wrapper. */
export function renderHtmlBody(document: TextDocument, options: HtmlOptions = {}): string {
  const r = new Renderer(document, MarkdownModel.build(document), options);
  let line = 0;
  // Every block consumes at least one line, so the walk always ends.
  while (line < document.lineCount) line = Math.max(line + 1, r.block(line));
  return r.out;
}

/** Every image destination in `document`, as written (code excluded), in order and without repeats. */
export function imageSources(document: TextDocument): string[] {
  const out = new Set<string>();
  for (const t of MarkdownModel.build(document).allTokens()) {
    if (t.type === 'imageUrl' && t.meta) out.add(t.meta);
  }
  return [...out];
}

/** Escape `&`, `<` and `>`. */
export function escapeHtml(s: string): string {
  return s.replace(/[&<>]/g, (c) => (c === '&' ? '&amp;' : c === '<' ? '&lt;' : '&gt;'));
}

/** Escape for a double-quoted attribute value: `&`, `<`, `>` and `"`. Every attribute the renderer writes from
 * document text goes through this. */
function escapeAttr(s: string): string {
  return escapeHtml(s).replaceAll('"', '&quot;');
}

/**
 * The lower-cased URL scheme of `url`, or null for a scheme-less (relative, `#fragment`, `//host`) URL.
 * Browsers ignore ASCII tabs and newlines anywhere in a URL and strip leading controls and spaces, so those
 * are dropped first: `java\tscript:` is still `javascript:`.
 */
function schemeOf(url: string): string | null {
  const m = /^([a-z][a-z0-9+.-]*):/i.exec(withoutControls(url));
  return m ? m[1]!.toLowerCase() : null;
}

function withoutControls(url: string): string {
  // eslint-disable-next-line no-control-regex
  return url.replace(/[\u0000- ]/g, '');
}

/** Raster formats an exported `<img>` may embed as a `data:` URL. SVG is excluded: it is a document. */
const dataImage = /^data:image\/(png|jpeg|gif|webp|avif|bmp)[;,]/i;

/**
 * The URL an exported link or image may carry (PRD §10.5), or null to render it with no live URL. Allowed:
 * `http`, `https`, `mailto` and scheme-less URLs (relative paths, `#fragments`, protocol-relative `//host`);
 * an image may also be a raster `data:image/…` URL. Everything else (`javascript:`, `vbscript:`, `file:`,
 * other `data:` URLs, any other scheme) is dropped, matching what the editor will open (§10.3).
 */
function safeHref(raw: string, kind: 'link' | 'image'): string | null {
  const url = raw.replace(/ /g, '%20'); // `<my file.md>` keeps its spaces in the source, not in the URL
  const scheme = schemeOf(url);
  if (scheme === null || scheme === 'http' || scheme === 'https' || scheme === 'mailto') return url;
  if (kind === 'image' && scheme === 'data' && dataImage.test(withoutControls(url))) return url;
  return null;
}

function stripCr(s: string): string {
  return s.replace(/\r+$/, '');
}

/**
 * Each column's alignment from a table's delimiter row (GFM): `:--` left, `:-:` center, `--:` right, and ''
 * for none.
 */
function columnAligns(delimiterRow: string): string[] {
  let cells = delimiterRow.split('|').map((c) => c.trim());
  if (cells.length > 1 && cells[0] === '') cells = cells.slice(1);
  if (cells.length > 1 && cells[cells.length - 1] === '') cells = cells.slice(0, -1);
  return cells.map((c) => {
    const left = c.startsWith(':');
    const right = c.endsWith(':') && c.length > 1;
    return left && right ? 'center' : right ? 'right' : left ? 'left' : '';
  });
}

/** `s` without up to `columns` columns of leading blanks; a tab that crosses the limit leaves its rest as spaces. */
function stripColumns(s: string, columns: number): string {
  let col = 0;
  let k = 0;
  while (k < s.length && col < columns) {
    const c = s.charAt(k);
    if (c === ' ') col++;
    else if (c === '\t') col += 4 - (col % 4);
    else break;
    k++;
  }
  return ' '.repeat(Math.max(0, col - columns)) + s.slice(k);
}

/** How many columns of blanks `s` starts with, a tab reaching the next multiple of four. */
function leadingColumns(s: string): number {
  let col = 0;
  for (const c of s) {
    if (c === ' ') col++;
    else if (c === '\t') col += 4 - (col % 4);
    else break;
  }
  return col;
}

class Renderer {
  out = '';
  private readonly doc: TextDocument;
  private readonly model: MarkdownModel;
  private readonly options: HtmlOptions;
  /** Front matter, fenced code and tables, found as the editor finds them, so both agree where each ends. */
  private readonly blocks: readonly Block[];

  constructor(doc: TextDocument, model: MarkdownModel, options: HtmlOptions) {
    this.doc = doc;
    this.model = model;
    this.options = options;
    this.blocks = blocksOf(model);
  }

  /** The last line of the `kind` block that starts at `line`, or null when none starts there. */
  private blockEnd(line: number, kind: BlockKind): number | null {
    const b = blockAtLine(this.blocks, line);
    return b && b.kind === kind && b.startLine === line ? b.endLine : null;
  }

  /** Render the block beginning at `line`; return the next unconsumed line. */
  block(line: number): number {
    if (this.isBlank(line)) return line + 1;
    const first = this.first(line);

    switch (first?.type) {
      // A setext underline is markup for the heading above it, already rendered.
      case 'setextUnderline':
        return line + 1;
      case 'frontMatterFence':
        return this.frontMatter(line);
      case 'codeFence':
        return this.codeBlock(line);
      case 'codeText':
        // Outside a fence, a code line is indented code (the fence's own lines are taken by codeBlock).
        return this.indentedCode(line);
      case 'headingMarker': {
        const level = Number.parseInt(first.meta ?? '', 10) || 1;
        this.out += `<h${level}>`;
        this.inline(this.model.lineTokens(line));
        this.out += `</h${level}>\n`;
        return line + 1;
      }
      case 'horizontalRule':
        this.out += '<hr>\n';
        return line + 1;
      case 'blockquoteMarker':
        return this.quote(line);
      case 'listMarker':
        return this.listLevel(line, this.listIndent(line), 0, 0);
      case 'autolink':
      case 'embed':
      case 'emphasis':
      case 'escape':
      case 'frontMatterText':
      case 'hardBreak':
      case 'highlight':
      case 'imageAlt':
      case 'imageUrl':
      case 'inlineCode':
      case 'linkUrl':
      case 'marker':
      case 'strikethrough':
      case 'strong':
      case 'strongEmphasis':
      case 'tableDelimiter':
      case 'taskMarker':
      case 'text':
      case 'wikiLink':
      case undefined:
        break;
    }

    const tableEnd = this.blockEnd(line, 'table');
    if (tableEnd !== null) return this.table(line, tableEnd);
    return this.paragraph(line);
  }

  /** Front matter renders as a collapsed "Properties" block (PRD §10.2). */
  private frontMatter(line: number): number {
    const end = this.blockEnd(line, 'frontMatter') ?? this.doc.lineCount - 1;
    let yaml = '';
    for (let i = line + 1; i <= end && this.first(i)?.type !== 'frontMatterFence'; i++) {
      yaml += escapeHtml(stripCr(this.doc.getLineText(i))) + '\n';
    }
    this.out += `<details class="properties"><summary>Properties</summary><pre>${yaml}</pre></details>\n`;
    return end + 1;
  }

  private codeBlock(line: number): number {
    const lang = this.first(line)?.meta;
    const end = this.blockEnd(line, 'fencedCode') ?? this.doc.lineCount - 1;
    let code = '';
    // The block ends at its closing fence, or at the end of the note when the fence is never closed.
    for (let i = line + 1; i <= end; i++) {
      if (i === end && this.first(i)?.type === 'codeFence') break;
      code += escapeHtml(stripCr(this.doc.getLineText(i))) + '\n';
    }
    const cls = lang ? ` class="language-${escapeAttr(lang)}"` : '';
    this.out += `<pre><code${cls}>${code}</code></pre>\n`;
    return end + 1;
  }

  /**
   * An indented code block: its code lines and the blank lines between them, each without the four columns
   * of indent (counted past the list item it sits in) that made it code.
   */
  private indentedCode(line: number): number {
    const n = this.doc.lineCount;
    let code = '';
    let blanks = '';
    let i = line;
    while (i < n) {
      if (this.first(i)?.type === 'codeText') {
        const strip = listBase(this.model.enteringState(i)) + 4;
        code += blanks + escapeHtml(stripColumns(stripCr(this.doc.getLineText(i)), strip)) + '\n';
        blanks = '';
      } else if (this.isBlank(i) && this.model.enteringState(i).kind === 'indentedCode') {
        blanks += '\n'; // kept only when another code line follows
      } else {
        break;
      }
      i++;
    }
    this.out += `<pre><code>${code}</code></pre>\n`;
    return i;
  }

  /**
   * A run of quoted lines, opening nested `<blockquote>`s down to each line's depth and closing back up from
   * a deeper one. Inside, the text after the `>` prefix is classified like a line of its own: a bare `>`
   * ends a paragraph, list items make a list, `#` makes a heading, and consecutive prose lines at one depth
   * are one paragraph with a `<br>` per line break (PRD §10.5). Iterative, so any depth renders without
   * recursion.
   */
  private quote(line: number): number {
    const n = this.doc.lineCount;
    let i = line;
    let open = 0;
    while (i < n) {
      const d = this.quoteDepth(i);
      if (d < 1) break;
      for (; open < d; open++) this.out += '<blockquote>\n';
      for (; open > d; open--) this.out += '</blockquote>\n';
      if (this.isBlankIn(i, d)) {
        i++;
        continue;
      }
      if (this.isListLine(i, d)) {
        i = this.listLevel(i, this.listIndent(i), 0, d);
        continue;
      }
      const level = this.quotedHeadingLevel(i);
      if (level > 0) {
        this.out += `<h${level}>`;
        this.inline(this.model.lineTokens(i));
        this.out += `</h${level}>\n`;
        i++;
        continue;
      }
      this.out += '<p>';
      let first = true;
      while (
        i < n &&
        this.quoteDepth(i) === d &&
        !this.isBlankIn(i, d) &&
        !this.isListLine(i, d) &&
        this.quotedHeadingLevel(i) === 0
      ) {
        if (!first) this.out += '<br>\n';
        this.inline(this.model.lineTokens(i));
        first = false;
        i++;
      }
      this.out += '</p>\n';
    }
    for (; open > 0; open--) this.out += '</blockquote>\n';
    return i;
  }

  /** The level of an ATX heading after a quote prefix (`> ## Note`), or 0. */
  private quotedHeadingLevel(line: number): number {
    const [prefix, heading] = this.model.lineTokens(line);
    if (prefix?.type !== 'blockquoteMarker' || heading?.type !== 'headingMarker') return 0;
    return Number.parseInt(heading.meta ?? '', 10) || 0;
  }

  private quoteDepth(line: number): number {
    const t = this.first(line);
    return t?.type === 'blockquoteMarker' ? Number.parseInt(t.meta ?? '', 10) || 0 : 0;
  }

  /**
   * One list level: items at `levelIndent` are siblings, a deeper run nests inside the preceding item, and a
   * shallower item returns to the caller. Blank lines between items keep them in one list. A numbered list
   * `depth` levels in counts as the editor draws it: 1, 2, 3, then a, b, c, then i, ii, iii (PRD §10.2).
   * `quote` is the blockquote depth the list sits at: only items at that depth belong to it.
   */
  private listLevel(line: number, levelIndent: number, depth: number, quote: number): number {
    const n = this.doc.lineCount;
    const ordered = this.isOrdered(line);
    const start = ordered ? this.startNumber(line) : 1;
    const type = (['', ' type="a"', ' type="i"'] as const)[depth % 3];
    this.out += ordered ? (start !== 1 ? `<ol${type} start="${start}">\n` : `<ol${type}>\n`) : '<ul>\n';

    let i = line;
    while (i < n) {
      if (this.isBlankIn(i, quote)) {
        let j = i;
        while (j < n && this.isBlankIn(j, quote)) j++;
        if (j < n && this.isListLine(j, quote) && this.listIndent(j) >= levelIndent) {
          i = j;
          continue;
        }
        break;
      }
      if (!this.isListLine(i, quote)) break;
      const indent = this.listIndent(i);
      if (indent !== levelIndent) break; // shallower: an outer list's; deeper with no sibling: the caller's

      const column = this.itemColumn(i);
      const tokens = this.model.lineTokens(i);
      const at = tokens.indexOf(this.listMarkerOf(i)!);
      const box = tokens[at + 1]?.type === 'taskMarker' ? tokens[at + 1]! : null;
      if (box) {
        // Three states, not a bit: the export draws the same box the editor does (PRD §11.4). The text is
        // wrapped so a done item can be struck through without the rule crossing its box.
        const state = taskStateFromMark(box.meta?.charAt(0) || ' ');
        this.out +=
          `<li class="task ${taskClass(state)}"><span class="tbox" role="img" aria-label="${taskLabel(state)}">` +
          `${taskGlyph(state)}</span><span class="ttext">`;
      } else {
        this.out += '<li>';
      }
      this.inline(tokens);
      i++;
      // Continuation lines straight after the item belong to it, a line break each (PRD §10.5).
      while (i < n && this.isContinuation(i, quote)) {
        this.out += '<br>\n';
        this.inline(this.model.lineTokens(i));
        i++;
      }
      if (box) this.out += '</span>';

      // A deeper item, right below or past blank lines, nests inside this one. So does any other block
      // indented to the item's text, such as a paragraph or code after a blank line (PRD §10.5).
      for (;;) {
        let j = i;
        while (j < n && this.isBlankIn(j, quote)) j++;
        if (j >= n) break;
        if (this.isListLine(j, quote) && this.listIndent(j) > levelIndent) {
          if (!this.out.endsWith('\n')) this.out += '\n';
          i = this.listLevel(j, this.listIndent(j), depth + 1, quote);
        } else if (
          quote === 0 &&
          !this.isListLine(j, 0) &&
          leadingColumns(this.doc.getLineText(j)) >= column
        ) {
          if (!this.out.endsWith('\n')) this.out += '\n';
          i = this.block(j);
        } else {
          break;
        }
      }
      this.out += '</li>\n';
    }

    this.out += ordered ? '</ol>\n' : '</ul>\n';
    return i;
  }

  /**
   * Whether `line` continues the list item above it: prose at the same quote depth that opens no block of
   * its own (a hard-wrapped item's second line, or an indented one).
   */
  private isContinuation(line: number, quote: number): boolean {
    if (quote === 0) return this.isParagraphLine(line);
    const tokens = this.model.lineTokens(line);
    const second = tokens[1];
    return (
      this.quoteDepth(line) === quote &&
      second !== undefined &&
      second.type !== 'listMarker' &&
      second.type !== 'headingMarker'
    );
  }

  /**
   * The column a list item's own blocks are indented to: the content column the model opens for the item on
   * `line` (just past its marker). Infinite when nothing follows, so no line counts as inside it.
   */
  private itemColumn(line: number): number {
    if (line + 1 >= this.doc.lineCount) return Number.POSITIVE_INFINITY;
    const columns = this.model.enteringState(line + 1).listColumns;
    return columns.length > 0 ? columns[columns.length - 1]! : Number.POSITIVE_INFINITY;
  }

  /** The line's list marker: its first token, or the one right after its blockquote prefix. */
  private listMarkerOf(line: number): Token | undefined {
    const tokens = this.model.lineTokens(line);
    const t = tokens[0]?.type === 'blockquoteMarker' ? tokens[1] : tokens[0];
    return t?.type === 'listMarker' ? t : undefined;
  }

  /** A list item at blockquote depth `quote`. */
  private isListLine(line: number, quote: number): boolean {
    return this.quoteDepth(line) === quote && this.listMarkerOf(line) !== undefined;
  }

  /** Blank at blockquote depth `quote`: whitespace only, or only the quote's own prefix (`>`). */
  private isBlankIn(line: number, quote: number): boolean {
    if (quote === 0) return this.isBlank(line);
    const tokens = this.model.lineTokens(line);
    return this.quoteDepth(line) === quote && tokens.length === 1;
  }

  /**
   * The column of a list marker within its container: from the line start, or inside a quote from just past
   * the last `>` and the one space after it, so `>   - b` sits two columns in under `> - a`.
   */
  private listIndent(line: number): number {
    const marker = this.listMarkerOf(line);
    if (!marker) return 0;
    const first = this.first(line)!;
    if (first.type !== 'blockquoteMarker') return marker.start - this.doc.getLineStart(line);
    const prefix = this.doc.getText(first.start, first.length);
    let base = first.start + prefix.lastIndexOf('>') + 1;
    if (base < marker.start && /[ \t]/.test(this.doc.getText(base, 1))) base++;
    return marker.start - base;
  }

  private markerText(line: number): string {
    const marker = this.listMarkerOf(line);
    return marker ? this.doc.getText(marker.start, marker.length) : '';
  }

  private isOrdered(line: number): boolean {
    return isAsciiDigit(this.markerText(line).charAt(0));
  }

  private startNumber(line: number): number {
    const digits = /^\d*/.exec(this.markerText(line))?.[0] ?? '';
    return digits.length > 0 ? Number.parseInt(digits, 10) : 1;
  }

  /** The table block starting at `line` (its header row), through its last body row. */
  private table(line: number, end: number): number {
    const aligns = columnAligns(stripCr(this.doc.getLineText(line + 1)));
    this.out += '<table>\n<thead>\n';
    this.tableRow(this.model.lineTokens(line), 'th', aligns);
    this.out += '</thead>\n';
    // Past the header and delimiter rows.
    if (line + 2 <= end) {
      this.out += '<tbody>\n';
      for (let i = line + 2; i <= end; i++) this.tableRow(this.model.lineTokens(i), 'td', aligns);
      this.out += '</tbody>\n';
    }
    this.out += '</table>\n';
    return end + 1;
  }

  /** Split a row's tokens into cells at the `|` markers, then render each cell's inline run. */
  private tableRow(tokens: readonly Token[], tag: string, aligns: readonly string[]): void {
    this.out += '<tr>';
    let cellStart = 0;
    let column = 0;
    for (let i = 0; i <= tokens.length; i++) {
      const t = tokens[i];
      const isPipe = t !== undefined && this.isPipe(t);
      if (isPipe || i === tokens.length) {
        if (i > cellStart || (isPipe && cellStart !== 0)) {
          const outer = this.out;
          this.out = '';
          this.inline(tokens, cellStart, i);
          const content = this.out.trim();
          this.out = outer;
          if (content.length > 0 || (i !== tokens.length && cellStart !== 0)) {
            const align = aligns[column++];
            const style = align ? ` style="text-align: ${align}"` : '';
            this.out += `<${tag}${style}>${content}</${tag}>`;
          }
        }
        cellStart = i + 1;
      }
    }
    this.out += '</tr>\n';
  }

  /** A paragraph from `line`, which it always takes, through the lines that continue it. */
  private paragraph(line: number): number {
    const n = this.doc.lineCount;
    this.out += '<p>';
    let i = line;
    let firstLine = true;
    while (i < n && (firstLine || (!this.isBlank(i) && this.isParagraphLine(i)))) {
      // Every line break inside a paragraph is kept as a <br>, as the editor shows it (PRD §10.5).
      if (!firstLine) this.out += '<br>\n';
      this.inline(this.model.lineTokens(i));
      firstLine = false;
      i++;
    }
    this.out += '</p>\n';
    return i;
  }

  // ----- inline rendering: pairs link and image structure out of the flat token stream -----

  /** Render `tokens[from, to)`. */
  private inline(tokens: readonly Token[], from = 0, to = tokens.length): void {
    let i = from;
    while (i < to) {
      const t = tokens[i]!;
      if (t.type === 'marker') {
        const text = this.doc.getText(t.start, t.length);
        if (text === '[') {
          i = this.link(tokens, i, to);
          continue;
        }
        if (text === '![') {
          i = this.image(tokens, i, to);
          continue;
        }
        i++; // emphasis, code, escape, autolink, wiki and embed punctuation is concealed
        continue;
      }
      if (t.role === 'marker') {
        i++; // block markers (heading #, bullet, quote >, task box) aren't text
        continue;
      }
      this.content(t);
      i++;
    }
  }

  /**
   * Tokens: `[` (node N), the link text (other nodes), then `](`, `linkUrl` and `)` (node N). The parts are
   * found by the construct's node id, not by position, because a zero-length part (an empty URL) emits no
   * token. An unsafe destination (§10.5) renders the text without a link.
   */
  private link(tokens: readonly Token[], i: number, to: number): number {
    const node = tokens[i]!.node;
    let mid = i + 1;
    while (mid < to && tokens[mid]!.node !== node) mid++;
    let end = mid;
    let href = '';
    for (; end < to && tokens[end]!.node === node; end++) {
      if (tokens[end]!.type === 'linkUrl') href = tokens[end]!.meta ?? '';
    }
    const safe = safeHref(href, 'link');
    if (safe !== null) this.out += `<a href="${escapeAttr(safe)}">`;
    this.inline(tokens, i + 1, mid);
    if (safe !== null) this.out += '</a>';
    return end;
  }

  /** Tokens, all of node N: `![` imageAlt `](` imageUrl `)`; the alt and URL are absent when empty. */
  private image(tokens: readonly Token[], i: number, to: number): number {
    const node = tokens[i]!.node;
    let end = i + 1;
    let alt = '';
    let src = '';
    for (; end < to && tokens[end]!.node === node; end++) {
      const t = tokens[end]!;
      if (t.type === 'imageAlt') alt = t.meta ?? '';
      else if (t.type === 'imageUrl') src = t.meta ?? '';
    }
    const safe = safeHref(this.options.image?.(src) ?? src, 'image');
    this.out += safe !== null ? `<img src="${escapeAttr(safe)}" alt="${escapeAttr(alt)}">` : escapeHtml(alt);
    return end;
  }

  private content(t: Token): void {
    const text = escapeHtml(this.doc.getText(t.start, t.length));
    switch (t.type) {
      case 'emphasis':
        this.out += `<em>${text}</em>`;
        break;
      case 'strong':
        this.out += `<strong>${text}</strong>`;
        break;
      case 'strongEmphasis':
        this.out += `<strong><em>${text}</em></strong>`;
        break;
      case 'strikethrough':
        this.out += `<del>${text}</del>`;
        break;
      case 'highlight':
        this.out += `<mark>${text}</mark>`;
        break;
      case 'inlineCode':
        this.out += `<code>${text}</code>`;
        break;
      case 'autolink': {
        // `<user@host>` has no scheme: it is an email autolink (CommonMark §6.5).
        const target = t.meta ?? this.doc.getText(t.start, t.length);
        const safe = safeHref(schemeOf(target) === null ? `mailto:${target}` : target, 'link');
        this.out += safe !== null ? `<a href="${escapeAttr(safe)}">${text}</a>` : text;
        break;
      }
      case 'wikiLink':
        this.out += `<span class="wikilink">${text}</span>`;
        break;
      case 'embed': {
        const target = embedTarget(t.meta);
        const src = target ? this.options.embed?.(target) : null;
        const safe = src ? safeHref(src, 'image') : null;
        if (safe === null) {
          this.out += `<span class="embed">${text}</span>`;
          break;
        }
        const width = embedWidth(t.meta);
        this.out += `<img src="${escapeAttr(safe)}" alt="${escapeAttr(target)}"${width ? ` width="${width}"` : ''}>`;
        break;
      }
      case 'blockquoteMarker':
      case 'codeFence':
      case 'codeText':
      case 'escape':
      case 'frontMatterFence':
      case 'frontMatterText':
      case 'hardBreak':
      case 'headingMarker':
      case 'horizontalRule':
      case 'imageAlt':
      case 'imageUrl':
      case 'linkUrl':
      case 'listMarker':
      case 'marker':
      case 'setextUnderline':
      case 'tableDelimiter':
      case 'taskMarker':
      case 'text':
        this.out += text;
    }
  }

  // ----- line classification -----

  private first(line: number): Token | undefined {
    return this.model.lineTokens(line)[0];
  }

  /** Empty or whitespace only: the tokenizer gives such a line no tokens and treats it as blank. */
  private isBlank(line: number): boolean {
    return this.doc.getLineText(line).trim().length === 0;
  }

  private isParagraphLine(line: number): boolean {
    const first = this.first(line);
    if (!first) return false;
    switch (first.type) {
      case 'headingMarker':
      case 'blockquoteMarker':
      case 'listMarker':
      case 'horizontalRule':
      case 'codeFence':
      case 'frontMatterFence':
      case 'tableDelimiter':
      case 'setextUnderline':
      case 'codeText':
        return false;
      case 'autolink':
      case 'embed':
      case 'emphasis':
      case 'escape':
      case 'frontMatterText':
      case 'hardBreak':
      case 'highlight':
      case 'imageAlt':
      case 'imageUrl':
      case 'inlineCode':
      case 'linkUrl':
      case 'marker':
      case 'strikethrough':
      case 'strong':
      case 'strongEmphasis':
      case 'taskMarker':
      case 'text':
      case 'wikiLink':
        return this.blockEnd(line, 'table') === null;
    }
  }

  private isPipe(t: Token): boolean {
    return t.type === 'marker' && t.length === 1 && this.doc.getText(t.start, 1) === '|';
  }
}

/** The CSS class that carries a task item's state (see `defaultCss`). */
function taskClass(state: TaskState): string {
  return state === 'inProgress' ? 't-prog' : state === 'done' ? 't-done' : 't-not';
}

/**
 * The symbol inside a task box, as inline SVG in `currentColor`; a resting box draws nothing. The path data
 * and stroke widths are the ones the editor surface draws; keep them in step.
 */
function taskGlyph(state: TaskState): string {
  const path =
    state === 'inProgress'
      ? '<path d="M6.7 12 L17.3 12" stroke="currentColor" stroke-width="2.8" stroke-linecap="round"/>'
      : state === 'done'
        ? '<path d="M6.5 12.6 L10.2 16.3 L17.6 7.9" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/>'
        : '';
  return path ? `<svg viewBox="0 0 24 24" fill="none" aria-hidden="true">${path}</svg>` : '';
}

const defaultCss = `:root { color-scheme: light dark; }
body { max-width: 44rem; margin: 2rem auto; padding: 0 1rem;
       font: 16px/1.6 -apple-system, "Segoe UI", system-ui, sans-serif; }
h1, h2, h3, h4, h5, h6 { line-height: 1.25; }
code { font-family: ui-monospace, "SF Mono", Menlo, monospace; font-size: 0.9em;
       background: rgba(127,127,127,0.15); padding: 0.1em 0.3em; border-radius: 4px; }
pre { background: rgba(127,127,127,0.12); padding: 1rem; border-radius: 8px; overflow:auto; }
pre code { background: none; padding: 0; }
blockquote { border-left: 3px solid rgba(127,127,127,0.5); margin: 0; padding: 0 1rem; color: #666; }
mark { background: #fdf6b2; color: #1f1f1f; }
a { color: #0b62d6; }
@media screen and (prefers-color-scheme: dark) {
  blockquote { color: #a8a8a8; }
  mark { background: #6b5a12; color: inherit; }
  a { color: #6aa6ff; }
}
table { border-collapse: collapse; }
th, td { border: 1px solid rgba(127,127,127,0.4); padding: 0.4em 0.7em; }
.properties { color: #888; font-size: 0.85em; }
img { max-width: 100%; }

/* Task states (PRD §11.4). One accent, single-hue: the dash and the check share it, so "under way" and
   "done" read as two points on one path. A resting box is a bare outline in the quiet border colour. */
:root { --t-border:#B1B8C2; --t-accent:#216BE0; --t-check:#fff; --t-off:#6E7781; }
@media screen and (prefers-color-scheme: dark) {
  :root { --t-border:#5A6272; --t-accent:#3B82F6; --t-check:#fff; --t-off:#9AA3B5; }
}
li.task { list-style: none; margin-left: -1.35em; }
li.task > .tbox { position: relative; display: inline-block; box-sizing: border-box;
                  width: 1.15em; height: 1.15em; margin-right: 0.45em; vertical-align: -0.22em;
                  border: 1.5px solid var(--t-border); border-radius: 0.32em; }
li.task > .tbox svg { position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%);
                      width: 1.15em; height: 1.15em; display: block; }
li.t-prog > .tbox { color: var(--t-accent); }
li.t-done > .tbox { background: var(--t-accent); border-color: var(--t-accent); color: var(--t-check); }
li.t-done > .ttext { color: var(--t-off); text-decoration: line-through; }

/* Print and PDF (PRD §10.5): light, the page's own margins, and no break inside a code block or picture. */
@page { margin: 18mm 16mm; }
@media print {
  :root { color-scheme: light; }
  body { max-width: none; margin: 0; padding: 0; font-size: 11pt; }
  pre { white-space: pre-wrap; overflow-wrap: anywhere; overflow: visible; }
  pre, img, tr, blockquote, li.task { break-inside: avoid; }
  h1, h2, h3, h4, h5, h6 { break-after: avoid; }
  a { color: inherit; }
}`;
