using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Puffinote.Components.Input;
using Puffinote.Components.Theming;
using Puffinote.Text.Editing;

namespace Plaintain.Control.Controls;

/// <summary>
/// Plaintain's editing surface: a custom-drawn, virtualized plaintext editor over
/// <see cref="DocumentBuffer"/>.
///
/// <para><b>Uniform rows.</b> Every row is exactly <see cref="RowHeight"/> tall and every character
/// exactly <see cref="CharWidth"/> wide, because the font is monospace by design (PRD §3). That single
/// decision turns all of this editor's geometry into arithmetic: the row a pixel is in is a division,
/// the pixel a row starts at is a multiplication, and — the part that matters for a 50 MB log — the
/// number of rows a line wraps into is <c>ceil(columns / columnsPerRow)</c>, computed without
/// measuring anything. Typage's surface cannot do any of that (its blocks have varying heights and
/// collapsing margins, so it carries a pixel prefix sum), which is exactly why the two surfaces are
/// separate and only their semantics are shared (proposal 0003 §5).</para>
///
/// <para><b>Scrolling</b> is delegated: the control implements <see cref="IScrollable"/>, so a host's
/// <see cref="ScrollViewer"/> drives an offset instead of arranging a million rows' worth of control.
/// Only the rows in view are ever built or drawn.</para>
/// </summary>
public sealed partial class PlainTextEditor : Avalonia.Controls.Control, IScrollable
{
    /// <summary>Inset from the text area's edges, so a glyph never touches the gutter or the caret clips.</summary>
    private const double PadX = 6;

    /// <summary>Columns a tab advances to the next multiple of. Fixed at 4 — see PRD §8.</summary>
    internal const int TabWidth = 4;

    /// <summary>Gap between the gutter's right edge and the first character.</summary>
    private const double GutterGap = 10;

    private Vector _offset;
    private Typeface _typeface;

    // The font's metrics, re-measured whenever the family or size changes. Fields rather than auto
    // properties because the whole surface reads them on every painted row, and because MeasureFont
    // writes all three together — a set of three setters would let two of them be true at once.
#pragma warning disable IDE0032 // Use auto property
    private double _charWidth = 8;
    private double _rowHeight = 16;
#pragma warning restore IDE0032

    /// <summary>
    /// Cumulative row counts, one entry per line plus a terminator: <c>_rowStarts[i]</c> is the visual
    /// row line <c>i</c> begins on. Built only while wrapping — with wrap off a line <i>is</i> a row and
    /// the map is the identity, which is the whole reason a log opens instantly.
    /// </summary>
    private int[]? _rowStarts;

    /// <summary>The widest line seen while wrap is off, in columns — the horizontal scroll extent.</summary>
    private int _widestColumns;

    /// <summary>The family's blinking caret (<see cref="CaretBlink"/>) — one rate across every app.</summary>
    private readonly CaretBlink _caretBlink;

