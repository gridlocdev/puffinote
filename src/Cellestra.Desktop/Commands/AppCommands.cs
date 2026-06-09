using System;
using System.Collections.Generic;
using Avalonia.Input;
using Cellestra.Core.Editing;
using Cellestra.Core.Styling;

namespace Cellestra.Desktop.Commands;

/// <summary>
/// The one command catalog (Golden Rule #4, ARCHITECTURE §7). Defined once here; the menu bar,
/// command bar, Command Palette, and keybindings all read it. Shortcuts are the defaults and are
/// remappable in Settings.
/// </summary>
public static class AppCommands
{
    private static readonly List<AppCommand> _all = Build();
    private static readonly Dictionary<string, AppCommand> _byId =
        _all.ToDictionary(c => c.Id, StringComparer.Ordinal);

    public static IReadOnlyList<AppCommand> All => _all;

    public static AppCommand? ById(string id) => _byId.GetValueOrDefault(id);

    private const KeyModifiers Cmd = KeyModifiers.Meta;
    private const KeyModifiers CmdShift = KeyModifiers.Meta | KeyModifiers.Shift;
    private const KeyModifiers CmdAlt = KeyModifiers.Meta | KeyModifiers.Alt;

    // Format Painter is a two-step gesture: the first invocation captures the active cell's
    // formatting (the "stamp"); the next applies it to the selection and clears the stamp.
    private static Cellestra.Core.Editing.FormatBlock? _formatStamp;

