using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
/// exact and simple. The surface is line-virtualized (only the visible slice is drawn, via
/// <see cref="ILogicalScrollable"/>) and re-tokenizes incrementally from the first changed line.
/// Full glyph-hiding with an offset↔visual map, IME, and an automation peer are the dedicated later
/// phases (PRD §11). The public API and the engine seam below are stable so that work slots in.
/// </remarks>
public class MarkdownEditor : Avalonia.Controls.Control, ILogicalScrollable
{
    private readonly DocumentBuffer _buffer = new();
    private MarkdownModel _model;
    private MarkdownTheme _theme = MarkdownTheme.Default;
    private IBrush _dimMarker = new SolidColorBrush(Color.FromArgb(0x46, 0x6E, 0x76, 0x81));

    // Virtualized logical-scroll state: only the lines inside the viewport are drawn, and the
    // horizontal extent grows as wide lines are encountered (no whole-document width scan).
    private Vector _offset;
    private Size _viewport;
    private double _extentWidth;
    private bool _canHorizontallyScroll = true;
    private bool _canVerticallyScroll = true;
    private EventHandler? _scrollInvalidated;

    public MarkdownEditor()
    {
        Focusable = true;
        _model = MarkdownModel.Build(_buffer.Document);
        _buffer.TextChanged += (_, _) =>
        {
            // Incremental re-tokenization: reuse the unchanged prefix and re-run only from the first
            // changed line down (PRD §8.2). Equivalent to a full rebuild — see IncrementalTokenizerTests.
            _model = _model.Update(_buffer.Document, _buffer.LastChangedLine, out _);
            RaiseScrollInvalidated(EventArgs.Empty); // line count / extent may have changed
            InvalidateVisual();
            TextChanged?.Invoke(this, EventArgs.Empty);
        };
        _buffer.Changed += (_, _) =>
        {
            InvalidateVisual();
            ScrollCaretIntoView();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
        ContextMenu = BuildContextMenu();
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

    /// <summary>Live document statistics for the host's status bar (cached; recomputed on text change only).</summary>
    public DocumentStats Stats => _buffer.Stats;

    /// <summary>The caret position as a 1-based line:column for the status bar.</summary>
    public LinePosition CaretPosition => _buffer.Document.GetPosition(_buffer.Selection.Caret);

    /// <summary>The number of grapheme clusters currently selected (0 when the selection is empty).</summary>
    public int SelectedCharacterCount
    {
        get
        {
            var sel = _buffer.Selection;
            return sel.IsEmpty ? 0 : Grapheme.Count(_buffer.Document.GetText(sel.Start, sel.Length));
        }
    }

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
            _extentWidth = 0; // fonts changed — let the horizontal extent regrow as lines redraw
            RaiseScrollInvalidated(EventArgs.Empty);
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

    /// <summary>Wrap the selection as a link <c>[text](url)</c>, leaving the caret in the URL slot.</summary>
    public void InsertLink()
    {
        var sel = _buffer.Selection;
        var text = sel.IsEmpty ? string.Empty : _buffer.Document.GetText(sel.Start, sel.Length);
        var replacement = $"[{text}]()";
        _buffer.ReplaceRange(sel.Start, sel.Length, replacement);
        _buffer.Selection = Selection.At(sel.Start + replacement.Length - 1); // inside ()
        Focus();
    }

    /// <summary>Strip any heading marker from the caret's line, turning it back into a paragraph.</summary>
    public void SetParagraph()
    {
        var doc = _buffer.Document;
        var line = doc.GetPosition(_buffer.Selection.Caret).Line;
        var lineStart = doc.GetLineStart(line);
        var text = doc.GetLineText(line).TrimEnd('\r');
        var trimmed = text.TrimStart('#', ' ');
        if (trimmed.Length != text.Length)
        {
            _buffer.ReplaceRange(lineStart, text.Length, trimmed);
        }
    }

    /// <summary>Select the whole document.</summary>
    public void SelectAll() => _buffer.Selection = new Selection(0, _buffer.Document.Length);

    // ----- clipboard (PRD §7.2 — copy yields Markdown source) -----

    public async Task CopyAsync()
    {
        var sel = _buffer.Selection;
        if (sel.IsEmpty || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(_buffer.Document.GetText(sel.Start, sel.Length));
    }

    public async Task CutAsync()
    {
        if (_buffer.Selection.IsEmpty)
        {
            return;
        }

        await CopyAsync();
        _buffer.Insert(string.Empty); // replaces (deletes) the selection
    }

    public async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text))
        {
            _buffer.Insert(text);
        }
    }

