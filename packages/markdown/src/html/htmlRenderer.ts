import type { TextDocument } from '@puffinote/text';
import { isAsciiDigit } from '../markdown/chars.ts';
import { MarkdownModel } from '../markdown/markdownModel.ts';
import { taskLabel, taskStateFromMark, type TaskState } from '../markdown/taskState.ts';
import type { Token } from '../markdown/token.ts';

/**
 * Renders a document to self-contained HTML (PRD §7.8). Pure and dependency-free: it walks the same token
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
  while (line < document.lineCount) line = r.block(line);
  return r.out;
}

/** Escape `&`, `<` and `>`. */
export function escapeHtml(s: string): string {
  return s.replace(/[&<>]/g, (c) => (c === '&' ? '&amp;' : c === '<' ? '&lt;' : '&gt;'));
}

function escapeAttr(s: string): string {
  return escapeHtml(s).replaceAll('"', '&quot;');
}

function stripCr(s: string): string {
  return s.replace(/\r+$/, '');
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
        return this.quoteLevel(line, 1);
      case 'listMarker':
        return this.listLevel(line, this.listIndent(line));
    }

    if (this.isTableStart(line)) return this.table(line);
    return this.paragraph(line);
  }

  /** Front matter renders as a collapsed "Properties" block (PRD §7.4). */
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
    while (i < n && this.model.enteringState(i).kind === 'fencedCode') {
      code += escapeHtml(stripCr(this.doc.getLineText(i))) + '\n';
      i++;
    }
    if (i < n && this.first(i)?.type === 'codeFence') i++;
    const cls = lang ? ` class="language-${escapeHtml(lang)}"` : '';
    this.out += `<pre><code${cls}>${code}</code></pre>\n`;
    return i;
  }

  /**
   * One blockquote level: a line at this depth is a paragraph of this quote, a deeper run opens a nested
   * `<blockquote>`, and a shallower line returns to the caller.
   */
  private quoteLevel(line: number, depth: number): number {
    const n = this.doc.lineCount;
    let i = line;
    this.out += '<blockquote>\n';
    while (i < n) {
      const d = this.quoteDepth(i);
      if (d < depth) break;
      if (d > depth) {
        i = this.quoteLevel(i, depth + 1);
        continue;
      }
      this.out += '<p>';
      this.inline(this.model.lineTokens(i));
      this.out += '</p>\n';
      i++;
    }
    this.out += '</blockquote>\n';
    return i;
  }

  private quoteDepth(line: number): number {
    const t = this.first(line);
    return t?.type === 'blockquoteMarker' ? Number.parseInt(t.meta ?? '', 10) || 0 : 0;
  }

  /**
   * One list level: items at `levelIndent` are siblings, a deeper run nests inside the preceding item, and a
   * shallower item returns to the caller. Blank lines between items keep them in one list.
   */
  private listLevel(line: number, levelIndent: number): number {
    const n = this.doc.lineCount;
    const ordered = this.isOrdered(line);
    const start = ordered ? this.startNumber(line) : 1;
    this.out += ordered ? (start !== 1 ? `<ol start="${start}">\n` : '<ol>\n') : '<ul>\n';

    let i = line;
    while (i < n) {
      if (this.isBlank(i)) {
        let j = i;
        while (j < n && this.isBlank(j)) j++;
        if (j < n && this.isListLine(j) && this.listIndent(j) >= levelIndent) {
          i = j;
          continue;
        }
        break;
      }
      if (!this.isListLine(i)) break;
      const indent = this.listIndent(i);
      if (indent !== levelIndent) break; // shallower: an outer list's; deeper with no sibling: the caller's

      const tokens = this.model.lineTokens(i);
      const box = tokens[1]?.type === 'taskMarker' ? tokens[1] : null;
      if (box) {
        // Three states, not a bit: the export draws the same box the editor does (PRD §8.3.2). The text is
        // wrapped so a done item can be struck through without the rule crossing its box.
        const state = taskStateFromMark(box.meta?.charAt(0) || ' ');
        this.out +=
          `<li class="task ${taskClass(state)}"><span class="tbox" role="img" aria-label="${taskLabel(state)}">` +
          `${taskGlyph(state)}</span><span class="ttext">`;
      } else {
        this.out += '<li>';
      }
      this.inline(tokens);
      if (box) this.out += '</span>';
      i++;

      if (i < n && this.isListLine(i) && this.listIndent(i) > levelIndent) {
        this.out += '\n';
        i = this.listLevel(i, this.listIndent(i));
      }
      this.out += '</li>\n';
    }

    this.out += ordered ? '</ol>\n' : '</ul>\n';
    return i;
  }

  private isListLine(line: number): boolean {
    return this.first(line)?.type === 'listMarker';
  }

  private listIndent(line: number): number {
    const first = this.first(line);
    return first ? first.start - this.doc.getLineStart(line) : 0;
  }

  private markerText(line: number): string {
    const first = this.first(line);
    return first ? this.doc.getText(first.start, first.length) : '';
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
    if (i < n && this.hasTableTokens(i)) {
      this.out += '<tbody>\n';
      while (i < n && this.hasTableTokens(i)) {
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
      const isPipe =
        t !== undefined && t.type === 'marker' && t.length === 1 && this.doc.getText(t.start, 1) === '|';
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

  private paragraph(line: number): number {
    const n = this.doc.lineCount;
    this.out += '<p>';
    let i = line;
    let firstLine = true;
    let prevHardBreak = false;
    while (i < n && !this.isBlank(i) && this.isParagraphLine(i)) {
      // A hard break at the end of the previous line becomes a <br>; otherwise a soft join.
      if (!firstLine) this.out += prevHardBreak ? '<br>\n' : '\n';
      const tokens = this.model.lineTokens(i);
      this.inline(tokens);
      prevHardBreak = tokens.at(-1)?.type === 'hardBreak';
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

  /** Tokens: `[` inner… `](` linkUrl `)`. */
  private link(tokens: readonly Token[], i: number, to: number): number {
    let depth = 1;
    let j = i + 1;
    for (; j < to; j++) {
      const t = tokens[j]!;
      if (t.type !== 'marker') continue;
      const m = this.doc.getText(t.start, t.length);
      if (m === '[' || m === '![') {
        depth++;
      } else if (m === '](') {
        depth--;
        if (depth === 0) break;
      }
    }
    if (j + 1 >= to) return to;
    const href = tokens[j + 1]!.meta ?? '';
    this.out += `<a href="${escapeAttr(href)}">`;
    this.inline(tokens, i + 1, j);
    this.out += '</a>';
    return j + 3;
  }

  /** Tokens: `![` imageAlt `](` imageUrl `)`. */
  private image(tokens: readonly Token[], i: number, to: number): number {
    if (i + 4 >= to) return to;
    const alt = tokens[i + 1]!.meta ?? '';
    const src = tokens[i + 3]!.meta ?? '';
    this.out += `<img src="${escapeAttr(src)}" alt="${escapeAttr(alt)}">`;
    return i + 5;
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
      case 'autolink':
        this.out += `<a href="${escapeAttr(t.meta ?? this.doc.getText(t.start, t.length))}">${text}</a>`;
        break;
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

  private isBlank(line: number): boolean {
    return stripCr(this.doc.getLineText(line)).length === 0;
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
        return false;
      default:
        return !this.isTableStart(line);
    }
  }

  private isTableStart(line: number): boolean {
    return (
      line + 1 < this.doc.lineCount &&
      this.hasTableTokens(line) &&
      this.first(line + 1)?.type === 'tableDelimiter'
    );
  }

  private hasTableTokens(line: number): boolean {
    return this.model.lineTokens(line).some((t) => t.type === 'marker' || t.type === 'tableDelimiter');
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

/* Task states (PRD §8.3.2). One accent, single-hue: the dash and the check share it, so "under way" and
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
