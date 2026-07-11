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

    /// <summary>
    /// The <c>#</c> run of an ATX heading, carrying the level (1–6) in <see cref="Token.Meta"/>.
    ///
    /// <para>A <b>setext</b> heading has no marker punctuation on its text line, so it emits this token
    /// <b>zero-length</b> at the line's start — same type, same <c>Meta</c>, no glyphs. That keeps one
    /// representation of "this line is a heading of level N" for every consumer (the layout, the HTML
    /// renderer, the CLI outline, the toolbar's format state), none of which has to know which spelling
    /// produced it. Its underline is a separate <see cref="SetextUnderline"/> on the next line.</para>
    /// </summary>
    HeadingMarker,
    BlockquoteMarker,
    ListMarker,
    TaskMarker,
    HorizontalRule,

    /// <summary>
    /// The <c>===</c> / <c>---</c> underline of a setext heading. Distinct from <see cref="HorizontalRule"/>
    /// (which a bare <c>---</c> would otherwise be) and from <see cref="HeadingMarker"/> (which carries the
    /// level, and sits on the line above) so that the surface can conceal the underline <i>and</i> collapse
    /// its row away — the underline is pure markup, and in a WYSIWYG view it should occupy no space at all.
    /// </summary>
    SetextUnderline,
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
