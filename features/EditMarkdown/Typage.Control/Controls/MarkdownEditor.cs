using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Typage.Control.Rendering;
using Typage.Engine.Document;
using Typage.Engine.Editing;
using Typage.Engine.Markdown;

namespace Typage.Control.Controls;

/// <summary>
/// The embeddable live-preview editing surface. A custom Avalonia control (not a
/// <c>TextBox</c>) that draws the document from the engine's token stream with the
/// <see cref="MarkdownTheme"/>, owns caret/selection/input, and conceals (dims) syntax markers
/// when the caret is away from their line — the "live preview" feel (PRD §7.3, §7.4).
/// </summary>
/// <remarks>
/// This is an early Phase 3/4 surface: rendering uses <see cref="FormattedText"/> per line and
/// offsets stay 1:1 with the visual model (markers dim rather than collapse), so caret mapping is
/// exact and simple. Full glyph-hiding with an offset↔visual map, line virtualization, IME, and an
/// automation peer are the dedicated later phases (PRD §11) and are documented as not-yet-built.
/// The public API and the engine seam below are stable so that work slots in without a rewrite.
/// </remarks>
public class MarkdownEditor : Avalonia.Controls.Control
{
    private readonly DocumentBuffer _buffer = new();
    private MarkdownModel _model;
    private MarkdownTheme _theme = MarkdownTheme.Default;
    private IBrush _dimMarker = new SolidColorBrush(Color.FromArgb(0x55, 0xA0, 0xA6, 0xAE));

