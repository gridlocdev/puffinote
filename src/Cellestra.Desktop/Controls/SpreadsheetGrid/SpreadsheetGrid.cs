using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
    };

    private readonly Surface _surface;
    private WorkbookSession? _session;
    private int _scrollRow;
    private int _scrollColumn;
    private double _zoom = 1.0;
    private bool _editing;
    private bool _selecting;

    private readonly List<(int Index, double Offset, double Size)> _visibleColumns = new();
    private readonly List<(int Index, double Offset, double Size)> _visibleRows = new();

    public SpreadsheetGrid()
    {
        Focusable = true;
        Background = Brushes.White;
        _surface = new Surface(this);
        Children.Add(_surface);        // draws the grid
        Children.Add(_editor);         // floats above for inline editing
        InitChrome();                  // popup layer for filter/validation dropdowns
        Selection.Changed += (_, _) => Redraw();
        _editor.KeyDown += EditorKeyDown;
        _editor.LostFocus += (_, _) => CommitEdit(moveRow: 0, moveColumn: 0);
    }

    private void Redraw() => _surface.InvalidateVisual();

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

            double w = sheet.GetColumnWidth(col) * _zoom;
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

            double w = sheet.GetColumnWidth(col) * _zoom;
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

            double h = sheet.GetRowHeight(row) * _zoom;
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

            double h = sheet.GetRowHeight(row) * _zoom;
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

        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)));
        var headerBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4));
        var headerText = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        var selectionFill = new SolidColorBrush(Color.FromArgb(0x22, 0x2A, 0x7D, 0xE1));
        var activePen = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x7D, 0xE1)), 2);

        DrawTableBanding(context, sheet);
        DrawSelection(context, selectionFill);
        DrawCells(context, sheet, gridPen);
        DrawMerges(context, sheet, gridPen);
        DrawTableChrome(context, sheet);
        DrawFreezeDividers(context);
        DrawHeaders(context, headerBrush, headerText, gridPen);
        DrawActiveOutline(context, activePen);
        DrawMarquee(context);
        DrawFillPreview(context);
        DrawFillHandle(context);
    }

    private void DrawCells(DrawingContext context, Worksheet sheet, Pen gridPen)
    {
        foreach (var (row, y, h) in _visibleRows)
        {
            foreach (var (col, x, w) in _visibleColumns)
            {
                if (sheet.FindMerge(row, col) is not null)
                {
                    continue; // merged regions are painted once, in DrawMerges
                }

                var rect = new Rect(x, y, w, h);
                context.DrawRectangle(null, gridPen, rect);

                var cell = sheet.Find(row, col);
                if (cell is null || cell.Value.IsEmpty)
                {
                    continue;
                }

                DrawCellContent(context, sheet, cell, rect);
            }
        }
    }

    private void DrawMerges(DrawingContext context, Worksheet sheet, Pen gridPen)
    {
        foreach (var region in sheet.MergedRegions)
        {
            if (!TryGetRegionRect(region.Range.Top, region.Range.Left, region.Range.Bottom, region.Range.Right, out var rect))
            {
                continue;
            }

            context.FillRectangle(Brushes.White, rect);
            context.DrawRectangle(null, gridPen, rect);

            var anchor = sheet.Find(region.Range.Top, region.Range.Left);
            if (anchor is not null && !anchor.Value.IsEmpty)
            {
                DrawCellContent(context, sheet, anchor, rect);
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

        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)), 1.5);
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

    private void DrawCellContent(DrawingContext context, Worksheet sheet, Cell cell, Rect rect)
    {
        var style = _session!.Workbook.Styles.Get(cell.StyleId);
        if (style.Fill.HasBackground)
        {
            context.FillRectangle(new SolidColorBrush(ToColor(style.Fill.Background!.Value)), rect);
        }

        string text = NumberFormatter.Format(cell.Value, style.NumberFormat, Culture);
        if (text.Length == 0)
        {
            return;
        }

        var typeface = new Typeface(
            style.Font.Family,
            style.Font.Italic ? FontStyle.Italic : FontStyle.Normal,
            style.Font.Bold ? FontWeight.Bold : FontWeight.Normal);

        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, style.Font.SizePoints * 96.0 / 72.0 * _zoom, TextBrushFor(style, cell.Value));

        double available = rect.Width - 6;
        formatted.MaxTextWidth = Math.Max(1, available);
        formatted.Trimming = TextTrimming.CharacterEllipsis;

        bool numeric = cell.Value.IsNumber || cell.Value.IsBoolean || cell.Value.IsError;
        var horizontal = style.Alignment.Horizontal switch
        {
            SAlign.General => numeric ? SAlign.Right : SAlign.Left,
            var h => h,
        };

        double tx = horizontal switch
        {
            SAlign.Right => rect.Right - 3 - formatted.Width,
            SAlign.Center => rect.X + (rect.Width - formatted.Width) / 2,
            _ => rect.X + 3,
        };
        double ty = rect.Bottom - formatted.Height - 2;

        context.DrawText(formatted, new Point(tx, Math.Max(rect.Y, ty)));
    }

    private IBrush TextBrushFor(CellStyle style, CellValue value)
    {
        if (value.IsError)
        {
            return Brushes.Crimson;
        }

        return new SolidColorBrush(style.Font.Color.IsTransparent ? Colors.Black : ToColor(style.Font.Color));
    }

    private void DrawSelection(DrawingContext context, IBrush fill)
    {
        if (!TryGetCellRect(Selection.Top, Selection.Left, out var topLeft) ||
            !TryGetCellRect(Selection.Bottom, Selection.Right, out var bottomRight))
        {
            // Selection may be partly off-screen; fall back to intersection of visible cells.
            foreach (var (row, y, h) in _visibleRows)
            {
                if (row < Selection.Top || row > Selection.Bottom)
                {
                    continue;
                }

                foreach (var (col, x, w) in _visibleColumns)
                {
                    if (col >= Selection.Left && col <= Selection.Right)
                    {
                        context.FillRectangle(fill, new Rect(x, y, w, h));
                    }
                }
            }

            return;
        }

        context.FillRectangle(fill, new Rect(topLeft.TopLeft, bottomRight.BottomRight));
    }

    private void DrawActiveOutline(DrawingContext context, Pen pen)
    {
        if (TryGetCellRect(Selection.ActiveRow, Selection.ActiveColumn, out var rect))
        {
            context.DrawRectangle(null, pen, rect);
        }
    }

    private void DrawHeaders(DrawingContext context, IBrush headerBrush, IBrush textBrush, Pen gridPen)
    {
        // Corner.
        context.FillRectangle(headerBrush, new Rect(0, 0, HeaderWidth, HeaderHeight));

        var typeface = new Typeface(Cellestra.Core.Defaults.FontFamily);
        foreach (var (col, x, w) in _visibleColumns)
        {
            var rect = new Rect(x, 0, w, HeaderHeight);
            bool active = col >= Selection.Left && col <= Selection.Right;
            context.FillRectangle(active ? new SolidColorBrush(Color.FromRgb(0xE6, 0xEF, 0xFB)) : headerBrush, rect);
            context.DrawRectangle(null, gridPen, rect);
            DrawCentered(context, SheetDimensions.ColumnToLetters(col), rect, typeface, textBrush);
        }

        foreach (var (row, y, h) in _visibleRows)
        {
            var rect = new Rect(0, y, HeaderWidth, h);
            bool active = row >= Selection.Top && row <= Selection.Bottom;
            context.FillRectangle(active ? new SolidColorBrush(Color.FromRgb(0xE6, 0xEF, 0xFB)) : headerBrush, rect);
            context.DrawRectangle(null, gridPen, rect);
            DrawCentered(context, (row + 1).ToString(CultureInfo.InvariantCulture), rect, typeface, textBrush);
        }
    }

    private static void DrawCentered(DrawingContext context, string text, Rect rect, Typeface typeface, IBrush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, brush);
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
        Focus();
        if (_editing)
        {
            CommitEdit(0, 0);
        }

        var point = e.GetPosition(this);
        if (TryHandleChromeClick(point))
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
        if (rowHeader && !columnHeader)
        {
            Selection.SetRange(row, 0, row, SheetDimensions.MaxColumns - 1);
        }
        else if (columnHeader && !rowHeader)
        {
            Selection.SetRange(0, column, SheetDimensions.MaxRows - 1, column);
        }
        else if (extend)
        {
            Selection.ExtendTo(row, column);
        }
        else
        {
            Selection.MoveTo(row, column);
            _selecting = true;
            e.Pointer.Capture(this);
        }

        RaiseSelectionChanged();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_filling)
        {
            UpdateFill(e.GetPosition(this));
            return;
        }

        if (!_selecting)
        {
            return;
        }

        if (HitTest(e.GetPosition(this), out int row, out int column, out _, out _))
        {
            Selection.ExtendTo(row, column);
            RaiseSelectionChanged();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
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
            case Key.Up: Move(row - 1, column, extend); break;
            case Key.Down: Move(row + 1, column, extend); break;
            case Key.Left: Move(row, column - 1, extend); break;
            case Key.Right: Move(row, column + 1, extend); break;
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

    public void BeginEdit(string? seed)
    {
        var sheet = Sheet;
        if (sheet is null)
        {
            return;
        }

        InvalidateClipboard(); // starting an edit drops any pending cut/copy marquee (#5)
        _editing = true;
        var cell = sheet.Find(Selection.ActiveRow, Selection.ActiveColumn);
        _editor.Text = seed ?? CellEditText(cell);
        _editor.IsVisible = true;
        InvalidateArrange();
        _editor.Focus();
        _editor.CaretIndex = _editor.Text?.Length ?? 0;
    }

    private static string CellEditText(Cell? cell)
    {
        if (cell is null)
        {
            return string.Empty;
        }

        return cell.IsFormula ? "=" + cell.Formula : cell.Value.ToInvariantString();
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
        }
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

        _session.Execute(Cellestra.Core.Editing.EditCommands.SetInput(
            _session.Workbook, _session.ActiveSheet.Name, row, column, text, Culture));

        if (moveRow != 0 || moveColumn != 0)
        {
            Selection.MoveTo(row + moveRow, column + moveColumn);
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
        }
        else
        {
            _editor.Arrange(new Rect(0, 0, 0, 0));
        }

        return finalSize;
    }

    private void RaiseSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);

    private static Color ToColor(ArgbColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);
}
