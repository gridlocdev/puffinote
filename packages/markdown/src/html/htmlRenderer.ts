import type { TextDocument } from '@puffinote/text';
import { listBase } from '../markdown/blockState.ts';
import { isAsciiDigit } from '../markdown/chars.ts';
import { MarkdownModel } from '../markdown/markdownModel.ts';
import { taskLabel, taskStateFromMark, type TaskState } from '../markdown/taskState.ts';
import type { Token } from '../markdown/token.ts';

/**
 * Renders a document to self-contained HTML (PRD §10.5). Pure and dependency-free: it walks the same token
 * stream the editor uses, so what the CLI exports matches what the editor shows. No external CSS, no network.
 */
export function renderHtmlDocument(document: TextDocument, title?: string | null): string {
  return (
    '<!DOCTYPE html>\n<html lang="en">\n<head>\n' +
    '<meta charset="utf-8">\n' +
    '<meta name="viewport" content="width=device-width, initial-scale=1">\n' +
    `<title>${escapeHtml(title ?? 'Document')}</title>\n` +
    `<style>\n${defaultCss}\n</style>\n` +
    '</head>\n<body>\n' +
    renderHtmlBody(document) +
    '</body>\n</html>\n'
  );
}

/** Just the body markup, without the `<html>` wrapper. */
export function renderHtmlBody(document: TextDocument): string {
  const r = new Renderer(document, MarkdownModel.build(document));
  let line = 0;
  // Every block consumes at least one line, so the walk always ends.
  while (line < document.lineCount) line = Math.max(line + 1, r.block(line));
  return r.out;
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

class Renderer {
  out = '';
  private readonly doc: TextDocument;
  private readonly model: MarkdownModel;

  constructor(doc: TextDocument, model: MarkdownModel) {
    this.doc = doc;
    this.model = model;
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
    }

    if (this.isTableStart(line)) return this.table(line);
    return this.paragraph(line);
  }

  /** Front matter renders as a collapsed "Properties" block (PRD §10.2). */
  private frontMatter(line: number): number {
    const n = this.doc.lineCount;
    let i = line + 1;
    let yaml = '';
    while (i < n && this.first(i)?.type !== 'frontMatterFence') {
      yaml += escapeHtml(stripCr(this.doc.getLineText(i))) + '\n';
      i++;
    }
    if (i < n) i++;
    this.out += `<details class="properties"><summary>Properties</summary><pre>${yaml}</pre></details>\n`;
    return i;
  }

  private codeBlock(line: number): number {
    const lang = this.first(line)?.meta;
    const n = this.doc.lineCount;
    let i = line + 1;
    let code = '';
    // The closing fence is entered in fenced-code state too; it is the line whose token is a fence.
    while (
      i < n &&
      this.model.enteringState(i).kind === 'fencedCode' &&
      this.first(i)?.type !== 'codeFence'
    ) {
      code += escapeHtml(stripCr(this.doc.getLineText(i))) + '\n';
      i++;
    }
    if (i < n && this.first(i)?.type === 'codeFence') i++;
    const cls = lang ? ` class="language-${escapeAttr(lang)}"` : '';
    this.out += `<pre><code${cls}>${code}</code></pre>\n`;
    return i;
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
   * A run of quoted lines: each line is a paragraph at its depth, opening nested `<blockquote>`s down to
   * it and closing back up from a deeper one. Iterative, so any depth of `>` renders without recursion.
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
      if (this.isListLine(i, d)) {
        i = this.listLevel(i, this.listIndent(i), 0, d);
        continue;
      }
      this.out += '<p>';
      this.inline(this.model.lineTokens(i));
      this.out += '</p>\n';
      i++;
    }
    for (; open > 0; open--) this.out += '</blockquote>\n';
    return i;
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

      // A deeper item, right below or past blank lines, nests inside this one.
      for (;;) {
        let j = i;
        while (j < n && this.isBlankIn(j, quote)) j++;
        if (!(j < n && this.isListLine(j, quote) && this.listIndent(j) > levelIndent)) break;
        this.out += '\n';
        i = this.listLevel(j, this.listIndent(j), depth + 1, quote);
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

  private table(line: number): number {
    const n = this.doc.lineCount;
    this.out += '<table>\n<thead>\n';
    this.tableRow(this.model.lineTokens(line), 'th');
    this.out += '</thead>\n';
    let i = line + 2; // past the header and delimiter rows
    if (i < n && this.isTableRow(i)) {
      this.out += '<tbody>\n';
      while (i < n && this.isTableRow(i)) {
        this.tableRow(this.model.lineTokens(i), 'td');
        i++;
      }
      this.out += '</tbody>\n';
    }
    this.out += '</table>\n';
    return i;
  }

  /** Split a row's tokens into cells at the `|` markers, then render each cell's inline run. */
  private tableRow(tokens: readonly Token[], tag: string): void {
    this.out += '<tr>';
    let cellStart = 0;
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
            this.out += `<${tag}>${content}</${tag}>`;
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
    const safe = safeHref(src, 'image');
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
      case 'embed':
        this.out += `<span class="embed">${text}</span>`;
        break;
      default:
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
      default:
        return !this.isTableStart(line);
    }
  }

  private isTableStart(line: number): boolean {
    return (
      line + 1 < this.doc.lineCount &&
      this.isTableRow(line) &&
      this.first(line + 1)?.type === 'tableDelimiter'
    );
  }

  /**
   * A table row: the model splits a row's cells at its unescaped pipes and marks each one, so a line with a
   * `|` marker is a row. Other inline markers (`**`, `[`) don't count, so the line after a table ends it.
   */
  private isTableRow(line: number): boolean {
    return this.model.lineTokens(line).some((t) => this.isPipe(t));
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
mark { background: #fdf6b2; }
table { border-collapse: collapse; }
th, td { border: 1px solid rgba(127,127,127,0.4); padding: 0.4em 0.7em; }
.properties { color: #888; font-size: 0.85em; }
img { max-width: 100%; }

/* Task states (PRD §11.4). One accent, single-hue: the dash and the check share it, so "under way" and
   "done" read as two points on one path. A resting box is a bare outline in the quiet border colour. */
:root { --t-border:#B1B8C2; --t-accent:#216BE0; --t-check:#fff; --t-off:#6E7781; }
@media (prefers-color-scheme: dark) {
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
li.t-done > .ttext { color: var(--t-off); text-decoration: line-through; }`;
