using Typage.Engine.Editing;
using Typage.Engine.Markdown;

namespace Typage.Control.Controls;

public partial class MarkdownEditor
{
    // The caret's current formatting, surfaced so the toolbar can light up the matching tools (like the
    // spreadsheet's bar). Each is read cheaply off the already-built line tokens / line prefix at the caret.

    /// <summary>Whether the target (selection, or the word under the caret) is bold (<c>**…**</c>).</summary>
    public bool IsBoldActive => StarEmphasisLevel() >= 2;

    /// <summary>Whether the target (selection, or the word under the caret) is italic (<c>*…*</c>).</summary>
    public bool IsItalicActive => StarEmphasisLevel() is 1 or 3;

    /// <summary>Whether the target is inline code (<c>`…`</c>).</summary>
    public bool IsInlineCodeActive => IsFormatActive(TokenType.InlineCode, '`');

    /// <summary>Whether the target is struck through (<c>~~…~~</c>).</summary>
    public bool IsStrikethroughActive => IsFormatActive(TokenType.Strikethrough, '~');

    /// <summary>Whether the target is highlighted (<c>==…==</c>).</summary>
    public bool IsHighlightActive => IsFormatActive(TokenType.Highlight, '=');

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

    /// <summary>
    /// Whether the text the toggle would act on already carries <paramref name="contentType"/> — asked of the
    /// <em>same</em> run-finder the toggle itself uses, so the lit tool and the tool that would turn it off are
    /// never two different answers (a caret mid-way through a struck sentence lights the strike tool, and
    /// pressing it removes that strike).
    /// </summary>
    private bool IsFormatActive(TokenType contentType, char delim)
    {
        var (start, end) = FormatTarget();
        return FindFormatRun(start, end, contentType, delim, star: false) is not null;
    }

    /// <summary>The delimiter-run length of the <c>*</c>/<c>_</c> emphasis over the target — 1 italic, 2 bold,
    /// 3 both — or 0 when it carries none. Bold and italic are layers of one run, so both read it.</summary>
    private int StarEmphasisLevel()
    {
        var (start, end) = FormatTarget();
        return FindFormatRun(start, end, TokenType.Strong, '*', star: true)?.MarkerLen ?? 0;
    }
}
