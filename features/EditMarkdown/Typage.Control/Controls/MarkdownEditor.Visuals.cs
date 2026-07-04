using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Typage.Control.Rendering;
using Typage.Engine.Document;
using Typage.Engine.Editing;
using Typage.Engine.Markdown;

namespace Typage.Control.Controls;

public partial class MarkdownEditor
{
    /// <summary>
    /// The laid-out <see cref="VisualLine"/> for <paramref name="block"/>, cached while the layout is stable
    /// (the cache is cleared by EnsureLayout on any structural or width change). Callers always run
    /// EnsureLayout first, so the block boundaries behind a cached entry are current. On a miss the
    /// <see cref="BlockLayoutBuilder"/> builds it from a snapshot of the current document/model/theme/view state.
    /// </summary>
    private VisualLine BlockVisual(int block)
    {
        if (_blockVisualCache.TryGetValue(block, out var cached))
        {
            return cached;
        }

        var visual = _layout.Build(block, new BlockLayoutContext(
            _buffer.Document, _model, _theme, _blockStartLine,
            RawTextMode, MarkersRevealed, WrapWidth(), CheckboxGutter,
            OrderedMarkerText));
        _blockVisualCache[block] = visual;
        return visual;
    }

    /// <summary>The displayed number text (<c>"N. "</c>) for an ordered-list marker at the line-relative span
    /// [<paramref name="sLine"/>, <paramref name="enLine"/>) on <paramref name="line"/>, or <c>null</c> when
    /// that span is not an ordered marker. The number is computed from the list's document structure, so it
    /// stays on the control rather than in the layout builder.</summary>
    private string? OrderedMarkerText(int line, string lineText, int sLine, int enLine)
        => IsOrderedMarker(lineText, sLine, enLine)
            ? OrderedOrdinal(line) + OrderedDelimiter(lineText, sLine, enLine) + " "
            : null;

    /// <summary>Whether <paramref name="line"/> is part of a code block — fenced or indented (for the background panel).</summary>
    private bool IsCodeLine(int line)
    {
        var tokens = _model.LineTokens(line);
        if (tokens.Count > 0)
        {
            // A code row begins with the fence marker or carries code text; the dedent line that ends an
            // indented block tokenizes normally and is correctly excluded here.
            return tokens[0].Type is TokenType.CodeFence or TokenType.CodeText;
        }

        // A blank row inside a fenced block is code until the fence closes. Inside an *indented* block a
        // blank stays code only when it's interior — a later line resumes the indented code. A blank that
        // merely trails the block (nothing more indented follows) is not part of it, so it isn't panelled
        // — this is the empty line your caret sits on while typing below an indented block.
        var entering = _model.EnteringState(line).Kind;
        if (entering == BlockKind.FencedCode)
        {
            return true;
        }

        if (entering == BlockKind.IndentedCode)
        {
            for (var l = line + 1; l < _buffer.Document.LineCount; l++)
            {
                var toks = _model.LineTokens(l);
                if (toks.Count == 0)
                {
                    continue; // another interior blank — keep scanning forward
                }

                return toks[0].Type == TokenType.CodeText; // indented code resumes → this blank is interior
            }
        }

        return false;
    }

    /// <summary>The right edge of the text column in content space (for full-width adornments).</summary>
    private double ContentRight()
    {
        var w = _viewport.Width > 0 ? _viewport.Width : Bounds.Width;
        return Math.Max(120, w - PaddingLeft * 2);
    }

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
           && _buffer.Selection.Length == 0
           && IsHorizontalRuleLine(line)
           && _buffer.Document.GetPosition(_buffer.Selection.Caret).Line == line;

    /// <summary>Fetch a run's <see cref="FormattedText"/> through the cache, exercising it; for perf tests.</summary>
    internal void GetFormattedTextForTest(string text)
    {
        var style = _theme.Resolve(TokenType.Text);
        _measurer.GetFormattedText(text, style, style.Foreground);
    }
}
