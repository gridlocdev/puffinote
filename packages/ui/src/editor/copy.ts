import { MarkdownModel, renderHtmlBody } from '@puffinote/markdown';
import { TextDocument } from '@puffinote/text';

/*
 * What Copy and Cut put on the clipboard besides the Markdown source (PRD §10.1): the selection as formatted
 * text, rendered as Export to HTML renders it.
 */

/** The selection's formatted copy, its pictures still links: the shell swaps in the pictures themselves. */
export function selectionHtml(markdown: string): string {
  return renderHtmlBody(TextDocument.fromString(markdown));
}

/** Whether `markdown` shows a picture: an image link or a wiki embed, outside code. */
export function showsPictures(markdown: string): boolean {
  if (!markdown.includes('![')) return false;
  const model = MarkdownModel.build(TextDocument.fromString(markdown));
  return model.allTokens().some((t) => t.type === 'imageUrl' || t.type === 'embed');
}