    private static List<AppCommand> Build() => new()
    {
        // File
        new("file.new", "New", "Start a new, empty workbook.", e => _ = e.NewWorkbookAsync(), "file-plus", new KeyGesture(Key.N, Cmd)),
        new("file.open", "Open…", "Open a spreadsheet from disk.", e => _ = e.OpenWorkbookAsync(), "folder-open", new KeyGesture(Key.O, Cmd)),
        new("file.save", "Save", "Save changes to the current file.", e => _ = e.SaveWorkbookAsync(), "save", new KeyGesture(Key.S, Cmd)),
        new("file.saveAs", "Save As…", "Save a copy under a new name.", e => _ = e.SaveWorkbookAsAsync(), "save", new KeyGesture(Key.S, CmdShift)),
        new("file.importCsv", "Import CSV…", "Load data from a CSV file.", e => _ = e.ImportCsvAsync(), "file-down"),
        new("file.exportCsv", "Export CSV…", "Write the active sheet to a CSV file.", e => _ = e.ExportCsvAsync(), "file-up"),

        // Edit
        new("edit.undo", "Undo", "Step backward through your changes.", e => { e.Session.Undo(); e.Refresh(); }, "undo",
            new KeyGesture(Key.Z, Cmd), e => e.Session.CanUndo),
        new("edit.redo", "Redo", "Re-apply a change you undid.", e => { e.Session.Redo(); e.Refresh(); }, "redo",
            new KeyGesture(Key.Z, CmdShift), e => e.Session.CanRedo),
        new("edit.cut", "Cut", "Move the selection to the clipboard.", e => e.Cut(), "scissors", new KeyGesture(Key.X, Cmd)),
        new("edit.copy", "Copy", "Copy the selection to the clipboard.", e => e.Copy(), "copy", new KeyGesture(Key.C, Cmd)),
        new("edit.paste", "Paste", "Paste from the clipboard.", e => e.Paste(), "clipboard", new KeyGesture(Key.V, Cmd)),
        new("edit.formatPainter", "Format Painter", "Copy formatting from the active cell, then paste it onto the next selection.",
            FormatPainter, "paint-bucket", new KeyGesture(Key.C, CmdShift), isActive: _ => _formatStamp is not null),
        new("edit.delete", "Clear Contents", "Clear the values, keep the formatting.", ClearContents, "eraser", new KeyGesture(Key.Delete)),
        new("edit.clearFormats", "Clear Formatting", "Reset styling, keep the values.", ClearFormatting, "remove-formatting"),
        new("edit.clearAll", "Clear All", "Clear both values and formatting.", ClearAll, "eraser"),
        new("edit.selectAll", "Select All", "Select the data region, then the whole sheet.", SelectAll, "square-dashed", new KeyGesture(Key.A, Cmd)),
        new("edit.find", "Find…", "Find text in the sheet.", e => e.ShowFindReplace(replace: false), "search", new KeyGesture(Key.F, Cmd)),
        new("edit.findReplace", "Find & Replace…", "Find text and replace it.", e => e.ShowFindReplace(replace: true), "replace",
            new KeyGesture(Key.F, CmdAlt)),

        // Format
        new("format.bold", "Bold", "Make the text heavier.", e => ToggleFont(e, f => f.Bold, (f, v) => f.WithBold(v)), "bold",
            new KeyGesture(Key.B, Cmd), isActive: e => ActiveStyle(e).Font.Bold),
        new("format.italic", "Italic", "Slant the text.", e => ToggleFont(e, f => f.Italic, (f, v) => f.WithItalic(v)), "italic",
            new KeyGesture(Key.I, Cmd), isActive: e => ActiveStyle(e).Font.Italic),
        new("format.underline", "Underline", "Underline the text.", e => ToggleFont(e, f => f.Underline, (f, v) => f.WithUnderline(v)), "underline",
            new KeyGesture(Key.U, Cmd), isActive: e => ActiveStyle(e).Font.Underline),
        new("format.strikethrough", "Strikethrough", "Draw a line through the text.", e => ToggleFont(e, f => f.Strikethrough, (f, v) => f.WithStrikethrough(v)), "strikethrough",
            new KeyGesture(Key.X, CmdShift), isActive: e => ActiveStyle(e).Font.Strikethrough),
        new("format.alignLeft", "Align Left", "Align contents to the left.", e => SetAlignment(e, HorizontalAlignment.Left), "align-left",
            isActive: e => ActiveStyle(e).Alignment.Horizontal == HorizontalAlignment.Left),
        new("format.alignCenter", "Align Center", "Centre contents in the cell.", e => SetAlignment(e, HorizontalAlignment.Center), "align-center",
            isActive: e => ActiveStyle(e).Alignment.Horizontal == HorizontalAlignment.Center),
        new("format.alignRight", "Align Right", "Align contents to the right.", e => SetAlignment(e, HorizontalAlignment.Right), "align-right",
            isActive: e => ActiveStyle(e).Alignment.Horizontal == HorizontalAlignment.Right),
        new("format.wrap", "Wrap Text", "Wrap long text onto multiple lines.", ToggleWrap, "wrap-text",
            isActive: e => ActiveStyle(e).Alignment.WrapText),
        new("format.currency", "Currency", "Format the number as currency.", e => ToggleNumberFormat(e, CurrencyFormat, NumberFormatKind.Currency),
            "dollar-sign", canExecute: IsNumericContext, isActive: e => ActiveStyle(e).NumberFormat.Kind == NumberFormatKind.Currency),
        new("format.percent", "Percent", "Format the number as a percentage.", e => ToggleNumberFormat(e, NumberFormat.Percent, NumberFormatKind.Percent),
            "percent", canExecute: IsNumericContext, isActive: e => ActiveStyle(e).NumberFormat.Kind == NumberFormatKind.Percent),
        new("format.date", "Date", "Format the number as a date.", e => ToggleNumberFormat(e, DateFormat, NumberFormatKind.Date),
            "calendar", canExecute: IsNumericContext, isActive: e => ActiveStyle(e).NumberFormat.Kind == NumberFormatKind.Date),
        new("format.cells", "Cells…", "Open the full cell-formatting dialog.", e => e.ShowFormatCellsDialog(), "settings-2", new KeyGesture(Key.D1, Cmd)),

        // Insert
        new("insert.rowsAbove", "Rows Above", "Insert rows above the selection.", e => InsertRows(e, above: true), "rows-plus"),
        new("insert.columnsLeft", "Columns Left", "Insert columns left of the selection.", e => InsertColumns(e, before: true), "columns-plus"),
        new("edit.deleteRows", "Delete Rows", "Delete the selected rows.", DeleteRows, "trash-2"),
        new("edit.deleteColumns", "Delete Columns", "Delete the selected columns.", DeleteColumns, "trash-2"),
        new("insert.sheet", "Sheet", "Add a new worksheet.", e => e.AddSheet(), "plus"),
        new("sheet.rename", "Rename Sheet…", "Rename the active worksheet.", e => e.RenameActiveSheet(), "pencil"),
        new("sheet.delete", "Delete Sheet", "Delete the active worksheet.", e => e.DeleteActiveSheet(), "trash-2",
            canExecute: e => e.Session.Workbook.Worksheets.Count > 1),

        // Data
        new("data.sortAsc", "Sort Ascending", "Sort the column A→Z.", e => Sort(e, ascending: true), "sort-asc"),
        new("data.sortDesc", "Sort Descending", "Sort the column Z→A.", e => Sort(e, ascending: false), "sort-desc"),
        new("data.customSort", "Sort…", "Sort by one or more columns.", e => e.ShowSortDialog(), "sort"),
        new("data.toggleFilter", "Filter", "Filter rows by a rule.", e => e.ToggleFilter(), "filter", new KeyGesture(Key.F, CmdShift),
            isActive: e => e.Session.ActiveSheet.ActiveFilter is not null),
        new("data.clearFilter", "Clear Filter", "Remove the active filter.", ClearFilter, "filter-x"),
        new("data.createTable", "Table", "Turn the range into a table.", CreateTable, "table", new KeyGesture(Key.T, Cmd)),
        new("data.removeDuplicates", "Remove Duplicates…", "Drop repeated rows from the range.", e => e.ShowRemoveDuplicatesDialog(), "list-x"),
        new("data.textToColumns", "Text to Columns…", "Split a column on a delimiter.", e => e.ShowTextToColumnsDialog(), "columns"),
        new("data.validation", "Data Validation…", "Restrict what can be entered.", e => e.ShowDataValidationDialog(), "square-check"),

        // Formulas
        new("formula.recalc", "Recalculate", "Recompute every formula now.", e => e.Session.RecalculateAll(), "refresh-cw", new KeyGesture(Key.F9)),

        // View
        new("view.commandPalette", "Command Palette…", "Search and run any command.", e => e.ShowCommandPalette(), "command",
            new KeyGesture(Key.K, Cmd)),
        new("view.goToSheet", "Go to Sheet…", "Jump to another worksheet.", e => e.ShowSheetSwitcher(), "list", new KeyGesture(Key.P, CmdShift)),
        new("view.zoomIn", "Zoom In", "Make everything larger.", e => e.ZoomBy(0.1), "zoom-in", new KeyGesture(Key.OemPlus, Cmd)),
        new("view.zoomOut", "Zoom Out", "Make everything smaller.", e => e.ZoomBy(-0.1), "zoom-out", new KeyGesture(Key.OemMinus, Cmd)),
        new("view.actualSize", "Actual Size", "Reset zoom to 100%.", e => e.ResetZoom(), "maximize", new KeyGesture(Key.D0, Cmd)),
        new("view.freezePanes", "Freeze Panes", "Keep header rows and columns in view.", e => e.ToggleFreezePanes(), "layout-grid"),

        // Insert (merge)
        new("format.merge", "Merge Cells", "Combine the selected cells into one.", MergeCells, "table-cells-merge"),

        // Help
        // "?" (⇧/) opens the shortcuts reference; gated off while a text field has focus so the
        // character types normally when editing a cell, the formula bar, or a dialog.
        new("help.shortcuts", "Keyboard Shortcuts", "List every keyboard shortcut.", e => e.ShowKeyboardShortcuts(), "keyboard",
            new KeyGesture(Key.OemQuestion, KeyModifiers.Shift), e => !e.IsTextInputFocused),
        new("help.about", "About Cellestra", "About Cellestra — version, pronunciation, and license.", e => e.ShowAbout(), "info"),
    };

