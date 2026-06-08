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

    private readonly TextBox _editor = new()
    {
        IsVisible = false,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(3, 1, 0, 0),
        Background = Brushes.White,
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top, // sit at the top of the cell, like the drawn text
    };

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
        Children.Add(_surface);        // draws the grid
        Children.Add(_editor);         // floats above for inline editing
        InitChrome();                  // popup layer for filter/validation dropdowns
        InitSignatureHelp();           // function argument hint while typing a formula
        ApplyTheme();                  // canvas/text colors follow the system light/dark theme
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        Selection.Changed += (_, _) => Redraw();
        _editor.KeyDown += EditorKeyDown;
        // Arrow keys must be caught in the tunnel phase: the TextBox's own (bubbling) handler would
        // otherwise consume them for caret movement before point mode could poke at a cell.
        _editor.AddHandler(KeyDownEvent, EditorPreviewKeyDown, RoutingStrategies.Tunnel);
        _editor.LostFocus += (_, _) => CommitEdit(moveRow: 0, moveColumn: 0);
        _editor.TextChanged += (_, _) =>
        {
            // Compare by value (not a bool flag) so a TextChanged that arrives after SetEditorText
            // returns still counts as our own edit, not typing.
            bool programmatic = _editor.Text == _programmaticText;
            _programmaticText = null;
            if (!programmatic)
            {
                ResetPointMode();  // typing ends ref replacement (#17)
                OfferCompletion(); // type-ahead from the column (#20)
            }

            Redraw(); // keep live reference highlights in step with the formula (#18)
            UpdateSignatureHelp(); // refresh the argument hint as the formula changes
        };
        // The argument hint also tracks caret movement (arrows / clicks within the formula).
        _editor.GetObservable(TextBox.CaretIndexProperty).Subscribe(new AnonymousObserver<int>(_ => UpdateSignatureHelp()));
    }

    private void Redraw() => _surface.InvalidateVisual();

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

    public GridSelection Selection { get; } = new();

    public event EventHandler? SelectionChanged;

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
        Redraw();
    }

    public void Refresh() => Redraw();

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
        DrawTableChrome(context, sheet);
        DrawFreezeDividers(context);
        DrawHeaders(context, _palette.HeaderFill, _palette.HeaderText, gridPen);
        DrawActiveOutline(context, _palette.ActiveOutline);
        DrawReferenceHighlights(context);
        DrawMarquee(context);
        DrawFillPreview(context);
        DrawFillHandle(context);
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
                if (cell is not null && !cell.Value.IsEmpty)
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

            context.FillRectangle(_palette.Canvas, rect);
            context.DrawRectangle(null, gridPen, rect);

            var anchor = sheet.Find(region.Range.Top, region.Range.Left);
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
        string text = NumberFormatter.Format(cell.Value, style.NumberFormat, Culture);
        if (text.Length == 0)
        {
            return;
        }

        var typeface = new Typeface(
            style.Font.Family,
            style.Font.Italic ? FontStyle.Italic : FontStyle.Normal,
            style.Font.Bold ? FontWeight.Bold : FontWeight.Normal);
        double fontSize = style.Font.SizePoints * 96.0 / 72.0 * _zoom;
        var brush = TextBrushFor(style, cell.Value);

        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, fontSize, brush);
        if (style.Font.Underline)
        {
            formatted.SetTextDecorations(TextDecorations.Underline);
        }

        double cellAvailable = rect.Width - 6;
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

        // Left-aligned text overflows into empty neighbours and only clips when one is occupied.
        double available = cellAvailable;
        if (allowSpill && !numeric && horizontal == SAlign.Left && formatted.Width > cellAvailable)
        {
            available += SpillRoom(sheet, row, column);
        }

        formatted.MaxTextWidth = Math.Max(1, available);
        formatted.Trimming = TextTrimming.CharacterEllipsis;

        double tx = horizontal switch
        {
            SAlign.Right => rect.Right - 3 - formatted.Width,
            SAlign.Center => rect.X + (rect.Width - formatted.Width) / 2,
            _ => rect.X + 3,
        };
        double ty = rect.Bottom - formatted.Height - 2;

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
        double tx = rect.Right - 3 - hashes.Width; // numbers right-align
        double ty = rect.Bottom - hashes.Height - 2;
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

        if (e.ClickCount == 2)
        {
            Selection.MoveTo(row, column);
            BeginEdit(null);
            return;
        }

        bool extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Meta); // Cmd: add a disjoint range (#27/#28)
        if (rowHeader && !columnHeader)
        {
            SelectRowOrColumn(additive, row, 0, row, SheetDimensions.MaxColumns - 1);
        }
        else if (columnHeader && !rowHeader)
        {
            SelectRowOrColumn(additive, 0, column, SheetDimensions.MaxRows - 1, column);
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
                Selection.MoveTo(row, column);
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

        if (!_selecting)
        {
            // Hovering a header border shows the resize cursor; elsewhere, the default arrow.
            UpdateResizeCursor(point);
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

        _selecting = false;
        e.Pointer.Capture(null);
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
        base.OnKeyDown(e);
        if (_editing)
        {
            return;
        }

        bool extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        int row = Selection.ActiveRow;
        int column = Selection.ActiveColumn;

        switch (e.Key)
        {
            case Key.Up: _tabOriginColumn = null; Move(row - 1, column, extend); break;
            case Key.Down: _tabOriginColumn = null; Move(row + 1, column, extend); break;
            case Key.Left: _tabOriginColumn = null; Move(row, column - 1, extend); break;
            case Key.Right: _tabOriginColumn = null; Move(row, column + 1, extend); break;
            case Key.Tab: Move(row, column + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1), false); break;
            case Key.Return:
                if (HasMarquee)
                {
                    EnterPasteRequested?.Invoke(this, EventArgs.Empty); // paste once at the active cell (#3)
                }
                else
                {
                    Move(row + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1), column, false);
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
        if (!_editing && !string.IsNullOrEmpty(e.Text) && e.Text[0] >= ' ')
        {
            BeginEdit(e.Text);
            e.Handled = true;
        }
    }

    private static bool IsTypingKey(KeyEventArgs e) =>
        !e.KeyModifiers.HasFlag(KeyModifiers.Meta) && !e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
        e.Key is (>= Key.A and <= Key.Z) or (>= Key.D0 and <= Key.D9);

    private void Move(int row, int column, bool extend)
    {
        if (extend)
        {
            Selection.ExtendTo(row, column);
        }
        else
        {
            Selection.MoveTo(row, column);
        }

        RaiseSelectionChanged();
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
    }

    public void BeginEdit(string? seed)
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
        _editor.Focus();
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

    private void CommitEdit(int moveRow, int moveColumn)
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

        Focus();
        Redraw();
        RaiseSelectionChanged();
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
        if (_editing && TryGetCellRect(Selection.ActiveRow, Selection.ActiveColumn, out var rect))
        {
            _editor.Arrange(rect);
            ArrangeSignatureTip(rect, finalSize);
        }
        else
        {
            _editor.Arrange(new Rect(0, 0, 0, 0));
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

    private void RaiseSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);

    private static Color ToColor(ArgbColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);
}
