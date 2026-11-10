/**
 * The semantic kinds the tokenizer emits (PRD §11.3; layering in ARCHITECTURE.md). The engine emits semantic tokens only — types,
 * source ranges and roles — and knows nothing about styling or concealment; those are the editor's job.
 *
 * - `headingMarker` is the `#` run of an ATX heading, with the level (1–6) in `meta`. A setext heading has no
 *   marker punctuation on its text line, so it emits this token zero-length at the line's start: one
 *   representation of "this line is a heading of level N" for every consumer. Its underline is a separate
 *   `setextUnderline` on the next line.
 * - `setextUnderline` is the `===` / `---` under a setext heading. It is distinct from `horizontalRule` so the
 *   surface can conceal it and collapse its row away.
 * - `marker` is a run of inline marker punctuation (`**`, `[`, `](`, `)`).
 */
export type TokenType =
  | 'text'
  // Line-prefix / block markers
  | 'headingMarker'
  | 'blockquoteMarker'
  | 'listMarker'
  | 'taskMarker'
  | 'horizontalRule'
  | 'setextUnderline'
  | 'tableDelimiter'
  | 'frontMatterFence'
  | 'codeFence'
  // Block content
  | 'headingText'
  | 'codeText'
  | 'frontMatterText'
  | 'tableCellText'
  // Inline constructs
  | 'emphasis'
  | 'strong'
  | 'strongEmphasis'
  | 'strikethrough'
  | 'highlight'
  | 'inlineCode'
  | 'linkText'
  | 'linkUrl'
  | 'autolink'
  | 'imageAlt'
  | 'imageUrl'
  | 'wikiLink'
  | 'embed'
  | 'escape'
  | 'hardBreak'
  | 'marker';

/**
 * Whether a token is visible rendered content or marker punctuation. The live preview keys concealment off
 * this: markers hide when the caret is away, content always shows (PRD §10.2, §11.3).
 */
export type TokenRole = 'content' | 'marker';

/**
 * One semantic token: a typed, role-tagged span of the document by absolute offset. Spans never cross a line
 * boundary. `meta` carries construct-specific data (heading level, quote depth, task state, code-fence
 * language, a link or embed target), or null.
 *
 * `node` groups the tokens of one inline construct (the `**`, the bold content and the closing `**` share an
 * id) so the live preview can reveal or skip a construct as a unit. It is line-local, starts at 1 within a
 * line, and is 0 for tokens outside a groupable inline node (plain text, block markers, bare URLs).
 *
 * Tokens are plain objects with every field present, so two token streams compare with structural equality.
 */
export interface Token {
  readonly start: number;
  readonly length: number;
  readonly type: TokenType;
  readonly role: TokenRole;
  readonly meta: string | null;
  readonly node: number;
}

export function token(
  start: number,
  length: number,
  type: TokenType,
  role: TokenRole,
  meta: string | null = null,
  node = 0,
): Token {
  return { start, length, type, role, meta, node };
}

/** Offset just past the token. */
export function tokenEnd(t: Token): number {
  return t.start + t.length;
}

/** True when the token's source range intersects `[start, start + length)`; an empty range touches its offset. */
export function tokenIntersects(t: Token, start: number, length: number): boolean {
  return t.start < start + length + (length === 0 ? 1 : 0) && tokenEnd(t) > start;
}