    public MarkdownEditor()
    {
        Focusable = true;
        _model = MarkdownModel.Build(_buffer.Document);
        _buffer.TextChanged += (_, _) =>
        {
            _model = MarkdownModel.Build(_buffer.Document);
            InvalidateMeasure();
            InvalidateVisual();
            TextChanged?.Invoke(this, EventArgs.Empty);
        };
        _buffer.Changed += (_, _) =>
        {
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
    }

    /// <summary>Raised after the document text changes.</summary>
    public event EventHandler? TextChanged;

    /// <summary>Raised after the caret or selection changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The full document text. Setting it replaces the document and clears undo history.</summary>
    public string Text
    {
        get => _buffer.Document.GetText();
        set => _buffer.Load(value);
    }

    /// <summary>True when the buffer differs from the last-saved snapshot.</summary>
    public bool IsDirty => _buffer.IsDirty;

    /// <summary>Live document statistics for the host's status bar.</summary>
    public DocumentStats Stats => DocumentStats.Compute(_buffer.Document);

    /// <summary>The caret position as a 1-based line:column for the status bar.</summary>
    public LinePosition CaretPosition => _buffer.Document.GetPosition(_buffer.Selection.Caret);

    /// <summary>The editable model, exposed for the host (save/load/dirty) and tests.</summary>
    internal DocumentBuffer Buffer => _buffer;

    public bool CanUndo => _buffer.CanUndo;

    public bool CanRedo => _buffer.CanRedo;

    /// <summary>The active theme (presentation seam — swap to re-style without touching the engine).</summary>
    public MarkdownTheme EditorTheme
    {
        get => _theme;
        set
        {
            _theme = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>Resolves embed targets to controls; defaults to <see cref="NullEmbedResolver"/> (embeds show as source).</summary>
    public IEmbedResolver EmbedResolver { get; set; } = NullEmbedResolver.Instance;

    public void MarkSaved() => _buffer.MarkSaved();

    public void Undo() => _buffer.Undo();

    public void Redo() => _buffer.Redo();

    // ----- formatting commands (toolbar / Format menu — PRD §7.5) -----

    public void ToggleBold() => Wrap("**", "**");

    public void ToggleItalic() => Wrap("*", "*");

    public void ToggleInlineCode() => Wrap("`", "`");

    public void ToggleStrikethrough() => Wrap("~~", "~~");

    /// <summary>Set the caret's line to an ATX heading of <paramref name="level"/> (replacing any existing one).</summary>
    public void ApplyHeading(int level)
    {
        level = Math.Clamp(level, 1, 6);
        var doc = _buffer.Document;
        var line = doc.GetPosition(_buffer.Selection.Caret).Line;
        var lineStart = doc.GetLineStart(line);
        var text = doc.GetLineText(line).TrimEnd('\r');
        var trimmed = text.TrimStart('#', ' ');
        var replacement = new string('#', level) + " " + trimmed;
        _buffer.ReplaceRange(lineStart, text.Length, replacement);
    }

    /// <summary>Wrap the selection in <paramref name="left"/>/<paramref name="right"/>, or insert the pair at the caret.</summary>
    public void Wrap(string left, string right)
    {
        var sel = _buffer.Selection;
        if (sel.IsEmpty)
        {
            _buffer.ReplaceRange(sel.Caret, 0, left + right);
            _buffer.Selection = Selection.At(sel.Caret + left.Length);
        }
        else
        {
            var inner = _buffer.Document.GetText(sel.Start, sel.Length);
            _buffer.ReplaceRange(sel.Start, sel.Length, left + inner + right);
            _buffer.Selection = new Selection(sel.Start + left.Length, sel.Start + left.Length + inner.Length);
        }

        Focus();
    }

    // ----- layout -----

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 800;
        return new Size(width, Math.Max(_theme.LineHeight, _buffer.Document.LineCount * _theme.LineHeight));
    }

    // ----- rendering -----

    public override void Render(DrawingContext context)
    {
        var doc = _buffer.Document;
        context.FillRectangle(_theme.Background, new Rect(Bounds.Size));

        var caret = _buffer.Selection;
        var caretLine = doc.GetPosition(caret.Caret).Line;

        for (var line = 0; line < doc.LineCount; line++)
        {
            RenderLine(context, doc, line, caret, caretLine);
        }
    }

    private void RenderLine(DrawingContext context, TextDocument doc, int line, Selection sel, int caretLine)
    {
        var lineTop = line * _theme.LineHeight;
        var lineStart = doc.GetLineStart(line);
        var lineEnd = doc.GetLineEnd(line);
        var lineText = doc.GetText(lineStart, lineEnd - lineStart).TrimEnd('\r');
        var contentLen = lineText.Length;

        var headingLevel = HeadingLevel(line);
        var revealMarkers = caretLine == line || sel.Intersects(lineStart, Math.Max(1, lineEnd - lineStart));

        // Selection highlight for this line.
        if (sel.Length > 0)
        {
            DrawSelection(context, line, lineStart, contentLen, sel, lineText);
        }

        // Tile the line with styled segments (filling gaps between tokens with default style).
        var x = 0d;
        var pos = 0;
        var caretX = double.NaN;
        var caretCol = sel.Caret - lineStart;

        foreach (var (segStart, segEnd, type) in Segments(line, contentLen))
        {
            if (segStart > pos)
            {
                x += DrawSegment(context, lineText, pos, segStart, lineTop, DefaultStyle(headingLevel), revealMarkers,
                    ref caretX, caretCol, x);
            }

            x += DrawSegment(context, lineText, segStart, segEnd, lineTop, StyleFor(type, headingLevel), revealMarkers,
                ref caretX, caretCol, x);
            pos = segEnd;
        }

        if (pos < contentLen)
        {
            x += DrawSegment(context, lineText, pos, contentLen, lineTop, DefaultStyle(headingLevel), revealMarkers,
                ref caretX, caretCol, x);
        }

        // Caret.
        if (caretLine == line && IsFocused)
        {
            if (double.IsNaN(caretX))
            {
                caretX = caretCol <= 0 ? 0 : x;
            }

            var h = _theme.BaseFontSize * 1.3;
            var y = lineTop + (_theme.LineHeight - h) / 2;
            context.FillRectangle(_theme.Caret, new Rect(caretX, y, 1.5, h));
        }
    }

    /// <summary>Draw one styled run; returns its advance width and records the caret X if it lands inside.</summary>
    private double DrawSegment(
        DrawingContext context, string lineText, int from, int to, double lineTop, RunStyle style,
        bool revealMarkers, ref double caretX, int caretCol, double x)
    {
        var text = lineText.Substring(from, to - from);
        var brush = style.IsMarker ? (revealMarkers ? _theme.MarkerForeground : _dimMarker) : style.Foreground;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            style.Typeface, style.FontSize, brush);
        if (style.Decorations is not null)
        {
            ft.SetTextDecorations(style.Decorations);
        }

        var y = lineTop + (_theme.LineHeight - ft.Height) / 2;

        if (style.Background is not null)
        {
            context.FillRectangle(style.Background, new Rect(x, lineTop + 2, ft.Width, _theme.LineHeight - 4), 3);
        }

        context.DrawText(ft, new Point(x, y));

        // Caret X if it falls within this run.
        if (caretCol >= from && caretCol <= to && double.IsNaN(caretX))
        {
            var prefix = lineText.Substring(from, caretCol - from);
            var pft = new FormattedText(prefix, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                style.Typeface, style.FontSize, brush);
            caretX = x + pft.Width;
        }

        return ft.Width;
    }

    private void DrawSelection(DrawingContext context, int line, int lineStart, int contentLen, Selection sel, string lineText)
    {
        var lineEnd = lineStart + contentLen;
        if (sel.Start > lineEnd || sel.End < lineStart)
        {
            return; // selection doesn't touch this line
        }

        var selStartCol = Math.Clamp(sel.Start - lineStart, 0, contentLen);
        var selEndCol = Math.Clamp(sel.End - lineStart, 0, contentLen);
        if (selEndCol <= selStartCol)
        {
            return;
        }

        var x1 = MeasurePrefixWidth(lineText, selStartCol);
        var x2 = MeasurePrefixWidth(lineText, selEndCol);
        context.FillRectangle(_theme.SelectionBackground, new Rect(x1, line * _theme.LineHeight + 2, x2 - x1, _theme.LineHeight - 4));
    }

    // ----- input -----

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
        {
            _buffer.Insert(e.Text);
            e.Handled = true;
        }

        base.OnTextInput(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var doc = _buffer.Document;
        var caret = _buffer.Selection.Caret;
        var handled = true;

        switch (e.Key)
        {
            case Key.Back: _buffer.Backspace(); break;
            case Key.Delete: _buffer.Delete(); break;
            case Key.Enter: _buffer.Insert("\n"); break;
            case Key.Tab: _buffer.Insert("    "); break;
            case Key.Left: MoveCaret(_buffer.PreviousCaret(caret), shift); break;
            case Key.Right: MoveCaret(_buffer.NextCaret(caret), shift); break;
            case Key.Up: MoveCaretByLine(-1, shift); break;
            case Key.Down: MoveCaretByLine(1, shift); break;
            case Key.Home: MoveCaret(doc.GetLineStart(doc.GetPosition(caret).Line), shift); break;
            case Key.End: MoveCaret(doc.GetLineEnd(doc.GetPosition(caret).Line), shift); break;
            case Key.A when ctrl: _buffer.Selection = new Selection(0, doc.Length); break;
            case Key.Z when ctrl && shift: _buffer.Redo(); break;
            case Key.Z when ctrl: _buffer.Undo(); break;
            case Key.Y when ctrl: _buffer.Redo(); break;
            default: handled = false; break;
        }

        e.Handled = handled;
        base.OnKeyDown(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var point = e.GetPosition(this);
        var line = Math.Clamp((int)(point.Y / _theme.LineHeight), 0, _buffer.Document.LineCount - 1);
        var offset = OffsetAt(line, point.X);
        var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        MoveCaret(offset, extend);
        e.Handled = true;
        base.OnPointerPressed(e);
    }

    private void MoveCaret(int newCaret, bool extend)
    {
        newCaret = Math.Clamp(newCaret, 0, _buffer.Document.Length);
        var anchor = extend ? _buffer.Selection.Anchor : newCaret;
        _buffer.Selection = new Selection(anchor, newCaret);
    }

    private void MoveCaretByLine(int delta, bool extend)
    {
        var doc = _buffer.Document;
        var pos = doc.GetPosition(_buffer.Selection.Caret);
        var targetLine = Math.Clamp(pos.Line + delta, 0, doc.LineCount - 1);
        MoveCaret(doc.GetOffset(targetLine, pos.Column), extend);
    }

    // ----- helpers -----

    private int HeadingLevel(int line)
    {
        var tokens = _model.LineTokens(line);
        if (tokens.Count > 0 && tokens[0].Type == TokenType.HeadingMarker && int.TryParse(tokens[0].Meta, out var l))
        {
            return l;
        }

        return 0;
    }

    private RunStyle DefaultStyle(int headingLevel)
    {
        var style = _theme.Resolve(TokenType.Text);
        return headingLevel > 0 ? style with { FontSize = _theme.HeadingSize(headingLevel), Typeface = HeadingTypeface(style) } : style;
    }

    private RunStyle StyleFor(TokenType type, int headingLevel)
    {
        var style = _theme.Resolve(type, headingLevel);
        if (headingLevel > 0 && !style.IsMarker)
        {
            style = style with { FontSize = _theme.HeadingSize(headingLevel) };
        }

        return style;
    }

    private static Typeface HeadingTypeface(RunStyle baseStyle) =>
        new(baseStyle.Typeface.FontFamily, FontStyle.Normal, FontWeight.Bold);

    /// <summary>The token spans on <paramref name="line"/>, clamped to <paramref name="contentLen"/> and line-relative.</summary>
    private IEnumerable<(int Start, int End, TokenType Type)> Segments(int line, int contentLen)
    {
        var lineStart = _buffer.Document.GetLineStart(line);
        foreach (var t in _model.LineTokens(line))
        {
            var s = Math.Clamp(t.Start - lineStart, 0, contentLen);
            var en = Math.Clamp(t.End - lineStart, 0, contentLen);
            if (en > s)
            {
                yield return (s, en, t.Type);
            }
        }
    }

    private double MeasurePrefixWidth(string lineText, int col)
    {
        if (col <= 0)
        {
            return 0;
        }

        var prefix = lineText.Substring(0, Math.Min(col, lineText.Length));
        var ft = new FormattedText(prefix, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            _theme.Resolve(TokenType.Text).Typeface, _theme.BaseFontSize, _theme.Foreground);
        return ft.Width;
    }

    private int OffsetAt(int line, double x)
    {
        var doc = _buffer.Document;
        var lineStart = doc.GetLineStart(line);
        var lineEnd = doc.GetLineEnd(line);
        var text = doc.GetText(lineStart, lineEnd - lineStart).TrimEnd('\r');
        // Walk grapheme boundaries, picking the one whose prefix width is closest to x.
        var best = 0;
        var bestDist = double.MaxValue;
        var col = 0;
        while (true)
        {
            var w = MeasurePrefixWidth(text, col);
            var d = Math.Abs(w - x);
            if (d < bestDist)
            {
                bestDist = d;
                best = col;
            }

            if (col >= text.Length)
            {
                break;
            }

            col = Grapheme.NextBoundary(text, col);
        }

        return lineStart + best;
    }
}
