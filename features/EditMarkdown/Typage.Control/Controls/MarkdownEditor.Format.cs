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

    /// <summary>
    /// Whether the caret's line is a bulleted-list item. A task item is its own kind, not a bullet — even
    /// though it is spelled with one (<c>- [ ] </c>) — so a checkbox lights only the checkbox tool, not the
    /// bullet tool as well. This mirrors <see cref="ToggleBulletList"/>'s own test for what it toggles off,
    /// so the lit tool and the tool that would undo it stay the same tool.
    /// </summary>
    public bool IsBulletListActive => CaretPrefix() is { List: ListKind.Unordered, HasTask: false };

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

    /// <summary>
    /// The block-level prefix in force at the caret: the marker of the block the caret is drawn inside, read
    /// from that block's first line rather than the caret's own source line. A hard-wrapped item's continuation
    /// lines carry no marker of their own, but the reader sees the caret inside the item — so the item's tool
    /// stays lit as the caret moves through it, instead of the toolbar going dark past the first wrapped line.
    /// Blocks the caret starts (and every line in raw-text mode) resolve to themselves, so this is a no-op there.
    /// </summary>
    private LinePrefix CaretPrefix() =>
        LinePrefix.Parse(Buffer.Document.GetLineText(BlockFirstLine(CaretLine)));

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