    private ContextMenu BuildContextMenu()
    {
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        var menu = new ContextMenu();
        menu.Items.Add(Item("Cut", () => _ = CutAsync()));
        menu.Items.Add(Item("Copy", () => _ = CopyAsync()));
        menu.Items.Add(Item("Paste", () => _ = PasteAsync()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Select All", SelectAll));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Bold", ToggleBold));
        menu.Items.Add(Item("Italic", ToggleItalic));
        menu.Items.Add(Item("Inline Code", ToggleInlineCode));
        menu.Items.Add(Item("Strikethrough", ToggleStrikethrough));
        menu.Items.Add(Item("Link…", InsertLink));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Undo", () => _buffer.Undo()));
        menu.Items.Add(Item("Redo", () => _buffer.Redo()));
        return menu;
    }

    // ----- layout & logical scrolling -----

    protected override Size MeasureOverride(Size availableSize)
    {
        // Under logical scrolling the presenter constrains us to the viewport; report that. When a
        // dimension is unconstrained (not in a scroll viewer), fall back to the content size.
        var w = double.IsFinite(availableSize.Width) ? availableSize.Width : _extentWidth;
        var h = double.IsFinite(availableSize.Height)
            ? availableSize.Height
            : Math.Max(_theme.LineHeight, _buffer.Document.LineCount * _theme.LineHeight);
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_viewport != finalSize)
        {
            _viewport = finalSize;
            RaiseScrollInvalidated(EventArgs.Empty);
        }

