using Typage.Engine.Editing;
using Typage.Engine.Markdown;

namespace Typage.Control.Controls;

public partial class MarkdownEditor
{
    // The caret's current formatting, surfaced so the toolbar can light up the matching tools (like the
    // spreadsheet's bar). Each is read cheaply off the already-built line tokens / line prefix at the caret.

    /// <summary>Whether the caret sits within bold (<c>**…**</c>) text.</summary>
    public bool IsBoldActive => CaretInInline(TokenType.Strong, TokenType.StrongEmphasis);

    /// <summary>Whether the caret sits within italic (<c>*…*</c>) text.</summary>
    public bool IsItalicActive => CaretInInline(TokenType.Emphasis, TokenType.StrongEmphasis);

    /// <summary>Whether the caret sits within inline code (<c>`…`</c>).</summary>
    public bool IsInlineCodeActive => CaretInInline(TokenType.InlineCode);

    /// <summary>Whether the caret sits within strikethrough (<c>~~…~~</c>) text.</summary>
    public bool IsStrikethroughActive => CaretInInline(TokenType.Strikethrough);

    /// <summary>Whether the caret sits within highlighted (<c>==…==</c>) text.</summary>
    public bool IsHighlightActive => CaretInInline(TokenType.Highlight);

    /// <summary>Whether the caret's line carries a task checkbox.</summary>
    public bool IsTaskActive => CaretPrefix().HasTask;

    /// <summary>Whether the caret's line is a bulleted-list item.</summary>
    public bool IsBulletListActive => CaretPrefix().List == ListKind.Unordered;

    /// <summary>Whether the caret's line is a numbered-list item.</summary>
    public bool IsNumberedListActive => CaretPrefix().List == ListKind.Ordered;

    /// <summary>Whether the caret's line is a blockquote.</summary>
    public bool IsQuoteActive => CaretPrefix().HasQuote;

    /// <summary>The heading level of the caret's line (1–6), or 0 when the line isn't a heading.</summary>
    public int CaretHeadingLevel
    {
        get
        {
            var tokens = _model.LineTokens(CaretLine);
            return tokens.Count > 0 && tokens[0].Type == TokenType.HeadingMarker && int.TryParse(tokens[0].Meta, out int level)
                ? level
                : 0;
        }
    }

    private int CaretLine => Buffer.Document.GetPosition(Buffer.Selection.Caret).Line;

    private LinePrefix CaretPrefix() => LinePrefix.Parse(Buffer.Document.GetLineText(CaretLine));

    /// <summary>True when the caret lies within a content token on its line whose type is one of <paramref name="types"/>.</summary>
    private bool CaretInInline(params TokenType[] types)
    {
        int caret = Buffer.Selection.Caret;
        foreach (var token in _model.LineTokens(CaretLine))
        {
            if (token.Role == TokenRole.Content && caret >= token.Start && caret <= token.End && Array.IndexOf(types, token.Type) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