    public PlainTextEditor()
    {
        Focusable = true;
        _typeface = new Typeface(FontFamily);
        WatchFocus();
        _caretBlink = CaretBlink.Attach(this);

        // The surface resolves its palette from ActualThemeVariant at paint time (see Render), so a theme
        // switch changes what it *would* draw — but nothing about a theme change asks a custom-drawn control
        // to draw again. Without this, choosing Light in Preferences leaves the text on the old canvas until
        // something else happens to invalidate it.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Buffer.Changed += (_, _) =>
        {
            // Every edit and every caret move comes through here, which is exactly when the caret has to
            // go solid again — it must not be mid-blink-out at the moment a character lands under it.
            _caretBlink.Show();
            InvalidateMetrics();
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>The document being edited, its undo history, and the caret.</summary>
    public DocumentBuffer Buffer { get; } = new();

    /// <summary>Raised when the caret, the selection, or the document changes — for a host's status bar.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The whole document as text. Setting it loads a new document (and clears undo).</summary>
    public string Text
    {
        get => Buffer.Document.GetText();
        set => Load(value);
    }

    /// <summary>Replace the document, optionally keeping it marked dirty (a crash-recovery restore does).</summary>
    public void Load(string? text, bool markSaved = true)
    {
        Buffer.Load(text, markSaved);
        _offset = default;
        InvalidateMetrics();
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Options ----------------------------------------------------------

    public static readonly StyledProperty<bool> WordWrapProperty =
        AvaloniaProperty.Register<PlainTextEditor, bool>(nameof(WordWrap));

    /// <summary>
    /// Whether long lines fold at the viewport's edge. <b>Off by default</b> — the Notepad expectation
    /// for a log, where a wrapped line hides the column structure that makes the log readable (PRD §3).
    /// </summary>
    public bool WordWrap
    {
        get => GetValue(WordWrapProperty);
        set => SetValue(WordWrapProperty, value);
    }

    public static readonly StyledProperty<bool> ShowLineNumbersProperty =
        AvaloniaProperty.Register<PlainTextEditor, bool>(nameof(ShowLineNumbers), defaultValue: true);

    /// <summary>Whether the line-number gutter is drawn. Typage has none and shouldn't; this is the difference.</summary>
    public bool ShowLineNumbers
    {
        get => GetValue(ShowLineNumbersProperty);
        set => SetValue(ShowLineNumbersProperty, value);
    }

    public static readonly StyledProperty<bool> HighlightCurrentLineProperty =
        AvaloniaProperty.Register<PlainTextEditor, bool>(nameof(HighlightCurrentLine), defaultValue: true);

    /// <summary>
    /// Whether the row the caret is on is washed with a faint tint. <b>On by default</b>, which is what
    /// this surface has always done — turning it off is the new choice, not the new default, and changing
    /// which one is the default would move the line under everyone who never asked.
    /// <para>
    /// It is worth being able to turn off: the tint is a band across the full width of the viewport, and on
    /// a log or a column-aligned table that band is the same shape as the data, so it reads as a highlight
    /// on the <em>content</em> rather than on the caret. The gutter still marks the caret's line in the
    /// brighter ink either way, so nothing is lost by hiding this.
    /// </para>
    /// </summary>
    public bool HighlightCurrentLine
    {
        get => GetValue(HighlightCurrentLineProperty);
        set => SetValue(HighlightCurrentLineProperty, value);
    }

    /// <summary>
    /// The surface's font. Registered as the editor's <i>own</i> property rather than adopted from
    /// <see cref="TextBlock"/>, and that is load-bearing rather than a style choice: the text properties
    /// on <c>TextElement</c> are <b>inherited</b>, so adopting one lets the nearest ancestor that sets a
    /// font — a themed <see cref="Window"/>, a Puffinote tab — silently win over the default below, and
    /// nothing here sets a family explicitly. The surface would then draw plaintext in the proportional
    /// UI font while every caret, selection and hit test still stepped in fixed <see cref="CharWidth"/>
    /// units, so the caret drifted further from the glyphs the further along a line it went (PRD §3 and
    /// the uniform-rows rule this class opens with). A non-inherited property can only be changed by a
    /// host that means to change it.
    /// </summary>
    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<PlainTextEditor, FontFamily>(nameof(FontFamily),
            defaultValue: new FontFamily("Menlo, Consolas, DejaVu Sans Mono, monospace"));

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>Not inherited either, for the reason <see cref="FontFamilyProperty"/> gives.</summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<PlainTextEditor, double>(nameof(FontSize), defaultValue: 13.0);

    /// <summary>The authored text size — what the reader chose in preferences, before any zoom.</summary>
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<PlainTextEditor, double>(nameof(Zoom), defaultValue: ZoomScale.Default);

    /// <summary>
    /// The reading zoom, on the family's shared ladder (<see cref="ZoomScale"/>). It multiplies
    /// <see cref="FontSize"/> rather than replacing it, so zooming and then changing the preferred size
    /// compose instead of one silently overwriting the other — and because "back to 100%" has to mean
    /// back to <em>your</em> size, not back to 13.
    /// </summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, ZoomScale.Clamp(value));
    }

    /// <summary>The size text is actually drawn at: the authored size taken up by the zoom. Every measure
    /// and every draw goes through this, so the caret, the gutter and the glyphs can't disagree.</summary>
    internal double ScaledFontSize => FontSize * Zoom;

    /// <summary>
    /// True when the document may not be written back — its bytes were not valid UTF-8, so the encoding
    /// it was read with is a guess (see <c>TextFileCodec</c>). Editing is allowed; the shell blocks the
    /// save. Drawn as a dimmed gutter so the state is visible, not only enforced.
    /// </summary>
    public bool IsReadOnlyEncoding { get; set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HighlightCurrentLineProperty)
        {
            InvalidateVisual(); // a repaint is enough: nothing about the tint changes a measurement
            return;
        }

        if (change.Property == WordWrapProperty || change.Property == FontFamilyProperty
            || change.Property == FontSizeProperty || change.Property == ShowLineNumbersProperty
            || change.Property == ZoomProperty)
        {
            _typeface = new Typeface(FontFamily);
            InvalidateMetrics();
            InvalidateVisual();
        }
    }

    // ---- Metrics ----------------------------------------------------------

    internal double RowHeight => _rowHeight;

    internal double CharWidth => _charWidth;

    /// <summary>Width of the line-number gutter, or 0 when it is off. Sized to the largest line number.</summary>
    internal double GutterWidth => ShowLineNumbers
        ? Math.Ceiling(Buffer.Document.LineCount.ToString(System.Globalization.CultureInfo.InvariantCulture).Length * _charWidth) + (2 * PadX) + GutterGap
        : 0;

    /// <summary>Left edge of the text area, in control coordinates.</summary>
    internal double TextLeft => GutterWidth + PadX;

    /// <summary>Width available for text, before horizontal scrolling.</summary>
    internal double TextWidth => Math.Max(_charWidth, Viewport.Width - TextLeft - PadX);

    /// <summary>Characters that fit on one row while wrapping — the arithmetic the monospace font buys.</summary>
    internal int ColumnsPerRow => Math.Max(1, (int)(TextWidth / _charWidth));

    private void MeasureFont()
    {
        // A monospace face advances every character equally, so one measurement describes all of them.
        double size = ScaledFontSize;
        var probe = new FormattedText("0", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _typeface, size, Brushes.Black);
        _charWidth = probe.WidthIncludingTrailingWhitespace > 0 ? probe.WidthIncludingTrailingWhitespace : size * 0.6;
        _rowHeight = Math.Ceiling(probe.Height > 0 ? probe.Height * 1.25 : size * 1.5);
    }

    /// <summary>Forget the row map and the widest-line cache; both are rebuilt on the next layout.</summary>
    private void InvalidateMetrics()
    {
        _rowStarts = null;
        _widestColumns = 0;
        InvalidateMeasure();
    }

    /// <summary>
    /// The expanded column a character offset sits at on its line: a tab advances to the next tab stop
    /// rather than counting as one, so the column arithmetic matches what is drawn.
    /// </summary>
    internal int ColumnOf(int line, int offset)
    {
        var doc = Buffer.Document;
        int start = doc.GetLineStart(line);
        int column = 0;
        for (int i = start; i < offset && i < doc.Length; i++)
        {
            column = doc[i] == '\t' ? ((column / TabWidth) + 1) * TabWidth : column + 1;
        }

        return column;
    }

    /// <summary>The offset on <paramref name="line"/> that sits at expanded <paramref name="column"/> (clamped to its end).</summary>
    internal int OffsetAtColumn(int line, int column)
    {
        var doc = Buffer.Document;
        int start = doc.GetLineStart(line);
        int end = doc.GetLineEnd(line);
        int c = 0;
        for (int i = start; i < end; i++)
        {
            if (c >= column)
            {
                return i;
            }

            c = doc[i] == '\t' ? ((c / TabWidth) + 1) * TabWidth : c + 1;
        }

        return end;
    }

    /// <summary>Expanded width of a whole line, in columns.</summary>
    internal int LineColumns(int line) => ColumnOf(line, Buffer.Document.GetLineEnd(line));

    /// <summary>Visual rows a line occupies: one, unless wrapping folds it.</summary>
    internal int RowsInLine(int line) =>
        !WordWrap ? 1 : Math.Max(1, (LineColumns(line) + ColumnsPerRow - 1) / ColumnsPerRow);

    /// <summary>
    /// Build (or reuse) the line → first-row map. Only wrapping needs one; with wrap off the map is the
    /// identity and never allocated, which is what keeps opening a million-line log O(1).
    /// </summary>
    private int[]? RowMap()
    {
        if (!WordWrap)
        {
            return null;
        }

        if (_rowStarts is { } cached && cached.Length == Buffer.Document.LineCount + 1)
        {
            return cached;
        }

        int lines = Buffer.Document.LineCount;
        var map = new int[lines + 1];
        int row = 0;
        for (int line = 0; line < lines; line++)
        {
            map[line] = row;
            row += RowsInLine(line);
        }

        map[lines] = row;
        _rowStarts = map;
        return map;
    }

    /// <summary>Total visual rows in the document.</summary>
    internal int TotalRows => RowMap() is { } map ? map[^1] : Buffer.Document.LineCount;

    /// <summary>The first visual row of <paramref name="line"/>.</summary>
    internal int FirstRowOf(int line) => RowMap() is { } map ? map[Math.Clamp(line, 0, map.Length - 1)] : line;

    /// <summary>The (line, segment) a visual row belongs to. Segment 0 is a line's first row.</summary>
    internal (int Line, int Segment) LineAtRow(int row)
    {
        if (RowMap() is not { } map)
        {
            return (Math.Clamp(row, 0, Math.Max(0, Buffer.Document.LineCount - 1)), 0);
        }

        // Binary search the cumulative map: the last line whose first row is <= row.
        int lo = 0, hi = map.Length - 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (map[mid] <= row)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return (lo, Math.Max(0, row - map[lo]));
    }

    // ---- IScrollable ------------------------------------------------------

    /// <summary>
    /// The full content size. A ScrollViewer reads this to size its thumbs; because we also report a
    /// viewport and take the offset, it never arranges us any larger than the window.
    /// </summary>
    public Size Extent => new(
        WordWrap ? Viewport.Width : (Math.Max(_widestColumns, 1) * _charWidth) + TextLeft + PadX,
        Math.Max(1, TotalRows) * _rowHeight);

    public Vector Offset
    {
        get => _offset;
        set
        {
            var clamped = new Vector(
                Math.Max(0, Math.Min(value.X, Math.Max(0, Extent.Width - Viewport.Width))),
                Math.Max(0, Math.Min(value.Y, Math.Max(0, Extent.Height - Viewport.Height))));
            if (clamped != _offset)
            {
                _offset = clamped;
                InvalidateVisual();
            }
        }
    }

    public Size Viewport { get; private set; }

    /// <summary>Whether the host may scroll us sideways — only when lines are not folded.</summary>
    public bool CanHorizontallyScroll
    {
        get => !WordWrap;
        set { } // the surface decides this from WordWrap; a viewer's opinion doesn't apply
    }

    public bool CanVerticallyScroll
    {
        get => true;
        set { }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        MeasureFont();

        // Fill the viewport rather than the content: the ScrollViewer scrolls us, so growing to the
        // document's height would mean laying out (and painting) every row of a 50 MB file at once.
        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Viewport = finalSize;
        _rowStarts = null; // the wrap width may have changed with the viewport
        Offset = _offset;  // re-clamp against the new extent
        return finalSize;
    }
}
