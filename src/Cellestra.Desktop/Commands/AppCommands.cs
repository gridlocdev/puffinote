using System;
using System.Collections.Generic;
using Avalonia.Input;
using Cellestra.Core.Editing;
using Cellestra.Core.Styling;

namespace Cellestra.Desktop.Commands;

/// <summary>
/// The one command catalog (Golden Rule #4, ARCHITECTURE §7). Defined once here; the menu bar,
/// ribbon, Command Palette, and keybindings all read it. Shortcuts are the defaults and are
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

    private static List<AppCommand> Build() => new()
    {
        // File
        new("file.new", "New", e => _ = e.NewWorkbookAsync(), "file-plus", new KeyGesture(Key.N, Cmd)),
        new("file.open", "Open…", e => _ = e.OpenWorkbookAsync(), "folder-open", new KeyGesture(Key.O, Cmd)),
        new("file.save", "Save", e => _ = e.SaveWorkbookAsync(), "save", new KeyGesture(Key.S, Cmd)),
        new("file.saveAs", "Save As…", e => _ = e.SaveWorkbookAsAsync(), "save", new KeyGesture(Key.S, CmdShift)),
        new("file.importCsv", "Import CSV…", e => _ = e.ImportCsvAsync(), "table-import"),
        new("file.exportCsv", "Export CSV…", e => _ = e.ExportCsvAsync(), "table-export"),

        // Edit
        new("edit.undo", "Undo", e => { e.Session.Undo(); e.Refresh(); }, "undo",
            new KeyGesture(Key.Z, Cmd), e => e.Session.CanUndo),
        new("edit.redo", "Redo", e => { e.Session.Redo(); e.Refresh(); }, "redo",
            new KeyGesture(Key.Z, CmdShift), e => e.Session.CanRedo),
        new("edit.cut", "Cut", e => e.Cut(), "scissors", new KeyGesture(Key.X, Cmd)),
        new("edit.copy", "Copy", e => e.Copy(), "copy", new KeyGesture(Key.C, Cmd)),
        new("edit.paste", "Paste", e => e.Paste(), "clipboard", new KeyGesture(Key.V, Cmd)),
        new("edit.delete", "Delete", ClearContents, "eraser", new KeyGesture(Key.Delete)),
        new("edit.selectAll", "Select All", SelectAll, null, new KeyGesture(Key.A, Cmd)),
        new("edit.find", "Find…", e => e.ShowFindReplace(replace: false), "search", new KeyGesture(Key.F, Cmd)),
        new("edit.findReplace", "Find & Replace…", e => e.ShowFindReplace(replace: true), "search",
            new KeyGesture(Key.F, CmdAlt)),

        // Format
        new("format.bold", "Bold", e => ToggleFont(e, f => f.Bold, (f, v) => f.WithBold(v)), "bold",
            new KeyGesture(Key.B, Cmd)),
        new("format.italic", "Italic", e => ToggleFont(e, f => f.Italic, (f, v) => f.WithItalic(v)), "italic",
            new KeyGesture(Key.I, Cmd)),
        new("format.underline", "Underline", e => ToggleFont(e, f => f.Underline, (f, v) => f.WithUnderline(v)), "underline",
            new KeyGesture(Key.U, Cmd)),
        new("format.cells", "Cells…", e => e.ShowFormatCellsDialog(), "settings", new KeyGesture(Key.D1, Cmd)),

        // Insert
        new("insert.rowsAbove", "Rows Above", e => InsertRows(e, above: true), "table-row-plus-before"),
        new("insert.columnsLeft", "Columns Left", e => InsertColumns(e, before: true), "table-column-plus-before"),
        new("insert.sheet", "Sheet", e => e.AddSheet(), "table-plus"),

        // Data
        new("data.sortAsc", "Sort Ascending", e => Sort(e, ascending: true), "sort-ascending"),
        new("data.sortDesc", "Sort Descending", e => Sort(e, ascending: false), "sort-descending"),
        new("data.customSort", "Sort…", e => e.ShowSortDialog(), "sort"),
        new("data.toggleFilter", "Filter", e => e.ToggleFilter(), "filter", new KeyGesture(Key.F, CmdShift)),
        new("data.clearFilter", "Clear Filter", ClearFilter, "filter-off"),
        new("data.createTable", "Table", CreateTable, "table", new KeyGesture(Key.T, Cmd)),
        new("data.removeDuplicates", "Remove Duplicates…", e => e.ShowRemoveDuplicatesDialog(), "table-minus"),
        new("data.textToColumns", "Text to Columns…", e => e.ShowTextToColumnsDialog(), "columns-split"),
        new("data.validation", "Data Validation…", e => e.ShowDataValidationDialog(), "checkbox"),

        // Formulas
        new("formula.recalc", "Recalculate", e => e.Session.RecalculateAll(), "refresh", new KeyGesture(Key.F9)),

        // View
        new("view.commandPalette", "Command Palette…", e => e.ShowCommandPalette(), "command",
            new KeyGesture(Key.P, CmdShift)),
        new("view.goToSheet", "Go to Sheet…", e => e.ShowSheetSwitcher(), "list", new KeyGesture(Key.P, Cmd)),
        new("view.zoomIn", "Zoom In", e => e.ZoomBy(0.1), "zoom-in", new KeyGesture(Key.OemPlus, Cmd)),
        new("view.zoomOut", "Zoom Out", e => e.ZoomBy(-0.1), "zoom-out", new KeyGesture(Key.OemMinus, Cmd)),
        new("view.actualSize", "Actual Size", e => e.ResetZoom(), "zoom-reset", new KeyGesture(Key.D0, Cmd)),
        new("view.showRibbon", "Show Ribbon", e => e.ToggleRibbon(), "chevron-up", new KeyGesture(Key.R, CmdAlt)),
        new("view.freezePanes", "Freeze Panes", e => e.ToggleFreezePanes(), "layout-grid"),

        // Insert (merge)
        new("format.merge", "Merge Cells", MergeCells, "table-merge"),

        // Help
        // "?" (⇧/) opens the shortcuts reference; gated off while a text field has focus so the
        // character types normally when editing a cell, the formula bar, or a dialog.
        new("help.shortcuts", "Keyboard Shortcuts", e => e.ShowKeyboardShortcuts(), "keyboard",
            new KeyGesture(Key.OemQuestion, KeyModifiers.Shift), e => !e.IsTextInputFocused),
    };

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

    // ---- Command implementations -----------------------------------------

    private static void ClearContents(IAppEnvironment e)
    {
        var sheet = e.Session.ActiveSheet;
        e.Session.Execute(EditCommands.ClearContents(sheet.Name, e.Selection.Range));
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
        string name = NextTableName(sheet);
        var table = new Cellestra.Core.Data.Table(name, range, header);
        e.Session.Execute(Cellestra.Core.Editing.DataEditCommands.CreateTable(sheet.Name, table));
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
