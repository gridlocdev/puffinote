namespace Typage.Engine.Markdown;

/// <summary>
/// The semantic kinds the tokenizer emits (PRD §8.3, §10.1). The engine emits semantic
/// tokens only — types, source ranges, and roles — and knows nothing about styling or
/// concealment; those are the control's job (MarkdownTheme / live-preview behavior).
/// </summary>
public enum TokenType
{
    /// <summary>Ordinary text content with no inline construct.</summary>
    Text,

    // ----- Line-prefix / block markers -----
    HeadingMarker,
    BlockquoteMarker,
    ListMarker,
    TaskMarker,
    HorizontalRule,
    TableDelimiter,
    FrontMatterFence,
    CodeFence,

    // ----- Block content -----
    HeadingText,
    CodeText,
    FrontMatterText,
    TableCellText,

    // ----- Inline constructs -----
    Emphasis,
    Strong,
    StrongEmphasis,
    Strikethrough,
    Highlight,
    InlineCode,
    LinkText,
    LinkUrl,
    Autolink,
    ImageAlt,
    ImageUrl,
    WikiLink,
    Embed,
    Escape,
    HardBreak,

    /// <summary>A run of inline marker punctuation (e.g. <c>**</c>, <c>[</c>, <c>](</c>, <c>)</c>).</summary>
    Marker,
}
