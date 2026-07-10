using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Cellestra.Core.Formulas.References;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using SAlign = Cellestra.Core.Styling.HorizontalAlignment;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

public sealed partial class SpreadsheetGrid
{
    // ---- Inline editing ---------------------------------------------------

    /// <summary>Test seam: the inline editor's current font size and whether its text is top-aligned.</summary>
    internal double EditorFontSize => _editor.FontSize;

    internal bool EditorTextIsTopAligned => _editor.VerticalContentAlignment == Avalonia.Layout.VerticalAlignment.Top;

    /// <summary>Test seams: the live editor's weight and horizontal alignment, so a format command applied mid-edit can be verified.</summary>
    internal bool EditorIsBold => _editor.FontWeight == FontWeight.Bold;

    internal TextAlignment EditorTextAlignment => _editor.TextAlignment;

    /// <summary>Test seams: the live editor's text and selected substring, to verify select-all-on-open.</summary>
    internal string EditorText => _editor.Text ?? string.Empty;

    /// <summary>Test seam: the inline editor's actual arranged bounds (so its rightward growth can be verified).</summary>
    internal Rect EditorBoundsForTest => _editor.Bounds;

    internal bool EditorTextIsAllSelected =>
        _editor.SelectionStart == 0 && _editor.SelectionEnd == (_editor.Text?.Length ?? 0) && (_editor.Text?.Length ?? 0) > 0;

    /// <summary>Test seam: the rectangle the inline editor would occupy for the active cell (the full
    /// merged region when the active cell is merged), so a merged editor's span can be verified.</summary>
    internal Rect ActiveEditorRect => TryGetActiveCellRect(out var rect) ? rect : default;

    /// <summary>Match the inline editor to the cell's drawn appearance — font, weight/style, underline,
    /// text colour, and fill — so editing looks the same as the displayed cell (not default black-on-white).</summary>
    private void ApplyEditorFont(Cell? cell)
    {
        var style = _session!.Workbook.Styles.Get(cell?.StyleId ?? StylePool.DefaultId);
        _editor.FontFamily = new FontFamily(style.Font.Family);
        _editor.FontSize = style.Font.SizePoints * 96.0 / 72.0 * Zoom;
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
        if (!IsInlineEditing || _session is null)
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
        double fontSize = style.Font.SizePoints * 96.0 / 72.0 * Zoom;
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

    /// <summary>
    /// Opens the cell for editing with its existing content fully selected, so the next keystroke
    /// replaces it (used when a new table's first header cell — pre-filled "Column 1" — opens for
    /// naming, mirroring Excel's "type straight over the placeholder").
    /// </summary>
    public void BeginEditSelectAll() => BeginEdit(seed: null, focus: true, selectAll: true);

    /// <param name="focus">
    /// When false the inline editor is shown and seeded but keeps focus where it is. The formula bar
    /// uses this to mirror the cell while the user keeps typing in the bar (task 9) — focusing the
    /// editor would steal the caret and trigger its LostFocus commit.
    /// </param>
    public void BeginEdit(string? seed, bool focus) => BeginEdit(seed, focus, selectAll: false);

    /// <param name="selectAll">
    /// When true the seeded text is selected rather than left with the caret at its end, so the first
    /// keystroke overtypes it. Only meaningful when <paramref name="focus"/> is true.
    /// </param>
    public void BeginEdit(string? seed, bool focus, bool selectAll)
    {
        var sheet = Sheet;
        if (sheet is null)
        {
            return;
        }

        InvalidateClipboard(); // starting an edit drops any pending cut/copy marquee (#5)
        ResetPointMode();
        IsInlineEditing = true;
        var cell = sheet.Find(Selection.ActiveRow, Selection.ActiveColumn);
        ApplyEditorFont(cell); // match the cell's drawn font so the text doesn't change size on edit
        _editor.Text = seed ?? CellEditText(cell);
        _editor.IsVisible = true;
        InvalidateArrange();
        if (focus)
        {
            _editor.Focus();
        }

        int length = _editor.Text?.Length ?? 0;
        if (selectAll && focus)
        {
            // Select the whole seeded value so the next key overtypes it (table-header naming). Setting
            // SelectionStart/End (not CaretIndex, which collapses the selection) anchors the range.
            _editor.SelectionStart = 0;
            _editor.SelectionEnd = length;
        }
        else
        {
            _editor.CaretIndex = length;
        }

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
        if (!IsInlineEditing || _session is null)
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

        IsInlineEditing = false;
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
        IsInlineEditing = false;
        _editor.IsVisible = false;
        HideSignatureHelp();
        Focus();
    }

    /// <summary>
    /// The rectangle the inline editor should occupy: the cell, widened rightward to fit the text as
    /// it grows (covering the cells to its right), like Google Sheets/Excel. Never narrower than the
    /// cell, never past the grid's right edge; height stays the cell's so it never grows downward.
    /// </summary>
    private Rect ExpandedEditorRect(Rect cell, Size finalSize)
    {
        string text = _editor.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return cell;
        }

        var typeface = new Typeface(_editor.FontFamily, _editor.FontStyle, _editor.FontWeight);
        var measured = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, _editor.FontSize, Brushes.Black);
        // Room for the text, the editor's own horizontal padding, and a little slack for the caret.
        double needed = measured.Width + 2 * EditorPadX + 6;
        double width = Math.Clamp(needed, cell.Width, Math.Max(cell.Width, finalSize.Width - cell.X));
        return new Rect(cell.X, cell.Y, width, cell.Height);
    }
}
