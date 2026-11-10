import type { MarkdownModel } from './markdownModel.ts';
import type { TextDocument } from '@puffinote/text';

/** A heading in the active note (PRD §7.2.3, the Outline panel). */
export interface OutlineItem {
  readonly level: number;
  readonly text: string;
  readonly line: number;
  /** Offset of the heading's text, where selecting the entry puts the caret. */
  readonly offset: number;
}

/** The note's ATX and setext headings, in order (the engine marks a setext text line with a zero-length marker). Headings inside code or front matter don't count. */
export function outlineOf(doc: TextDocument, model: MarkdownModel): OutlineItem[] {
  const out: OutlineItem[] = [];
  for (let line = 0; line < doc.lineCount; line++) {
    const tokens = model.lineTokens(line);
    const marker = tokens.find((t) => t.type === 'headingMarker');
    if (marker) {
      const text = doc.getLineText(line).replace(/\r$/, '');
      const content = text
        .replace(/^\s*#{1,6}[ \t]*/, '')
        .replace(/[ \t]+#+[ \t]*$/, '')
        .trim();
      const start = doc.getLineStart(line);
      out.push({
        level: Number(marker.meta),
        text: plain(content),
        line,
        offset: start + text.indexOf(content),
      });
    }
  }
  return out;
}

/** Heading text without inline markup, for the panel. */
function plain(text: string): string {
  return text
    .replace(/!?\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/(\*\*|__|~~|==|\*|_|`)/g, '')
    .trim();
}