    // Number-format codes the bar's one-tap toggles apply. They classify (NumberFormatParser) as
    // Currency/Date so the toggle can tell when a cell already has that format and revert it.
    private static readonly NumberFormat CurrencyFormat = new("$#,##0.00");
    private static readonly NumberFormat DateFormat = new("yyyy-mm-dd");

    private static void MergeCells(IAppEnvironment e)
    {
        var sheet = e.Session.ActiveSheet;
        if (e.Selection.IsSingleCell)
        {
            // Toggle: unmerge if the active cell is in a merge.
            if (sheet.FindMerge(e.Selection.ActiveRow, e.Selection.ActiveColumn) is not null)
            {
                e.Session.Execute(Cellestra.Core.Editing.EditCommands.UnmergeCells(sheet.Name, e.Selection.ActiveRow, e.Selection.ActiveColumn));
                e.Refresh();
            }

            return;
        }

        e.Session.Execute(Cellestra.Core.Editing.EditCommands.MergeCells(sheet.Name, e.Selection.Range));
        e.Refresh();
    }

    /// <summary>
    /// Format Painter (two-step): with no stamp held, capture the active cell's formatting (its
    /// single cell, or the whole selection so a multi-cell pattern is reused); with a stamp held,
    /// paint it onto the current selection — including replicating merge-cells — then clear it.
    /// </summary>
    private static void FormatPainter(IAppEnvironment e)
    {
        var session = e.Session;
        var sheet = session.ActiveSheet;

        if (_formatStamp is null)
        {
            var source = e.Selection.IsSingleCell
                ? new Cellestra.Core.Model.CellRange(e.Selection.ActiveAddress, e.Selection.ActiveAddress)
                : e.Selection.Range;
            _formatStamp = Cellestra.Core.Editing.FormatBlock.Capture(session.Workbook, sheet, source);
            e.Narrate("Format Painter: select cells to paint the copied formatting.");
            e.Refresh();
            return;
        }

        session.Execute(_formatStamp.ApplyTo(sheet.Name, e.Selection.Range));
        _formatStamp = null;
        e.Refresh();
    }