        return finalSize;
    }

    Size IScrollable.Extent => new(
        Math.Max(_theme.PaddingLeft * 2 + _extentWidth, _viewport.Width),
        Math.Max(_theme.LineHeight, _buffer.Document.LineCount * _theme.LineHeight) + _theme.PaddingTop * 2);

    Size IScrollable.Viewport => _viewport;

    Vector IScrollable.Offset
    {
        get => _offset;
        set
        {
            var clamped = ClampOffset(value);
            if (clamped != _offset)
            {
                _offset = clamped;
                InvalidateVisual();
            }
        }
    }

    /// <summary>Whether horizontal scrolling is enabled (set by the hosting scroll viewer).</summary>
    public bool CanHorizontallyScroll
    {
        get => _canHorizontallyScroll;
        set => _canHorizontallyScroll = value;
    }

    /// <summary>Whether vertical scrolling is enabled (set by the hosting scroll viewer).</summary>
    public bool CanVerticallyScroll
    {
        get => _canVerticallyScroll;
        set => _canVerticallyScroll = value;
    }

    bool ILogicalScrollable.IsLogicalScrollEnabled => true;

    Size ILogicalScrollable.ScrollSize => new(_theme.BaseFontSize, _theme.LineHeight);

    Size ILogicalScrollable.PageScrollSize => new(_viewport.Width, _viewport.Height);

    event EventHandler? ILogicalScrollable.ScrollInvalidated
    {
        add => _scrollInvalidated += value;
        remove => _scrollInvalidated -= value;
    }

    bool ILogicalScrollable.BringIntoView(Avalonia.Controls.Control target, Rect targetRect) => false;

    Avalonia.Controls.Control? ILogicalScrollable.GetControlInDirection(NavigationDirection direction, Avalonia.Controls.Control? from) => null;

    void ILogicalScrollable.RaiseScrollInvalidated(EventArgs e) => RaiseScrollInvalidated(e);

    private void RaiseScrollInvalidated(EventArgs e) => _scrollInvalidated?.Invoke(this, e);

    private Vector ClampOffset(Vector value)
    {
        var extent = ((IScrollable)this).Extent;
        var maxX = Math.Max(0, extent.Width - _viewport.Width);
        var maxY = Math.Max(0, extent.Height - _viewport.Height);
        return new Vector(Math.Clamp(value.X, 0, maxX), Math.Clamp(value.Y, 0, maxY));
    }

    // ----- rendering -----

    public override void Render(DrawingContext context)
    {
        var doc = _buffer.Document;
        context.FillRectangle(_theme.Background, new Rect(Bounds.Size));

        var caret = _buffer.Selection;
        var caretLine = doc.GetPosition(caret.Caret).Line;

        // Draw only the lines intersecting the viewport (virtualization), translated by the scroll
        // offset. The viewport height is 0 until the control is arranged in a scroll viewer.
        var lineH = _theme.LineHeight;
        var viewportH = _viewport.Height > 0 ? _viewport.Height : Bounds.Height;
        var contentTop = _offset.Y - _theme.PaddingTop;
        var first = Math.Max(0, (int)(contentTop / lineH));
        var last = Math.Min(doc.LineCount, (int)((contentTop + viewportH) / lineH) + 1);

        // Content is drawn in document space, inset by the padding and shifted by the scroll offset.
        using (context.PushTransform(Matrix.CreateTranslation(_theme.PaddingLeft - _offset.X, _theme.PaddingTop - _offset.Y)))
        {
            var maxWidth = _extentWidth;
            for (var line = first; line < last; line++)
            {
                maxWidth = Math.Max(maxWidth, RenderLine(context, doc, line, caret, caretLine));
            }

            if (maxWidth > _extentWidth)
            {
                _extentWidth = maxWidth; // grow the horizontal extent as wide lines come into view
                RaiseScrollInvalidated(EventArgs.Empty);
            }
        }
    }

    private double RenderLine(DrawingContext context, TextDocument doc, int line, Selection sel, int caretLine)
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
        var caretX = double.NaN;
        var caretCol = sel.Caret - lineStart;

        foreach (var (from, to, style) in StyledSegments(line, contentLen, headingLevel))
        {
            x += DrawSegment(context, lineText, from, to, lineTop, style, revealMarkers, ref caretX, caretCol, x);
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

        return x; // total content width of this line (drives the horizontal extent)
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

        // Caret X if it falls within this run (measured with trailing whitespace included).
        if (caretCol >= from && caretCol <= to && double.IsNaN(caretX))
        {
            caretX = x + MeasureWidth(lineText.Substring(from, caretCol - from), style);
        }

        return MeasureWidth(text, style); // advance includes trailing whitespace, unlike ft.Width
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

        var headingLevel = HeadingLevel(line);
        var x1 = ColumnX(line, contentLen, headingLevel, lineText, selStartCol);
        var x2 = ColumnX(line, contentLen, headingLevel, lineText, selEndCol);
        context.FillRectangle(_theme.SelectionBackground, new Rect(x1, line * _theme.LineHeight + 2, x2 - x1, _theme.LineHeight - 4));
    }

    // ----- input -----

    protected override void OnTextInput(TextInputEventArgs e)
    {
        // A Cmd/Ctrl+key combination is a command, not text — don't type the letter. (Ctrl+Alt is
        // left alone so AltGr characters still work.)
        var command = _modifiers.HasFlag(KeyModifiers.Meta)
            || (_modifiers.HasFlag(KeyModifiers.Control) && !_modifiers.HasFlag(KeyModifiers.Alt));

        if (!command && !string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
        {
            _buffer.Insert(e.Text);
        }

        e.Handled = true;
        base.OnTextInput(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        _modifiers = e.KeyModifiers;
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

            // Clipboard.
            case Key.C when ctrl: _ = CopyAsync(); break;
            case Key.X when ctrl: _ = CutAsync(); break;
            case Key.V when ctrl: _ = PasteAsync(); break;

            // Formatting (the shortcuts a Markdown editor is expected to have).
            case Key.B when ctrl: ToggleBold(); break;
            case Key.I when ctrl: ToggleItalic(); break;
            case Key.E when ctrl: ToggleInlineCode(); break;
            case Key.K when ctrl: InsertLink(); break;
            case Key.D0 when ctrl: SetParagraph(); break;
            case >= Key.D1 and <= Key.D6 when ctrl: ApplyHeading(e.Key - Key.D1 + 1); break;

            case Key.A when ctrl: SelectAll(); break;
            case Key.Z when ctrl && shift: _buffer.Redo(); break;
            case Key.Z when ctrl: _buffer.Undo(); break;
            case Key.Y when ctrl: _buffer.Redo(); break;
            default: handled = false; break;
        }

        e.Handled = handled;
        base.OnKeyDown(e);
    }

    private KeyModifiers _modifiers;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // Let a right-click through so the context menu can open; only the left button moves the caret.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            base.OnPointerPressed(e);
            return;
        }

        Focus();
        var point = e.GetPosition(this);
        // The viewport is inset by the padding and translated by the scroll offset; map back.
        var contentY = point.Y - _theme.PaddingTop + _offset.Y;
        var contentX = point.X - _theme.PaddingLeft + _offset.X;
        var line = Math.Clamp((int)(contentY / _theme.LineHeight), 0, _buffer.Document.LineCount - 1);
        var offset = OffsetAt(line, contentX);
        var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        MoveCaret(offset, extend);
        e.Handled = true;
        base.OnPointerPressed(e);
    }

    /// <summary>Scroll so the caret stays visible after an edit or navigation (we are the scrollable).</summary>
    private void ScrollCaretIntoView()
    {
        if (_viewport.Width <= 0 || _viewport.Height <= 0)
        {
            return; // not arranged in a viewport yet (e.g. headless construction)
        }

        var doc = _buffer.Document;
        var pos = doc.GetPosition(_buffer.Selection.Caret);
        var lineStart = doc.GetLineStart(pos.Line);
        var text = doc.GetText(lineStart, doc.GetLineEnd(pos.Line) - lineStart).TrimEnd('\r');
        var x = ColumnX(pos.Line, text.Length, HeadingLevel(pos.Line), text, Math.Min(pos.Column, text.Length));
        _extentWidth = Math.Max(_extentWidth, x + 12); // ensure we can scroll to a caret on a wide line

        // Caret position in scroll-content space (which includes the padding).
        var px = x + _theme.PaddingLeft;
        var py = pos.Line * _theme.LineHeight + _theme.PaddingTop;
        const double margin = 8;

        var newY = _offset.Y;
        if (py < _offset.Y)
        {
            newY = py;
        }
        else if (py + _theme.LineHeight > _offset.Y + _viewport.Height)
        {
            newY = py + _theme.LineHeight - _viewport.Height;
        }

        var newX = _offset.X;
        if (px < _offset.X + margin)
        {
            newX = Math.Max(0, px - margin);
        }
        else if (px + margin > _offset.X + _viewport.Width)
        {
            newX = px + margin - _viewport.Width;
        }

        var target = ClampOffset(new Vector(newX, newY));
        if (target != _offset)
        {
            _offset = target;
            InvalidateVisual();
            RaiseScrollInvalidated(EventArgs.Empty);
        }
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
        // On a heading line, scale everything — including the leading '#' marker — to the heading
        // size so the markers sit at the same size as the heading text on that line.
        if (headingLevel > 0)
        {
            style = style with { FontSize = _theme.HeadingSize(headingLevel) };
        }

        return style;
    }

    private static Typeface HeadingTypeface(RunStyle baseStyle) =>
        new(baseStyle.Typeface.FontFamily, FontStyle.Normal, FontWeight.Bold);

    /// <summary>
    /// The full styled tiling of a line: every token span resolved to a <see cref="RunStyle"/>,
    /// with the gaps between tokens (e.g. leading indent) filled by the default style. The single
    /// source of truth used by both rendering and width measurement, so the caret, the selection
    /// rectangle, and mouse hit-testing all agree with the drawn glyphs even on styled lines.
    /// </summary>
    private IEnumerable<(int From, int To, RunStyle Style)> StyledSegments(int line, int contentLen, int headingLevel)
    {
        var lineStart = _buffer.Document.GetLineStart(line);
        var pos = 0;
        foreach (var t in _model.LineTokens(line))
        {
            var s = Math.Clamp(t.Start - lineStart, 0, contentLen);
            var en = Math.Clamp(t.End - lineStart, 0, contentLen);
            if (en <= s)
            {
                continue;
            }

            if (s > pos)
            {
                yield return (pos, s, DefaultStyle(headingLevel));
            }

            yield return (s, en, StyleFor(t.Type, headingLevel));
            pos = en;
        }

        if (pos < contentLen)
        {
            yield return (pos, contentLen, DefaultStyle(headingLevel));
        }
    }

    /// <summary>The x-offset of column <paramref name="col"/> on <paramref name="line"/>, measured with each run's real font.</summary>
    private double ColumnX(int line, int contentLen, int headingLevel, string lineText, int col)
    {
        if (col <= 0)
        {
            return 0;
        }

        var x = 0d;
        foreach (var (from, to, style) in StyledSegments(line, contentLen, headingLevel))
        {
            if (col <= from)
            {
                break;
            }

            var end = Math.Min(col, to);
            x += MeasureWidth(lineText.Substring(from, end - from), style);
            if (col <= to)
            {
                break;
            }
        }

        return x;
    }

    /// <summary>
    /// The advance width of <paramref name="text"/> in <paramref name="style"/>, <b>including any
    /// trailing whitespace</b>. <see cref="FormattedText.Width"/> omits trailing whitespace, which
    /// made a run ending in a space (e.g. a heading marker <c>"## "</c> or a word before emphasis)
    /// advance too little so the next run drew on top of the space — the missing-space bug. Appending
    /// a non-space sentinel makes those spaces interior (and thus measured) and is then subtracted out.
    /// </summary>
    private double MeasureWidth(string text, RunStyle style)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        if (char.IsWhiteSpace(text[^1]))
        {
            return RawWidth(text + ".", style) - RawWidth(".", style);
        }

        return RawWidth(text, style);
    }

    private static double RawWidth(string text, RunStyle style) =>
        new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            style.Typeface, style.FontSize, style.Foreground).Width;

    private int OffsetAt(int line, double x)
    {
        var doc = _buffer.Document;
        var lineStart = doc.GetLineStart(line);
        var lineEnd = doc.GetLineEnd(line);
        var text = doc.GetText(lineStart, lineEnd - lineStart).TrimEnd('\r');
        var headingLevel = HeadingLevel(line);
        // Walk grapheme boundaries, picking the one whose styled prefix width is closest to x.
        var best = 0;
        var bestDist = double.MaxValue;
        var col = 0;
        while (true)
        {
            var w = ColumnX(line, text.Length, headingLevel, text, col);
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
