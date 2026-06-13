using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Reactive;
using Cellestra.Core.Formulas.References;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Cellestra.Desktop.Services;
using SAlign = Cellestra.Core.Styling.HorizontalAlignment;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// The custom-drawn, virtualized spreadsheet grid (ARCHITECTURE §6). Reads cell data directly
/// from the active <see cref="Worksheet"/> — no per-cell view models — and draws only the
/// visible cells. Handles selection, keyboard navigation, scrolling, and inline editing.
/// </summary>
public sealed partial class SpreadsheetGrid : Panel
{
    public const double HeaderWidth = 46;
    public const double HeaderHeight = 22;

    // Inset around cell text so glyphs have a little breathing room from the gridlines. The same
    // values drive both the drawn text (see DrawCellContent/DrawHashes) and the inline editor, so
    // committing an edit never shifts the text. CellPadX is the per-side horizontal inset; CellPadY
    // the per-side vertical inset.
    private const double CellPadX = 5;
    private const double CellPadY = 2;

    private const double EditorPadX = CellPadX;
    private const double EditorPadY = CellPadY;

    private readonly TextBox _editor = new()
    {
        IsVisible = false,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(EditorPadX, EditorPadY, EditorPadX, 0),
        Background = Brushes.White,
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top, // sit at the top of the cell, like the drawn text
    };

    // A TextBox can't render underline/strikethrough, so a thin overlay above the editor draws those
    // decorations itself — using the same text metrics the grid uses for cells, so they line up.
    private readonly EditorDecorationLayer _editorDecorations;

    private readonly Surface _surface;
    private WorkbookSession? _session;
    private int _scrollRow;
    private int _scrollColumn;
    private double _zoom = 1.0;
    private bool _editing;
    private bool _selecting;
    private int? _tabOriginColumn; // the column a Tab sequence began at, so Enter returns to it (#19)

    private readonly List<(int Index, double Offset, double Size)> _visibleColumns = new();
    private readonly List<(int Index, double Offset, double Size)> _visibleRows = new();

    private GridPalette _palette = GridPalette.For(Avalonia.Styling.ThemeVariant.Light);

    public SpreadsheetGrid()
    {
        Focusable = true;
        _surface = new Surface(this);
        _editorDecorations = new EditorDecorationLayer(this);
        Children.Add(_surface);            // draws the grid
        Children.Add(_editor);             // floats above for inline editing
        Children.Add(_editorDecorations);  // draws the editor's underline/strikethrough, above the editor
        InitChrome();                  // popup layer for filter/validation dropdowns
        InitSignatureHelp();           // function argument hint while typing a formula
        ApplyTheme();                  // canvas/text colors follow the system light/dark theme
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        Selection.Changed += (_, _) => Redraw();
        _editor.KeyDown += EditorKeyDown;
        // Arrow keys must be caught in the tunnel phase: the TextBox's own (bubbling) handler would
        // otherwise consume them for caret movement before point mode could poke at a cell.
        _editor.AddHandler(KeyDownEvent, EditorPreviewKeyDown, RoutingStrategies.Tunnel);
        // Commit on blur, but don't pull focus back to the grid: the editor is losing focus precisely
        // because it's moving elsewhere (e.g. the user clicked the formula bar). Re-grabbing it here is
        // a reentrant focus change *during* that transfer, which leaves Avalonia's focus manager pointing
        // at the grid while the new control still shows as focused — so a later click can't dislodge it
        // and the formula bar keeps its focus state. Let the focus go where it was headed.
        _editor.LostFocus += (_, _) => CommitEdit(moveRow: 0, moveColumn: 0, refocus: false);
        _editor.TextChanged += (_, _) =>
        {
            // Compare by value (not a bool flag) so a TextChanged that arrives after SetEditorText
            // returns still counts as our own edit, not typing. Two kinds of "programmatic" change
            // are distinguished: a grid self-edit (point mode, autocomplete, F4) still drives the
            // formula bar, while an echo coming FROM the bar must not bounce back.
            bool programmatic = _editor.Text == _programmaticText;
            bool barEcho = programmatic && _barEcho;
            _programmaticText = null;
            _barEcho = false;
            if (!programmatic)
            {
                ResetPointMode();  // typing ends ref replacement (#17)
                OfferCompletion(); // type-ahead from the column (#20)
            }

            Redraw(); // keep live reference highlights in step with the formula (#18)
            UpdateSignatureHelp(); // refresh the argument hint as the formula changes

            // Task-9 hook: notify external listeners (the formula bar) of edits so they can mirror
            // the inline editor live. User typing AND grid self-edits (point-mode references,
            // autocomplete, F4) update the bar; only the echo coming FROM the bar (SetInlineEditorText)
            // is suppressed, to avoid an echo loop.
            if (!barEcho)
            {
                InlineEditTextChanged?.Invoke(_editor.Text ?? string.Empty);
            }
        };
        // The argument hint also tracks caret movement (arrows / clicks within the formula).
        _editor.GetObservable(TextBox.CaretIndexProperty).Subscribe(new AnonymousObserver<int>(_ => UpdateSignatureHelp()));
    }

    private void Redraw()
    {
        _surface.InvalidateVisual();
        _editorDecorations.InvalidateVisual();
    }

    /// <summary>Re-resolve the theme palette and repaint; the inline editor matches the canvas so editing reads in either theme.</summary>
    private void ApplyTheme()
    {
        _palette = GridPalette.For(ActualThemeVariant);
        Background = _palette.Canvas;
        _editor.Background = _palette.Canvas;
        _editor.Foreground = _palette.DefaultText;
        Redraw();
    }

    /// <summary>The draw-only child. Panel.Render is sealed, so drawing lives on this Control.</summary>
    private sealed class Surface : Control
    {
        private readonly SpreadsheetGrid _owner;

        public Surface(SpreadsheetGrid owner)
        {
            _owner = owner;
            IsHitTestVisible = false; // pointer events route to the owning panel
        }

        public override void Render(DrawingContext context) => _owner.DrawTo(context);
    }

    /// <summary>
    /// A transparent, click-through layer that floats directly above the inline editor and draws the
    /// editor's underline/strikethrough — the one thing a plain <see cref="TextBox"/> can't show. It
    /// measures the live text with the cell's typeface (matching the editor's font), so the lines sit
    /// exactly under/through the glyphs the TextBox renders.
    /// </summary>
    private sealed class EditorDecorationLayer : Control
    {
        private readonly SpreadsheetGrid _owner;

        public EditorDecorationLayer(SpreadsheetGrid owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
            ClipToBounds = true;
        }

        public override void Render(DrawingContext context) => _owner.DrawEditorDecorations(context);
    }

    public GridSelection Selection { get; } = new();

    public event EventHandler? SelectionChanged;

