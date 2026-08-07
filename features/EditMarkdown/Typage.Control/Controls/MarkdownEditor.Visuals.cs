using Avalonia.Media;
using Puffinote.Text.Editing;
using Typage.Engine.Markdown;

namespace Typage.Control.Controls;

public partial class MarkdownEditor
{
    /// <summary>The displayed number text (<c>"N. "</c>) for an ordered-list marker at the line-relative span
    /// [<paramref name="sLine"/>, <paramref name="enLine"/>) on <paramref name="line"/>, or <c>null</c> when
    /// that span is not an ordered marker. The number is computed from the list's document structure, so it
    /// stays on the control rather than in the layout builder.</summary>
    private string? OrderedMarkerText(int line, string lineText, int sLine, int enLine)
        => IsOrderedMarker(lineText, sLine, enLine)
            ? OrderedOrdinal(line) + OrderedDelimiter(lineText, sLine, enLine) + " "
            : null;

    /// <summary>
    /// The nesting level of the list item on <paramref name="line"/> — 0 for a top-level item, one more for each
    /// ancestor item it sits under. Read from the list's <b>structure</b>, the way the ordinal is: an item nested
    /// with two spaces, with three, or with a tab is one level in either way, so every list type indents by the
    /// same step per level rather than by whatever its source spelling measures. A blank line starts a fresh list
    /// (back to level 0), matching <see cref="OrderedOrdinal"/>'s scan; a lazy continuation line belongs to the
    /// item above it and is stepped over, and anything that opens a block of its own ends the list.
    /// </summary>
    private int ListLevel(int line)
    {
        var doc = Buffer.Document;
        var indent = LineIndent(line);
        var level = 0;
        for (var k = line - 1; k >= 0 && indent > 0; k--)
        {
            var text = doc.GetLineText(k);
            if (text.Trim().Length == 0)
            {
                break; // a blank line starts a fresh list
            }

            if (LineIndent(k) >= indent)
            {
                continue; // a sibling or something nested deeper — not an ancestor of this item
            }

            if (ListItemKindOf(k) != ItemKind.None)
            {
                level++;               // a shallower item: this item's parent...
                indent = LineIndent(k); // ...and now look for *its* parent
            }
            else if (!LinePrefix.IsLazyContinuation(text))
            {
                break; // a heading, quote, rule, fence… — the list doesn't reach above this
            }
        }

        return level;
    }

    /// <summary>Whether <paramref name="line"/> is part of a code block — fenced or indented (for the background
    /// panel). The layout owns the answer, so the panel, the block kind, and the paragraph grouping agree.</summary>
    private bool IsCodeLine(int line) => _layout.IsCodeLine(line);

    /// <summary>The right edge of the text column in content space (for full-width adornments) — i.e. the width
    /// of the capped, centred reading column, since content space starts at its left edge.</summary>
    private double ContentRight() => ContentWidth();

    /// <summary>Whether <paramref name="line"/> is a horizontal rule (its sole token is the rule marker).</summary>
    private bool IsHorizontalRuleLine(int line)
    {
        var toks = _model.LineTokens(line);
        return toks.Count > 0 && toks[0].Type == TokenType.HorizontalRule;
    }

    /// <summary>
    /// Whether the rule line at <paramref name="line"/> should be drawn with a full-row highlight: the caret
    /// is on it, there is no range selection, and we're in WYSIWYG (non-source) view. (In raw-source view the
    /// rule is plain <c>---</c> text and the usual caret/selection apply.)
    /// </summary>
    internal bool ShouldHighlightRuleLine(int line)
        => !MarkersRevealed
           && Buffer.Selection.Length == 0
           && IsHorizontalRuleLine(line)
           && Buffer.Document.GetPosition(Buffer.Selection.Caret).Line == line;

    /// <summary>Fetch a run's <see cref="FormattedText"/> through the cache, exercising it; for perf tests.</summary>
    internal void GetFormattedTextForTest(string text)
    {
        var style = EditorTheme.Resolve(TokenType.Text);
        _measurer.GetFormattedText(text, style, style.Foreground);
    }
}
