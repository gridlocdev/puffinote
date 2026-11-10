import { TextDocument } from '@puffinote/text';
import { MarkdownModel, type TokenType } from '../src/index.ts';

export function tokenize(markdown: string): { doc: TextDocument; model: MarkdownModel } {
  const doc = TextDocument.fromString(markdown);
  return { doc, model: MarkdownModel.build(doc) };
}

/** Every token of `type`, as its source text. */
export function textsOfType(markdown: string, type: TokenType): string[] {
  const { doc, model } = tokenize(markdown);
  return model
    .allTokens()
    .filter((t) => t.type === type)
    .map((t) => doc.getText(t.start, t.length));
}

/** The distinct token types in the document, in first-seen order. */
export function typesIn(markdown: string): TokenType[] {
  return [
    ...new Set(
      tokenize(markdown)
        .model.allTokens()
        .map((t) => t.type),
    ),
  ];
}

export function hasType(markdown: string, type: TokenType): boolean {
  return typesIn(markdown).includes(type);
}