    // ---- Command implementations -----------------------------------------

    private static void ClearContents(IAppEnvironment e)
    {
        var sheet = e.Session.ActiveSheet;
        e.Session.Execute(EditCommands.ClearContents(sheet.Name, e.Selection.Range));
        e.Refresh();
    }

    private static void ClearFormatting(IAppEnvironment e)
    {
        var session = e.Session;
        session.Execute(EditCommands.ClearFormatting(session.Workbook, session.ActiveSheet.Name, e.Selection.Range));
        e.Refresh();
    }

    private static void ClearAll(IAppEnvironment e)
    {
        var session = e.Session;
        session.Execute(EditCommands.ClearAll(session.ActiveSheet.Name, e.Selection.Range));
        e.Refresh();
    }

    private static void SelectAll(IAppEnvironment e)
    {
        // Expanding Select-All (#26): the first press selects the contiguous data region around
        // the active cell; a second press (selection already at that region) selects the whole sheet.
        var sheet = e.Session.ActiveSheet;
        var selection = e.Selection;
        var region = Cellestra.Core.Data.DataRegion.CurrentRegion(sheet, selection.ActiveRow, selection.ActiveColumn);

        bool atRegion = selection.Top == region.Top && selection.Left == region.Left &&
            selection.Bottom == region.Bottom && selection.Right == region.Right;

        if (atRegion)
        {
            selection.SetRange(0, 0, Cellestra.Core.Model.SheetDimensions.MaxRows - 1, Cellestra.Core.Model.SheetDimensions.MaxColumns - 1);
        }
        else
        {
            selection.SetRange(region.Top, region.Left, region.Bottom, region.Right);
        }

        e.Refresh();
    }