    /// <summary>
    /// The active cell's address as text for the Name Box — the full merged range (e.g. <c>A1:B2</c>)
    /// when the active cell sits inside a merge, otherwise the single address (e.g. <c>A1</c>). The
    /// host's formula bar should display this rather than building the address from the raw
    /// active row/column, so a selected merge reads as its whole span.
    /// </summary>
    public string ActiveCellName
    {
        get
        {
            if (Sheet?.FindMerge(Selection.ActiveRow, Selection.ActiveColumn) is { } merge)
            {
                return merge.Range.ToString();
            }

            return new CellAddress(Selection.ActiveColumn, Selection.ActiveRow).ToString();
        }
    }

    /// <summary>
    /// When true (a modal overlay is open above the grid), the grid ignores keyboard and text input so
    /// navigation, typing and editing don't leak to the hidden grid behind the modal. The event is left
    /// unhandled so it still reaches the overlay. Set by the window when it shows/hides an overlay.
    /// </summary>
    public bool InputBlocked { get; set; }

    // The modifier chord from the most recent key press; consulted in OnTextInput so a Cmd/Ctrl combo
    // never types into a cell. KeyDown always precedes the text input it produces, so this stays current.
    private KeyModifiers _lastKeyModifiers;

    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, Cellestra.Core.Defaults.MinZoom, Cellestra.Core.Defaults.MaxZoom);
            Redraw();
        }
    }

    public void Attach(WorkbookSession session)
    {
        _session = session;
        _scrollRow = 0;
        _scrollColumn = 0;
        Selection.MoveTo(0, 0);
        WireFormatPainter(session); // paintbrush cursor + drag-to-paint follow the session's armed stamp
        Redraw();
    }

    public void Refresh()
    {
        // A formatting command (bold, colour, alignment, number format…) can run while a cell is being
        // edited — the toolbar buttons don't steal focus. Re-skin the live editor so the text you're
        // typing immediately takes on the new style, matching how the cell will look once committed.
        if (_editing && _session is not null)
        {
            ApplyEditorFont(Sheet?.Find(Selection.ActiveRow, Selection.ActiveColumn));
        }

        Redraw();
    }

    private Worksheet? Sheet => _session?.ActiveSheet;

    private CultureInfo Culture => _session?.Workbook.Settings.EffectiveLocale ?? CultureInfo.CurrentCulture;

    // ---- Layout -----------------------------------------------------------

    private double _frozenRight = HeaderWidth;
    private double _frozenBottom = HeaderHeight;

    private void BuildVisible(Size bounds)
    {
        _visibleColumns.Clear();
        _visibleRows.Clear();
        var sheet = Sheet;
        if (sheet is null)
        {
            return;
        }

        RebuildRowFontMap(sheet); // so EffectiveRowHeight can grow rows to fit their tallest font

        int frozenColumns = sheet.FrozenColumns;
        int frozenRows = sheet.FrozenRows;

        // Frozen columns are always shown, pinned just right of the row header.
        double x = HeaderWidth;
        for (int col = 0; col < frozenColumns && x < bounds.Width; col++)
        {
            if (sheet.IsColumnHidden(col))
            {
                continue;
            }

            double w = EffectiveColumnWidth(sheet, col) * _zoom;
            _visibleColumns.Add((col, x, w));
            x += w;
        }

        _frozenRight = x;
        for (int col = Math.Max(_scrollColumn, frozenColumns); col < SheetDimensions.MaxColumns && x < bounds.Width; col++)
        {
            if (sheet.IsColumnHidden(col))
            {
                continue;
            }

            double w = EffectiveColumnWidth(sheet, col) * _zoom;
            _visibleColumns.Add((col, x, w));
            x += w;
        }

        double y = HeaderHeight;
        for (int row = 0; row < frozenRows && y < bounds.Height; row++)
        {
            if (sheet.IsRowHidden(row))
            {
                continue;
            }

            double h = EffectiveRowHeight(sheet, row) * _zoom;
            _visibleRows.Add((row, y, h));
            y += h;
        }

        _frozenBottom = y;
        for (int row = Math.Max(_scrollRow, frozenRows); row < SheetDimensions.MaxRows && y < bounds.Height; row++)
        {
            if (sheet.IsRowHidden(row))
            {
                continue;
            }

            double h = EffectiveRowHeight(sheet, row) * _zoom;
            _visibleRows.Add((row, y, h));
            y += h;
        }
    }

    // ---- Rendering --------------------------------------------------------

    private void DrawTo(DrawingContext context)
    {
        BuildVisible(Bounds.Size);
        var sheet = Sheet;
        if (sheet is null)
        {
            return;
        }

        // Re-resolve here too (not just on theme change) so all partials draw with one consistent palette.
        _palette = GridPalette.For(ActualThemeVariant);
        var gridPen = _palette.GridLine;

        context.FillRectangle(_palette.Canvas, new Rect(Bounds.Size));
        DrawTableBanding(context, sheet);
        DrawSelection(context, _palette.SelectionFill);
        DrawCells(context, sheet, gridPen);
        DrawMerges(context, sheet, gridPen);
        DrawCellBorders(context, sheet);
        DrawTableChrome(context, sheet);
        DrawFreezeDividers(context);
        DrawHeaders(context, _palette.HeaderFill, _palette.HeaderText, gridPen);
        DrawActiveOutline(context, _palette.ActiveOutline);
        DrawReferenceHighlights(context);
        DrawMarquee(context);
        DrawFillPreview(context);
        DrawFillHandle(context);
        DrawPaintPreview(context);
    }

    private void DrawCells(DrawingContext context, Worksheet sheet, IPen gridPen)
    {
        // Pass 1: backgrounds and gridlines for every visible cell.
        foreach (var (row, y, h) in _visibleRows)
        {
            foreach (var (col, x, w) in _visibleColumns)
            {
                if (sheet.FindMerge(row, col) is not null)
                {
                    continue; // merged regions are painted once, in DrawMerges
                }

                var rect = new Rect(x, y, w, h);
                var cell = sheet.Find(row, col);
                if (cell is not null)
                {
                    var style = ResolveStyle(cell, row, col);
                    if (style.Fill.HasBackground)
                    {
                        context.FillRectangle(new SolidColorBrush(ToColor(style.Fill.Background!.Value)), rect);
                    }
                }

                context.DrawRectangle(null, gridPen, rect);
            }
        }

        // Pass 2: text, after all gridlines — so text spilling into empty neighbors isn't clipped.
        foreach (var (row, y, h) in _visibleRows)
        {
            foreach (var (col, x, w) in _visibleColumns)
            {
                if (sheet.FindMerge(row, col) is not null)
                {
                    continue;
                }

                var cell = sheet.Find(row, col);
                // A hyperlinked cell draws even when empty (its target stands in as the link text).
                if (cell is not null && (!cell.Value.IsEmpty || cell.Hyperlink is not null))
                {
                    DrawCellContent(context, sheet, cell, new Rect(x, y, w, h), row, col, allowSpill: true);
                }
            }
        }
    }

    private void DrawMerges(DrawingContext context, Worksheet sheet, IPen gridPen)
    {
        foreach (var region in sheet.MergedRegions)
        {
            if (!TryGetRegionRect(region.Range.Top, region.Range.Left, region.Range.Bottom, region.Range.Right, out var rect))
            {
                continue;
            }

            // Paint the anchor cell's fill across the whole merge (mirroring DrawCells, including any
            // live style-preview); fall back to the blank canvas when it has no background (task 7).
            var anchor = sheet.Find(region.Range.Top, region.Range.Left);
            if (anchor is not null && ResolveStyle(anchor, region.Range.Top, region.Range.Left).Fill is { HasBackground: true } fill)
            {
                context.FillRectangle(new SolidColorBrush(ToColor(fill.Background!.Value)), rect);
            }
            else
            {
                context.FillRectangle(_palette.Canvas, rect);
            }

            context.DrawRectangle(null, gridPen, rect);

            if (anchor is not null && !anchor.Value.IsEmpty)
            {
                DrawCellContent(context, sheet, anchor, rect, region.Range.Top, region.Range.Left, allowSpill: false);
            }
        }
    }

    private void DrawFreezeDividers(DrawingContext context)
    {
        var sheet = Sheet;
        if (sheet is null)
        {
            return;
        }

        var pen = _palette.FreezeDivider;
        if (sheet.FrozenColumns > 0 && _frozenRight > HeaderWidth)
        {
            context.DrawLine(pen, new Point(_frozenRight, 0), new Point(_frozenRight, Bounds.Height));
        }

        if (sheet.FrozenRows > 0 && _frozenBottom > HeaderHeight)
        {
            context.DrawLine(pen, new Point(0, _frozenBottom), new Point(Bounds.Width, _frozenBottom));
        }
    }

    private bool TryGetRegionRect(int top, int left, int bottom, int right, out Rect rect)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var (col, x, w) in _visibleColumns)
        {
            if (col >= left && col <= right)
            {
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x + w);
            }
        }

        foreach (var (row, y, h) in _visibleRows)
        {
            if (row >= top && row <= bottom)
            {
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y + h);
            }
        }

        if (minX > maxX || minY > maxY)
        {
            rect = default;
            return false;
        }

        rect = new Rect(minX, minY, maxX - minX, maxY - minY);
        return true;
    }

    private void DrawCellContent(DrawingContext context, Worksheet sheet, Cell cell, Rect rect, int row, int column, bool allowSpill)
    {
        var style = ResolveStyle(cell, row, column);
        bool isLink = cell.Hyperlink is not null;
        string text = NumberFormatter.Format(cell.Value, style.NumberFormat, Culture);
        // An empty hyperlinked cell shows its target as the visible (clickable) link text.
        if (text.Length == 0 && isLink)
        {
            text = cell.Hyperlink!.Target;
        }

        if (text.Length == 0)
        {
            return;
        }

        var typeface = new Typeface(
            style.Font.Family,
            style.Font.Italic ? FontStyle.Italic : FontStyle.Normal,
            style.Font.Bold ? FontWeight.Bold : FontWeight.Normal);
        double fontSize = style.Font.SizePoints * 96.0 / 72.0 * _zoom;
        // A hyperlink draws in the themed link colour and is always underlined, regardless of the
        // cell's own font colour/decorations, so links read as links in both light and dark mode.
        var brush = isLink ? _palette.Hyperlink : TextBrushFor(style, cell.Value);

        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, fontSize, brush);
        if (LinkDecorations(style.Font, isLink) is { } decorations)
        {
            formatted.SetTextDecorations(decorations);
        }

        double cellAvailable = rect.Width - 2 * CellPadX;
        bool numeric = cell.Value.IsNumber || cell.Value.IsBoolean || cell.Value.IsError;

        // A number (or date) too wide for its column shows ##### rather than being trimmed — widen
        // the column to see it. Errors keep their token.
        if (numeric && !cell.Value.IsError && formatted.Width > cellAvailable)
        {
            DrawHashes(context, rect, typeface, fontSize, brush, cellAvailable);
            return;
        }

        var horizontal = style.Alignment.Horizontal switch
        {
            SAlign.General => numeric ? SAlign.Right : SAlign.Left,
            var h => h,
        };

        // Wrap Text: the text flows onto multiple lines within the column width (never spilling into
        // neighbours, never trimmed); the row grows to fit via EffectiveRowHeight (task 6). Numbers
        // ignore wrap (they ##### instead, handled above).
        bool wrap = style.Alignment.WrapText && !numeric;
        if (wrap)
        {
            formatted.MaxTextWidth = Math.Max(1, cellAvailable);
            formatted.Trimming = TextTrimming.None;
        }
        else
        {
            // Left-aligned text overflows into empty neighbours and only clips when one is occupied.
            double available = cellAvailable;
            if (allowSpill && !numeric && horizontal == SAlign.Left && formatted.Width > cellAvailable)
            {
                available += SpillRoom(sheet, row, column);
            }

            formatted.MaxTextWidth = Math.Max(1, available);
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        double tx = horizontal switch
        {
            SAlign.Right => rect.Right - CellPadX - formatted.Width,
            SAlign.Center => rect.X + (rect.Width - formatted.Width) / 2,
            _ => rect.X + CellPadX,
        };
        double ty = style.Alignment.Vertical switch
        {
            Cellestra.Core.Styling.VerticalAlignment.Top => rect.Y + CellPadY,
            Cellestra.Core.Styling.VerticalAlignment.Middle => rect.Y + (rect.Height - formatted.Height) / 2,
            _ => rect.Bottom - formatted.Height - CellPadY, // Bottom (the default)
        };

        context.DrawText(formatted, new Point(tx, Math.Max(rect.Y, ty)));
    }

    /// <summary>Extra width available to the right of a cell while the next columns are empty.</summary>
    private double SpillRoom(Worksheet sheet, int row, int column)
    {
        double extra = 0;
        for (int i = 0; i < _visibleColumns.Count; i++)
        {
            if (_visibleColumns[i].Index != column)
            {
                continue;
            }

            for (int j = i + 1; j < _visibleColumns.Count; j++)
            {
                int next = _visibleColumns[j].Index;
                var neighbour = sheet.Find(row, next);
                if ((neighbour is not null && !neighbour.Value.IsEmpty) || sheet.FindMerge(row, next) is not null)
                {
                    break; // an occupied (or merged) neighbour clips the spill
                }

                extra += _visibleColumns[j].Size;
            }

            break;
        }

        return extra;
    }

    private void DrawHashes(DrawingContext context, Rect rect, Typeface typeface, double fontSize, IBrush brush, double available)
    {
        var one = new FormattedText("#", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, brush);
        int count = Math.Max(1, (int)(available / Math.Max(1, one.Width)));
        var hashes = new FormattedText(new string('#', count), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, brush);
        double tx = rect.Right - CellPadX - hashes.Width; // numbers right-align
        double ty = rect.Bottom - hashes.Height - CellPadY;
        context.DrawText(hashes, new Point(tx, Math.Max(rect.Y, ty)));
    }

    /// <summary>Test seam: the color a cell's text would be drawn in, for the current theme.</summary>
    internal Color ResolvedTextColor(CellStyle style, CellValue value) => ((ISolidColorBrush)TextBrushFor(style, value)).Color;

    private IBrush TextBrushFor(CellStyle style, CellValue value)
    {
        if (value.IsError)
        {
            return Brushes.Crimson;
        }

        // The default/"automatic" font color — transparent, or the default black — follows the theme
        // so text keeps its contrast in dark mode. An explicitly chosen non-black color is document
        // data and is drawn as-is.
        bool automatic = style.Font.Color.IsTransparent || style.Font.Color == ArgbColor.Black;
        return automatic ? _palette.DefaultText : new SolidColorBrush(ToColor(style.Font.Color));
    }

    private void DrawSelection(DrawingContext context, IBrush fill)
    {
        // Fill every selected rectangle (one, or several for a Cmd+click discontiguous selection),
        // each clipped to the visible viewport.
        foreach (var range in Selection.Ranges)
        {
            if (TryGetRegionRect(range.Top, range.Left, range.Bottom, range.Right, out var rect))
            {
                context.FillRectangle(fill, rect);
            }
        }
    }

    private void DrawActiveOutline(DrawingContext context, IPen pen)
    {
        // A merged active cell is outlined around its whole region, not just the anchor.
        if (Sheet?.FindMerge(Selection.ActiveRow, Selection.ActiveColumn) is { } merge)
        {
            if (TryGetRegionRect(merge.Range.Top, merge.Range.Left, merge.Range.Bottom, merge.Range.Right, out var mergeRect))
            {
                context.DrawRectangle(null, pen, mergeRect);
            }

            return;
        }

        if (TryGetCellRect(Selection.ActiveRow, Selection.ActiveColumn, out var rect))
        {
            context.DrawRectangle(null, pen, rect);
        }
    }

    private void DrawHeaders(DrawingContext context, IBrush headerBrush, IBrush textBrush, IPen gridPen)
    {
        // Corner.
        context.FillRectangle(headerBrush, new Rect(0, 0, HeaderWidth, HeaderHeight));

        var typeface = new Typeface(Cellestra.Core.Defaults.FontFamily);
        // Header labels scale with the zoom level, like the cell text, but stay legible at small zoom.
        double headerFontSize = Math.Clamp(11 * _zoom, 8, HeaderHeight - 4);
        foreach (var (col, x, w) in _visibleColumns)
        {
            var rect = new Rect(x, 0, w, HeaderHeight);
            bool active = col >= Selection.Left && col <= Selection.Right;
            context.FillRectangle(active ? _palette.ActiveHeaderFill : headerBrush, rect);
            context.DrawRectangle(null, gridPen, rect);
            DrawCentered(context, SheetDimensions.ColumnToLetters(col), rect, typeface, textBrush, headerFontSize);
        }

        foreach (var (row, y, h) in _visibleRows)
        {
            var rect = new Rect(0, y, HeaderWidth, h);
            bool active = row >= Selection.Top && row <= Selection.Bottom;
            context.FillRectangle(active ? _palette.ActiveHeaderFill : headerBrush, rect);
            context.DrawRectangle(null, gridPen, rect);
            DrawCentered(context, (row + 1).ToString(CultureInfo.InvariantCulture), rect, typeface, textBrush, headerFontSize);
        }
    }

    private static void DrawCentered(DrawingContext context, string text, Rect rect, Typeface typeface, IBrush brush, double fontSize)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, brush);
        context.DrawText(formatted, new Point(
            rect.X + (rect.Width - formatted.Width) / 2,
            rect.Y + (rect.Height - formatted.Height) / 2));
    }

    private bool TryGetCellRect(int row, int column, out Rect rect)
    {
        double? x = null, y = null, w = null, h = null;
        foreach (var (c, cx, cw) in _visibleColumns)
        {
            if (c == column)
            {
                x = cx;
                w = cw;
                break;
            }
        }

        foreach (var (r, ry, rh) in _visibleRows)
        {
            if (r == row)
            {
                y = ry;
                h = rh;
                break;
            }
        }

        if (x is null || y is null)
        {
            rect = default;
            return false;
        }

        rect = new Rect(x.Value, y.Value, w!.Value, h!.Value);
        return true;
    }

    /// <summary>
    /// The on-screen rectangle the inline editor should occupy for the active cell: the combined
    /// merged region when the active cell is merged, otherwise just the single cell.
    /// </summary>
    private bool TryGetActiveCellRect(out Rect rect)
    {
        if (Sheet?.FindMerge(Selection.ActiveRow, Selection.ActiveColumn) is { } merge)
        {
            return TryGetRegionRect(merge.Range.Top, merge.Range.Left, merge.Range.Bottom, merge.Range.Right, out rect);
        }

        return TryGetCellRect(Selection.ActiveRow, Selection.ActiveColumn, out rect);
    }

    // ---- Hit testing & input ---------------------------------------------

    private bool HitTest(Point point, out int row, out int column, out bool rowHeader, out bool columnHeader)
    {
        row = column = -1;
        rowHeader = point.X < HeaderWidth;
        columnHeader = point.Y < HeaderHeight;

        foreach (var (c, x, w) in _visibleColumns)
        {
            if (point.X >= x && point.X < x + w)
            {
                column = c;
                break;
            }
        }

        foreach (var (r, y, h) in _visibleRows)
        {
            if (point.Y >= y && point.Y < y + h)
            {
                row = r;
                break;
            }
        }

        if (column < 0 && _visibleColumns.Count > 0)
        {
            column = _visibleColumns[^1].Index;
        }

        if (row < 0 && _visibleRows.Count > 0)
        {
            row = _visibleRows[^1].Index;
        }

        return row >= 0 && column >= 0;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetPosition(this);

        // Right-click is for the context menu (raised separately as ContextRequested); don't let it
        // start a selection. Commit any in-progress edit first so the menu acts on a settled cell,
        // and focus the grid so typing works once the menu closes.
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (_editing)
            {
                CommitEdit(0, 0);
            }

            Focus();
            return;
        }

        // Point mode: clicking a cell while typing a formula inserts its reference instead of
        // committing (#17). Checked before stealing focus so the editor stays active.
        if (_editing && TryPointModeInsert(point))
        {
            e.Handled = true;
            return;
        }

        Focus();
        if (_editing)
        {
            CommitEdit(0, 0);
        }

        _tabOriginColumn = null; // a click breaks any Tab-entry sequence (#19)

        // When the Format Painter is armed, a press starts a paint gesture (click one cell or drag a
        // range), taking precedence over normal selection — release paints the stamp and disarms.
        if (TryBeginPaint(e, point))
        {
            e.Handled = true;
            return;
        }

        if (TryHandleChromeClick(point))
        {
            e.Handled = true;
            return;
        }

        // Double-clicking a column header's right border autofits the column to its content (#23).
        if (e.ClickCount == 2 && TryHitColumnBorder(point, out int borderColumn))
        {
            AutofitColumn(borderColumn);
            e.Handled = true;
            return;
        }

        // Drag on a header border resizes: a column by its right border, a row by its bottom border.
        // (A double-click on the border was already handled as autofit above.)
        if (TryBeginResize(e, point))
        {
            e.Handled = true;
            return;
        }

        // The fill handle takes precedence over cell selection at the selection's bottom-right.
        if (TryGetFillHandleRect(out var handle) && handle.Contains(point))
        {
            if (e.ClickCount == 2)
            {
                FillDownToAdjacent();
            }
            else
            {
                TryBeginFill(point);
                e.Pointer.Capture(this);
            }

            e.Handled = true;
            return;
        }

        if (!HitTest(point, out int row, out int column, out bool rowHeader, out bool columnHeader))
        {
            return;
        }

        // Cmd-click a hyperlinked cell opens its link with the OS default handler, leaving the
        // selection untouched (so the link gesture never breaks the discontiguous-select gesture
        // for non-linked cells). Single click only; headers and double-clicks fall through.
        if (e.ClickCount == 1 && !rowHeader && !columnHeader
            && e.KeyModifiers.HasFlag(KeyModifiers.Meta) && TryActivateHyperlink(row, column))
        {
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            SelectCellOrMerge(row, column);
            BeginEdit(null);
            return;
        }

        bool extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Meta); // Cmd: add a disjoint range (#27/#28)
        if (rowHeader && !columnHeader)
        {
            if (extend)
            {
                Selection.ExtendRowBand(row); // Shift+click: select every row from the anchor to here (#3)
            }
            else
            {
                SelectRowOrColumn(additive, row, 0, row, SheetDimensions.MaxColumns - 1);
            }
        }
        else if (columnHeader && !rowHeader)
        {
            if (extend)
            {
                Selection.ExtendColumnBand(column);
            }
            else
            {
                SelectRowOrColumn(additive, 0, column, SheetDimensions.MaxRows - 1, column);
            }
        }
        else if (extend)
        {
            Selection.ExtendTo(row, column);
        }
        else
        {
            if (additive)
            {
                Selection.AddPoint(row, column);
            }
            else
            {
                SelectCellOrMerge(row, column);
            }

            _selecting = true;
            e.Pointer.Capture(this);
        }

        RaiseSelectionChanged();
    }

    /// <summary>What a right-click landed on, so the host can show the matching context menu.</summary>
    internal enum ContextTarget
    {
        None,
        Cell,
        RowHeader,
        ColumnHeader,
    }

    /// <summary>
    /// Prepare the selection for a context menu at <paramref name="point"/> and report what it hit.
    /// A cell/row/column outside the current selection becomes selected (a whole row/column for a
    /// header); a click inside the existing selection keeps it, so the menu acts on the whole range.
    /// </summary>
    internal ContextTarget SelectForContext(Point point)
    {
        if (!HitTest(point, out int row, out int column, out bool rowHeader, out bool columnHeader))
        {
            return ContextTarget.None;
        }

        if (rowHeader && columnHeader)
        {
            return ContextTarget.None; // the top-left corner
        }

        if (rowHeader)
        {
            // Keep the selection only if it's already whole rows covering this one (so right-clicking
            // one of several selected rows deletes them all); otherwise select just this full row.
            bool wholeRows = Selection.Left == 0 && Selection.Right == SheetDimensions.MaxColumns - 1;
            if (!(wholeRows && row >= Selection.Top && row <= Selection.Bottom))
            {
                Selection.SetRange(row, 0, row, SheetDimensions.MaxColumns - 1);
                RaiseSelectionChanged();
            }

            return ContextTarget.RowHeader;
        }

        if (columnHeader)
        {
            bool wholeColumns = Selection.Top == 0 && Selection.Bottom == SheetDimensions.MaxRows - 1;
            if (!(wholeColumns && column >= Selection.Left && column <= Selection.Right))
            {
                Selection.SetRange(0, column, SheetDimensions.MaxRows - 1, column);
                RaiseSelectionChanged();
            }

            return ContextTarget.ColumnHeader;
        }

        bool inSelection = row >= Selection.Top && row <= Selection.Bottom && column >= Selection.Left && column <= Selection.Right;
        if (!inSelection)
        {
            Selection.MoveTo(row, column);
            RaiseSelectionChanged();
        }

        return ContextTarget.Cell;
    }

    private void SelectRowOrColumn(bool additive, int top, int left, int bottom, int right)
    {
        if (additive)
        {
            Selection.AddRange(top, left, bottom, right);
        }
        else
        {
            Selection.SetRange(top, left, bottom, right);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);

        if (IsResizing)
        {
            UpdateResize(point);
            return;
        }

        if (_filling)
        {
            UpdateFill(point);
            return;
        }

        if (_painting)
        {
            UpdatePaint(point);
            return;
        }

        if (IsFormatPainterArmed)
        {
            UpdatePaintCursor(); // keep the paintbrush cursor; don't let resize-hover override it
            return;
        }

        if (!_selecting)
        {
            // Hovering a header border shows the resize cursor; elsewhere, the default arrow.
            UpdateResizeCursor(point);
            UpdateHoverTip(point); // error-cell reason (task 12) or "⌘-click to open …" for a link
            return;
        }

        if (HitTest(point, out int row, out int column, out _, out _))
        {
            Selection.ExtendTo(row, column);
            RaiseSelectionChanged();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (IsResizing)
        {
            EndResize();
            e.Pointer.Capture(null);
            return;
        }

        if (_filling)
        {
            CommitFill();
        }

        if (_painting)
        {
            CommitPaint(); // paint the stamp over the dragged (or single-cell) range, then disarm
        }

        _selecting = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearHoverTip(); // don't leave a stale tooltip when the pointer leaves the grid
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        bool horizontal = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        double delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        int steps = delta > 0 ? -3 : 3;

        if (horizontal)
        {
            _scrollColumn = Math.Clamp(_scrollColumn + steps, 0, SheetDimensions.MaxColumns - 1);
        }
        else
        {
            _scrollRow = Math.Clamp(_scrollRow + steps, 0, SheetDimensions.MaxRows - 1);
        }

        Redraw();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Remember the chord on the key that may generate the next OnTextInput, so a Cmd/Ctrl combo
        // (reserved for shortcuts) never begins a cell edit even when the OS still emits its character.
        _lastKeyModifiers = e.KeyModifiers;
        base.OnKeyDown(e);
        if (InputBlocked)
        {
            return; // a modal is open above the grid — let the key reach the overlay, not the grid
        }

        if (_editing)
        {
            return;
        }

        // Esc disarms the Format Painter (cancel without painting) before any other Esc behavior.
        if (e.Key == Key.Escape && TryCancelPaint())
        {
            e.Handled = true;
            return;
        }

        bool extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        // ⌘+Arrow (Ctrl+Arrow off macOS) jumps to the edge of the data block, Excel-style; with Shift
        // it extends the selection to that edge instead of moving.
        bool jump = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.Up: _tabOriginColumn = null; Step(-1, 0, extend, jump); break;
            case Key.Down: _tabOriginColumn = null; Step(1, 0, extend, jump); break;
            case Key.Left: _tabOriginColumn = null; Step(0, -1, extend, jump); break;
            case Key.Right: _tabOriginColumn = null; Step(0, 1, extend, jump); break;
            case Key.Tab: MoveStep(0, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1, false); break;
            case Key.Return:
                // Cmd+Return opens the active cell's hyperlink (mirroring the Cmd-click gesture);
                // plain Return keeps its established meaning — paste a marquee, else move down — so
                // hyperlink activation never steals Enter from navigation/commit.
                if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                    && TryActivateHyperlink(Selection.ActiveRow, Selection.ActiveColumn))
                {
                    break;
                }

                if (HasMarquee)
                {
                    EnterPasteRequested?.Invoke(this, EventArgs.Empty); // paste once at the active cell (#3)
                }
                else
                {
                    MoveStep(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1, 0, false);
                }

                break;
            case Key.F2: BeginEdit(null); break;
            case Key.Delete or Key.Back: ClearSelection(); break;
            case Key.Escape: InvalidateClipboard(); break;
            default:
                if (IsTypingKey(e))
                {
                    BeginEdit(string.Empty);
                    return;
                }

                return;
        }

        EnsureVisible(Selection.ActiveRow, Selection.ActiveColumn);
        e.Handled = true;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (InputBlocked)
        {
            return; // a modal is open above the grid — don't begin an edit behind it
        }

        // A Cmd/Ctrl chord is reserved for shortcuts; the character some platforms still emit for an
        // unbound combo (e.g. ⌘J) must not start a cell edit.
        if (_lastKeyModifiers.HasFlag(KeyModifiers.Meta) || _lastKeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (!_editing && !string.IsNullOrEmpty(e.Text) && e.Text[0] >= ' ')
        {
            BeginEdit(e.Text);
            e.Handled = true;
        }
    }

    private static bool IsTypingKey(KeyEventArgs e) =>
        !e.KeyModifiers.HasFlag(KeyModifiers.Meta) && !e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
        e.Key is (>= Key.A and <= Key.Z) or (>= Key.D0 and <= Key.D9);

    /// <summary>
    /// Move (or extend) the active cell by one step, skipping over any merged region: a step starts
    /// from the current merge's edge and lands on the cell past it, and entering a merge selects the
    /// whole span. Falls back to a plain one-cell step when there are no merges in the way.
    /// </summary>
    private void MoveStep(int rowDelta, int columnDelta, bool extend)
    {
        var sheet = Sheet;
        int row = Selection.ActiveRow;
        int column = Selection.ActiveColumn;
        var (targetRow, targetColumn) = sheet?.StepOverMerges(row, column, rowDelta, columnDelta)
            ?? (Math.Clamp(row + rowDelta, 0, SheetDimensions.MaxRows - 1),
                Math.Clamp(column + columnDelta, 0, SheetDimensions.MaxColumns - 1));
        Move(targetRow, targetColumn, extend);
    }

    /// <summary>One arrow press: a ⌘-jump to the data edge, or a single merge-aware step.</summary>
    private void Step(int rowDelta, int columnDelta, bool extend, bool jump)
    {
        if (jump)
        {
            JumpEdge(rowDelta, columnDelta, extend);
        }
        else
        {
            MoveStep(rowDelta, columnDelta, extend);
        }
    }

    /// <summary>⌘+Arrow: move (or, with Shift, extend) to the next data edge in a direction (#1).</summary>
    private void JumpEdge(int rowDelta, int columnDelta, bool extend)
    {
        var sheet = Sheet;
        if (sheet is null)
        {
            MoveStep(rowDelta, columnDelta, extend);
            return;
        }

        var (targetRow, targetColumn) = Cellestra.Core.Data.EdgeNavigator.FindEdge(
            sheet, Selection.ActiveRow, Selection.ActiveColumn, rowDelta, columnDelta);
        Move(targetRow, targetColumn, extend);
    }

    /// <summary>Test seam: drive the merge-aware arrow navigation directly (no key event plumbing).</summary>
    internal void NavigateForTest(int rowDelta, int columnDelta, bool extend) => MoveStep(rowDelta, columnDelta, extend);

    /// <summary>Test seam: drive ⌘+Arrow edge-jump navigation directly (no key event plumbing).</summary>
    internal void JumpEdgeForTest(int rowDelta, int columnDelta, bool extend) => JumpEdge(rowDelta, columnDelta, extend);

    /// <summary>Test seam: select a cell the way a click/arrow does, expanding onto a merge.</summary>
    internal void SelectForTest(int row, int column)
    {
        SelectCellOrMerge(row, column);
        RaiseSelectionChanged();
    }

    private void Move(int row, int column, bool extend)
    {
        if (extend)
        {
            Selection.ExtendTo(row, column);
        }
        else
        {
            SelectCellOrMerge(row, column);
        }

        RaiseSelectionChanged();
    }

    /// <summary>
    /// Select a single cell, or — when it falls inside a merged region — the whole merge (active cell
    /// at the merge's anchor). This keeps the active outline and Name Box reporting the full span.
    /// </summary>
    private void SelectCellOrMerge(int row, int column)
    {
        if (Sheet?.FindMerge(row, column) is { } merge)
        {
            // active = anchor (top-left); the range covers the whole merge.
            Selection.Restore(merge.Range.Top, merge.Range.Left, merge.Range.Bottom, merge.Range.Right,
                merge.Range.Top, merge.Range.Left);
        }
        else
        {
            Selection.MoveTo(row, column);
        }
    }

    private void ClearSelection()
    {
        if (_session is null)
        {
            return;
        }

        _session.Execute(Cellestra.Core.Editing.EditCommands.ClearContents(_session.ActiveSheet.Name, Selection.Range));
        Redraw();
        RaiseSelectionChanged();
    }

    /// <summary>Scroll the active cell into view (used after a programmatic move, e.g. a Find result).</summary>
    internal void ScrollToActive() => EnsureVisible(Selection.ActiveRow, Selection.ActiveColumn);

    private void EnsureVisible(int row, int column)
    {
        var sheet = Sheet;
        int frozenRows = sheet?.FrozenRows ?? 0;
        int frozenColumns = sheet?.FrozenColumns ?? 0;

        // Frozen lines are always on screen; only scroll for cells beyond them.
        if (row >= frozenRows)
        {
            if (row < _scrollRow)
            {
                _scrollRow = row;
            }
            else if (_visibleRows.Count > 0 && row > _visibleRows[^1].Index)
            {
                _scrollRow += row - _visibleRows[^1].Index;
            }
        }

        if (column >= frozenColumns)
        {
            if (column < _scrollColumn)
            {
                _scrollColumn = column;
            }
            else if (_visibleColumns.Count > 0 && column > _visibleColumns[^1].Index)
            {
                _scrollColumn += column - _visibleColumns[^1].Index;
            }
        }

        _scrollRow = Math.Max(_scrollRow, frozenRows);
        _scrollColumn = Math.Max(_scrollColumn, frozenColumns);
        Redraw();
    }

    /// <summary>Freeze rows above and columns left of the active cell (or unfreeze if already frozen).</summary>
    public void ToggleFreezePanes()
    {
        var sheet = Sheet;
        if (sheet is null)
        {
            return;
        }

        if (sheet.FrozenRows > 0 || sheet.FrozenColumns > 0)
        {
            sheet.FrozenRows = 0;
            sheet.FrozenColumns = 0;
        }
        else
        {
            sheet.FrozenRows = Selection.ActiveRow;
            sheet.FrozenColumns = Selection.ActiveColumn;
        }

        Redraw();
    }

    // ---- Inline editing ---------------------------------------------------

    /// <summary>Test seam: the inline editor's current font size and whether its text is top-aligned.</summary>
    internal double EditorFontSize => _editor.FontSize;

    internal bool EditorTextIsTopAligned => _editor.VerticalContentAlignment == Avalonia.Layout.VerticalAlignment.Top;

    /// <summary>Test seams: the live editor's weight and horizontal alignment, so a format command applied mid-edit can be verified.</summary>
    internal bool EditorIsBold => _editor.FontWeight == FontWeight.Bold;

    internal TextAlignment EditorTextAlignment => _editor.TextAlignment;

    /// <summary>Test seam: the rectangle the inline editor would occupy for the active cell (the full
    /// merged region when the active cell is merged), so a merged editor's span can be verified.</summary>
    internal Rect ActiveEditorRect => TryGetActiveCellRect(out var rect) ? rect : default;

    /// <summary>Match the inline editor to the cell's drawn appearance — font, weight/style, underline,
    /// text colour, and fill — so editing looks the same as the displayed cell (not default black-on-white).</summary>
    private void ApplyEditorFont(Cell? cell)
    {
        var style = _session!.Workbook.Styles.Get(cell?.StyleId ?? StylePool.DefaultId);
        _editor.FontFamily = new FontFamily(style.Font.Family);
        _editor.FontSize = style.Font.SizePoints * 96.0 / 72.0 * _zoom;
        _editor.FontWeight = style.Font.Bold ? FontWeight.Bold : FontWeight.Normal;
        _editor.FontStyle = style.Font.Italic ? FontStyle.Italic : FontStyle.Normal;

        var value = cell?.Value ?? CellValue.Empty;
        // Use the cell's text colour, but not the crimson error colour — you're typing a replacement.
        _editor.Foreground = value.IsError ? _palette.DefaultText : TextBrushFor(style, value);
        _editor.Background = style.Fill.HasBackground
            ? new SolidColorBrush(ToColor(style.Fill.Background!.Value))
            : _palette.Canvas;

        // Mirror the cell's horizontal alignment so an Align command applied mid-edit shifts the text;
        // the decoration overlay (underline/strikethrough) repaints alongside via Redraw().
        _editor.TextAlignment = style.Alignment.Horizontal switch
        {
            SAlign.Center => TextAlignment.Center,
            SAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
    }

    /// <summary>Draw the editor's underline/strikethrough on the overlay, measured against the live text so they line up with the glyphs.</summary>
    private void DrawEditorDecorations(DrawingContext context)
    {
        if (!_editing || _session is null)
        {
            return;
        }

        var cell = Sheet?.Find(Selection.ActiveRow, Selection.ActiveColumn);
        var style = _session.Workbook.Styles.Get(cell?.StyleId ?? StylePool.DefaultId);
        if (Decorations(style.Font) is null)
        {
            return; // nothing to draw — neither underline nor strikethrough
        }

        string text = _editor.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        var typeface = new Typeface(
            style.Font.Family,
            style.Font.Italic ? FontStyle.Italic : FontStyle.Normal,
            style.Font.Bold ? FontWeight.Bold : FontWeight.Normal);
        double fontSize = style.Font.SizePoints * 96.0 / 72.0 * _zoom;
        var brush = _editor.Foreground ?? Brushes.Black;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, brush);

        // The overlay is arranged at the editor rect, so (0,0) is the cell's top-left. The editor's
        // content area is inset by EditorPadX on both sides (and EditorPadY at the top), and the
        // TextBox aligns text within it; matching that here puts our lines under the glyphs.
        double width = _editorDecorations.Bounds.Width;
        double inner = Math.Max(0, width - 2 * EditorPadX);
        double x0 = _editor.TextAlignment switch
        {
            TextAlignment.Right => Math.Max(EditorPadX, width - EditorPadX - formatted.Width),
            TextAlignment.Center => EditorPadX + (inner - formatted.Width) / 2,
            _ => EditorPadX,
        };
        double x1 = x0 + formatted.Width;
        double baseline = EditorPadY + formatted.Baseline;

        var pen = new Pen(brush, Math.Max(1, fontSize / 14.0));
        if (style.Font.Underline)
        {
            double y = baseline + Math.Max(1, fontSize * 0.08);
            context.DrawLine(pen, new Point(x0, y), new Point(x1, y));
        }

        if (style.Font.Strikethrough)
        {
            double y = baseline - (fontSize * 0.26);
            context.DrawLine(pen, new Point(x0, y), new Point(x1, y));
        }
    }

    public void BeginEdit(string? seed) => BeginEdit(seed, focus: true);

    /// <param name="focus">
    /// When false the inline editor is shown and seeded but keeps focus where it is. The formula bar
    /// uses this to mirror the cell while the user keeps typing in the bar (task 9) — focusing the
    /// editor would steal the caret and trigger its LostFocus commit.
    /// </param>
    public void BeginEdit(string? seed, bool focus)
    {
        var sheet = Sheet;
        if (sheet is null)
        {
            return;
        }

        InvalidateClipboard(); // starting an edit drops any pending cut/copy marquee (#5)
        ResetPointMode();
        _editing = true;
        var cell = sheet.Find(Selection.ActiveRow, Selection.ActiveColumn);
        ApplyEditorFont(cell); // match the cell's drawn font so the text doesn't change size on edit
        _editor.Text = seed ?? CellEditText(cell);
        _editor.IsVisible = true;
        InvalidateArrange();
        if (focus)
        {
            _editor.Focus();
        }

        _editor.CaretIndex = _editor.Text?.Length ?? 0;
        ResetAutocomplete(); // don't treat the seeded text as a typed character (#20)
        UpdateSignatureHelp(); // a seeded formula shows its hint immediately
    }

    private static string CellEditText(Cell? cell)
    {
        if (cell is null)
        {
            return string.Empty;
        }

        return cell.IsFormula ? "=" + cell.Formula : cell.Value.ToInvariantString();
    }

    private void EditorPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right
            && TryPointModeArrow(e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            e.Handled = true;
        }
    }

    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Return:
                CommitEdit(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1, 0);
                e.Handled = true;
                break;
            case Key.Tab:
                CommitEdit(0, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                e.Handled = true;
                break;
            case Key.Escape:
                CancelEdit();
                e.Handled = true;
                break;
            case Key.F4:
                CycleReferenceInEditor();
                e.Handled = true;
                break;
        }
    }

    /// <summary>F4 while editing a formula cycles the reference at the caret: A1 → $A$1 → A$1 → $A1.</summary>
    private void CycleReferenceInEditor()
    {
        var (text, caret) = ReferenceCycler.Cycle(_editor.Text ?? string.Empty, _editor.CaretIndex);
        _editor.Text = text;
        _editor.CaretIndex = caret;
    }

    /// <param name="refocus">
    /// When true (keyboard commits via Enter/Tab/Escape, or a click that lands on the grid) focus
    /// returns to the grid so arrow keys keep working. The blur-driven commit passes false: focus is
    /// already on its way to another control and stealing it back corrupts the transfer.
    /// </param>
    private void CommitEdit(int moveRow, int moveColumn, bool refocus = true)
    {
        if (!_editing || _session is null)
        {
            return;
        }

        string text = _editor.Text ?? string.Empty;
        int row = Selection.ActiveRow;
        int column = Selection.ActiveColumn;

        // Enforce data validation before committing (PRD §8.13). A Stop rule rejects the edit.
        if (!PassesValidation(row, column, text, out string? reason))
        {
            ShowValidationError(reason);
            _editor.Focus();
            return; // keep editing so the user can correct it
        }

        _editing = false;
        _editor.IsVisible = false;
        HideSignatureHelp();

        _session.Execute(Cellestra.Core.Editing.EditCommands.SetInput(
            _session.Workbook, _session.ActiveSheet.Name, row, column, text, Culture));

        ExpandTableIfAdjacent(row, column, text);

        int targetColumn = column + moveColumn;
        if (moveColumn != 0 && moveRow == 0)
        {
            // Tabbing across a row: remember where the sequence began.
            _tabOriginColumn ??= column;
        }
        else if (moveRow != 0 && moveColumn == 0 && _tabOriginColumn is { } origin)
        {
            // Enter after tabbing returns to the origin column on the next row (#19).
            targetColumn = origin;
            _tabOriginColumn = null;
        }
        else
        {
            _tabOriginColumn = null;
        }

        if (moveRow != 0 || moveColumn != 0)
        {
            Selection.MoveTo(row + moveRow, targetColumn);
            EnsureVisible(Selection.ActiveRow, Selection.ActiveColumn);
        }

        if (refocus)
        {
            Focus();
        }

        Redraw();
        RaiseSelectionChanged();
    }

    /// <summary>
    /// After an edit commits, grow any table the edit sits just below or to the right of (PRD §8.10
    /// table auto-expand). A non-empty edit adjacent to exactly one table absorbs the new row/column;
    /// the Core command is a no-op (returns null) otherwise. Skipped when the cell was cleared.
    /// </summary>
    private void ExpandTableIfAdjacent(int row, int column, string text)
    {
        if (_session is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        var sheet = _session.ActiveSheet;
        // Snapshot the table list: executing the expand replaces entries in sheet.Tables.
        foreach (var table in sheet.Tables.ToArray())
        {
            if (Cellestra.Core.Editing.DataEditCommands.ExpandTableForEdit(
                    _session.Workbook, sheet.Name, table, row, column) is { } expand)
            {
                _session.Execute(expand);
                break; // tables can't overlap, so at most one absorbs the edit
            }
        }
    }

    private void CancelEdit()
    {
        _editing = false;
        _editor.IsVisible = false;
        HideSignatureHelp();
        Focus();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _surface.Arrange(new Rect(finalSize));
        _popupLayer.Arrange(new Rect(finalSize));
        BuildVisible(finalSize);
        if (_editing && TryGetActiveCellRect(out var rect))
        {
            _editor.Arrange(rect);
            _editorDecorations.Arrange(rect); // the decoration overlay tracks the editor exactly
            ArrangeSignatureTip(rect, finalSize);
        }
        else
        {
            _editor.Arrange(new Rect(0, 0, 0, 0));
            _editorDecorations.Arrange(new Rect(0, 0, 0, 0));
            _signatureTip.Arrange(new Rect(0, 0, 0, 0));
        }

        return finalSize;
    }

    /// <summary>Place the argument hint just above the cell editor, dropping below it when there's no room.</summary>
    private void ArrangeSignatureTip(Rect editor, Size finalSize)
    {
        if (!_signatureTip.IsVisible)
        {
            _signatureTip.Arrange(new Rect(0, 0, 0, 0));
            return;
        }

        _signatureTip.Measure(finalSize);
        var size = _signatureTip.DesiredSize;
        double x = Math.Max(0, Math.Min(editor.X, finalSize.Width - size.Width));
        double y = editor.Y - size.Height - 2;
        if (y < HeaderHeight)
        {
            y = editor.Bottom + 2; // no room above — show below the cell
        }

        _signatureTip.Arrange(new Rect(x, y, size.Width, size.Height));
    }

    private SpreadsheetGridAutomationPeer? _automationPeer;

    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        _automationPeer = new SpreadsheetGridAutomationPeer(this);

    /// <summary>The active cell's address and displayed value, for the accessibility peer (#C).</summary>
    public string ActiveCellDescription
    {
        get
        {
            string address = ActiveCellName;
            var cell = Sheet?.Find(Selection.ActiveRow, Selection.ActiveColumn);
            if (cell is null || cell.Value.IsEmpty)
            {
                return $"{address}: empty";
            }

            var style = ResolveStyle(cell, Selection.ActiveRow, Selection.ActiveColumn);
            return $"{address}: {NumberFormatter.Format(cell.Value, style.NumberFormat, Culture)}";
        }
    }

    private void RaiseSelectionChanged()
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        _automationPeer?.AnnounceActiveCell(ActiveCellDescription); // read the new active cell to a screen reader
    }

    private static Color ToColor(ArgbColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>
    /// The decorations to draw on a cell: a hyperlink is always underlined (adding the font's own
    /// strikethrough if set); a plain cell uses just its font's underline/strikethrough.
    /// </summary>
    private static TextDecorationCollection? LinkDecorations(Cellestra.Core.Styling.Font font, bool isLink)
    {
        if (!isLink)
        {
            return Decorations(font);
        }

        var decorations = new TextDecorationCollection { TextDecorations.Underline[0] };
        if (font.Strikethrough)
        {
            decorations.Add(TextDecorations.Strikethrough[0]);
        }

        return decorations;
    }

    /// <summary>The text decorations for a font (underline and/or strikethrough), or null when it has neither.</summary>
    private static TextDecorationCollection? Decorations(Cellestra.Core.Styling.Font font)
    {
        if (!font.Underline && !font.Strikethrough)
        {
            return null;
        }

        var decorations = new TextDecorationCollection();
        if (font.Underline)
        {
            decorations.Add(TextDecorations.Underline[0]);
        }

        if (font.Strikethrough)
        {
            decorations.Add(TextDecorations.Strikethrough[0]);
        }

        return decorations;
    }
}
