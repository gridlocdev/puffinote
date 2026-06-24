namespace Typage.Engine.Markdown;

/// <summary>
/// Whether a token is the visible rendered <see cref="Content"/> of a construct or its
/// <see cref="Marker"/> punctuation. This is what the live-preview layer keys concealment
/// off: markers hide when the caret is away, content is always shown (PRD §7.4, §10.1).
/// </summary>
public enum TokenRole
{
    /// <summary>Rendered content (the text a reader sees in the formatted view).</summary>
    Content,

    /// <summary>Syntax punctuation that conceals when the caret is outside the node.</summary>
    Marker,
}

/// <summary>
/// One semantic token: a typed, role-tagged span of the document by absolute offset. Spans
/// never cross a line boundary (the tokenizer is line-based). <see cref="Meta"/> carries
/// construct-specific data (heading level as a string, code-fence language, a link/embed
/// target) when relevant; it is null otherwise.
/// </summary>
/// <remarks>
/// <see cref="Node"/> groups the tokens of one inline construct (e.g. the <c>**</c>, the bold
/// content, and the closing <c>**</c> all share an id) so the live-preview layer can reveal or
/// atomically skip a construct as a unit (PRD §7.4). It is a per-line-local id (constructs never
/// cross a line), starts at 1 within a line, and is <c>0</c> for tokens that aren't part of a
/// groupable inline node (plain text, line-prefix/block markers, bare URLs). Grouping is identity
/// only; the engine assigns no concealment policy — that stays in the control.
/// </remarks>
public readonly record struct Token(int Start, int Length, TokenType Type, TokenRole Role, string? Meta = null, int Node = 0)
{
    /// <summary>Offset just past the token.</summary>
    public int End => Start + Length;

    /// <summary>True when this token's source range intersects <c>[start, start+length)</c>.</summary>
    public bool Intersects(int start, int length) => Start < start + length + (length == 0 ? 1 : 0) && End > start;
}