    private static void ToggleFont(IAppEnvironment e, Func<Font, bool> get, Func<Font, bool, Font> set)
    {
        var session = e.Session;
        var sheet = session.ActiveSheet;
        var active = sheet.Find(e.Selection.ActiveRow, e.Selection.ActiveColumn);
        var style = session.Workbook.Styles.Get(active?.StyleId ?? StylePool.DefaultId);
        bool target = !get(style.Font);

        session.Execute(EditCommands.ApplyStyle(session.Workbook, sheet.Name, e.Selection.Range,
            s => s.WithFont(set(s.Font, target)), "Format"));
        e.Refresh();
    }

    /// <summary>The cell style of the active cell — the basis for "is this toggle already on?" checks.</summary>
    private static CellStyle ActiveStyle(IAppEnvironment e)
    {
        var sheet = e.Session.ActiveSheet;
        var active = sheet.Find(e.Selection.ActiveRow, e.Selection.ActiveColumn);
        return e.Session.Workbook.Styles.Get(active?.StyleId ?? StylePool.DefaultId);
    }

    private static void SetAlignment(IAppEnvironment e, HorizontalAlignment horizontal)
    {
        var session = e.Session;
        session.Execute(EditCommands.ApplyStyle(session.Workbook, session.ActiveSheet.Name, e.Selection.Range,
            s => s.WithAlignment(s.Alignment with { Horizontal = horizontal }), "Align"));
        e.Refresh();
    }

    private static void ToggleWrap(IAppEnvironment e)
    {
        var session = e.Session;
        bool target = !ActiveStyle(e).Alignment.WrapText;
        session.Execute(EditCommands.ApplyStyle(session.Workbook, session.ActiveSheet.Name, e.Selection.Range,
            s => s.WithAlignment(s.Alignment with { WrapText = target }), "Wrap Text"));
        e.Refresh();
    }

    /// <summary>Apply <paramref name="format"/>, or revert to General when the active cell already has that kind (a toggle).</summary>
    private static void ToggleNumberFormat(IAppEnvironment e, NumberFormat format, NumberFormatKind kind)
    {
        var session = e.Session;
        bool applying = ActiveStyle(e).NumberFormat.Kind != kind;
        var target = applying ? format : NumberFormat.General;
        session.Execute(EditCommands.ApplyStyle(session.Workbook, session.ActiveSheet.Name, e.Selection.Range,
            s => s.WithNumberFormat(target), "Number Format"));
        e.Refresh();

        // A freshly applied format (currency/date) can be wider than the column — grow it so the
        // result shows instead of reading #####. Removing a format never needs more room.
        if (applying)
        {
            e.AutofitColumns(e.Selection.Left, e.Selection.Right);
        }
    }

    /// <summary>
    /// True when the active cell can take a number format — it's empty or already numeric. Mirrors the
    /// prototype: the Numbers tools grey out on text so applying "Currency" to a label can't fail silently.
    /// </summary>
    private static bool IsNumericContext(IAppEnvironment e)
    {
        var value = e.Session.ActiveSheet.GetValue(e.Selection.ActiveRow, e.Selection.ActiveColumn);
        return value.IsEmpty || value.IsNumber;
    }

    private static void InsertRows(IAppEnvironment e, bool above)
    {
        var sheet = e.Session.ActiveSheet;
        int at = above ? e.Selection.Top : e.Selection.Bottom + 1;
        e.Session.Execute(new StructuralEditCommand(sheet.Name, StructuralAxis.Row, StructuralOperation.Insert, at, e.Selection.Bottom - e.Selection.Top + 1));
        e.Refresh();
    }

