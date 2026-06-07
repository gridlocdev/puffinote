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

        // Format
        new("format.bold", "Bold", e => ToggleFont(e, f => f.Bold, (f, v) => f.WithBold(v)), "bold",
            new KeyGesture(Key.B, Cmd)),
        new("format.italic", "Italic", e => ToggleFont(e, f => f.Italic, (f, v) => f.WithItalic(v)), "italic",
            new KeyGesture(Key.I, Cmd)),
        new("format.underline", "Underline", e => ToggleFont(e, f => f.Underline, (f, v) => f.WithUnderline(v)), "underline",
            new KeyGesture(Key.U, Cmd)),

        // Insert
        new("insert.rowsAbove", "Rows Above", e => InsertRows(e, above: true), "table-row-plus-before"),
        new("insert.columnsLeft", "Columns Left", e => InsertColumns(e, before: true), "table-column-plus-before"),
        new("insert.sheet", "Sheet", e => e.AddSheet(), "table-plus"),

        // Data / formulas
        new("formula.recalc", "Recalculate", e => e.Session.RecalculateAll(), "refresh", new KeyGesture(Key.F9)),

        // View
        new("view.commandPalette", "Command Palette…", e => e.ShowCommandPalette(), "command",
            new KeyGesture(Key.P, CmdShift)),
        new("view.goToSheet", "Go to Sheet…", e => e.ShowSheetSwitcher(), "list", new KeyGesture(Key.P, Cmd)),
        new("view.zoomIn", "Zoom In", e => e.ZoomBy(0.1), "zoom-in", new KeyGesture(Key.OemPlus, Cmd)),
        new("view.zoomOut", "Zoom Out", e => e.ZoomBy(-0.1), "zoom-out", new KeyGesture(Key.OemMinus, Cmd)),
        new("view.actualSize", "Actual Size", e => e.ResetZoom(), "zoom-reset", new KeyGesture(Key.D0, Cmd)),
        new("view.showRibbon", "Show Ribbon", e => e.ToggleRibbon(), "chevron-up", new KeyGesture(Key.R, CmdAlt)),
    };

    // ---- Command implementations -----------------------------------------

    private static void ClearContents(IAppEnvironment e)
    {
        var sheet = e.Session.ActiveSheet;
        e.Session.Execute(EditCommands.ClearContents(sheet.Name, e.Selection.Range));
        e.Refresh();
    }

    private static void SelectAll(IAppEnvironment e)
    {
        var used = e.Session.ActiveSheet.GetUsedRange();
        if (used is { } range)
        {
            e.Selection.SetRange(range.Top, range.Left, range.Bottom, range.Right);
        }
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
}