    private static void InsertColumns(IAppEnvironment e, bool before)
    {
        var sheet = e.Session.ActiveSheet;
        int at = before ? e.Selection.Left : e.Selection.Right + 1;
        e.Session.Execute(new StructuralEditCommand(sheet.Name, StructuralAxis.Column, StructuralOperation.Insert, at, e.Selection.Right - e.Selection.Left + 1));
        e.Refresh();
    }

    private static void DeleteRows(IAppEnvironment e)
    {
        var sheet = e.Session.ActiveSheet;
        int count = e.Selection.Bottom - e.Selection.Top + 1;
        if (count >= Cellestra.Core.Model.SheetDimensions.MaxRows)
        {
            return; // refuse to delete every row (it would wipe the sheet); Excel does the same
        }

        e.Session.Execute(new StructuralEditCommand(sheet.Name, StructuralAxis.Row, StructuralOperation.Delete, e.Selection.Top, count));
        e.Refresh();
    }

    private static void DeleteColumns(IAppEnvironment e)
    {
        var sheet = e.Session.ActiveSheet;
        int count = e.Selection.Right - e.Selection.Left + 1;
        if (count >= Cellestra.Core.Model.SheetDimensions.MaxColumns)
        {
            return; // refuse to delete every column
        }

        e.Session.Execute(new StructuralEditCommand(sheet.Name, StructuralAxis.Column, StructuralOperation.Delete, e.Selection.Left, count));
        e.Refresh();
    }

    // ---- Data -------------------------------------------------------------

    /// <summary>The data region a data command acts on: the selection, or the current region when one cell is selected.</summary>
    public static (Cellestra.Core.Model.Worksheet Sheet, Cellestra.Core.Model.CellRange Range, bool Header) DataTarget(IAppEnvironment e)
    {
        var sheet = e.Session.ActiveSheet;
        var range = e.Selection.IsSingleCell
            ? Cellestra.Core.Data.DataRegion.CurrentRegion(sheet, e.Selection.ActiveRow, e.Selection.ActiveColumn)
            : e.Selection.Range;
        bool header = Cellestra.Core.Data.DataRegion.LooksLikeHeader(sheet, range);
        return (sheet, range, header);
    }

    private static void Sort(IAppEnvironment e, bool ascending)
    {
        var (sheet, range, header) = DataTarget(e);
        int column = Math.Clamp(e.Selection.ActiveColumn, range.Left, range.Right);
        e.Session.Execute(Cellestra.Core.Editing.DataEditCommands.SortByColumn(sheet.Name, range, column, ascending, header));
        e.Refresh();
    }

    private static void ClearFilter(IAppEnvironment e)
    {
        var (sheet, range, _) = DataTarget(e);
        e.Session.Execute(Cellestra.Core.Editing.DataEditCommands.ClearFilter(sheet.Name, range));
        e.Refresh();
    }

    private static void CreateTable(IAppEnvironment e)
    {
        var (sheet, range, header) = DataTarget(e);

        // A cell may belong to only one table; AddTable throws on overlap. Catch that here and
        // explain it instead of crashing (Nielsen heuristic #9 — recover from errors).
        var existing = sheet.Tables.FirstOrDefault(t => t.Range.Intersects(range));
        if (existing is not null)
        {
            e.ShowMessage(
                "These cells are already in a table",
                $"The selection overlaps the table “{existing.Name}”, and a cell can belong to only one table.\n\n" +
                "To create another table, select cells outside any existing table first. " +
                "To change this one, select a cell inside it and use Sort, Filter, or Remove Duplicates from the Data menu.");
            return;
        }

        string name = NextTableName(sheet);
        var table = new Cellestra.Core.Data.Table(name, range, header);
        e.Session.Execute(Cellestra.Core.Editing.DataEditCommands.CreateTable(e.Session.Workbook, sheet.Name, table));
        e.Refresh();
    }

    private static string NextTableName(Cellestra.Core.Model.Worksheet sheet)
    {
        var taken = new HashSet<string>(sheet.Tables.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        for (int n = 1; ; n++)
        {
            string candidate = $"Table{n}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
