using Avalonia.Input;
using Cellestra.Core.Editing;
using Cellestra.Core.Styling;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls.CommandBar;

namespace Cellestra.Desktop.Commands;

/// <summary>
/// The one command catalog (Golden Rule #4, ARCHITECTURE §7). Built once per <see cref="IAppEnvironment"/>
/// (the editor view): the menu bar, command bar, Command Palette, and key bindings all read it. Each entry
/// is the shared, environment-agnostic <see cref="AppCommand"/> whose action and enabled/active checks
/// <b>close over the environment</b> captured here (proposal 0001 §5), so the palette and menus stay
/// app-agnostic. Shortcuts are the defaults and are remappable in Settings.
/// </summary>
public sealed class AppCommands
{
    private readonly List<AppCommand> _all;
    private readonly Dictionary<string, AppCommand> _byId;
    private readonly List<string> _recent = [];

    /// <summary>How many recent commands the palette offers back. Enough to cover a repeated task, short
    /// enough that the list still reads as "what I was just doing" rather than a second catalog.</summary>
    private const int RecentLimit = 5;

    public AppCommands(IAppEnvironment environment)
    {
        // Every command is wrapped so that running it — from anywhere: a menu, a shortcut, the bar, the
        // palette itself — is remembered. Wrapping here rather than asking each surface to report what it
        // ran is the same reasoning as the one-catalog rule: a surface that forgot would silently make the
        // history wrong, and there would be no single place to notice.
        _all = [.. Build(environment).Select(Recording)];
        _byId = _all.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<AppCommand> All => _all;

    public AppCommand? ById(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The titles of the commands run this session, most recent first, capped at <see cref="RecentLimit"/>.
    /// Session-only and never persisted — it answers "what was I just doing", not "what do I usually do".</summary>
    public IReadOnlyList<string> RecentTitles => [.. _recent.Select(id => _byId[id].Title)];

    private AppCommand Recording(AppCommand command) => new(
        command.Id,
        command.Title,
        command.Description,
        command.Shortcut,
        () =>
        {
            Remember(command.Id);
            command.Execute();
        },
        command.CanExecute,
        command.IsActive,
        command.Icon);

    private void Remember(string id)
    {
        // The surfaces that only *offer* commands are not commands you ran. Recording ⌘K would put
        // "Command Palette…" at the top of its own Recent list every single time it opened.
        if (id is "view.commandMenu" or "view.goToSheet")
        {
            return;
        }

        _recent.Remove(id);
        _recent.Insert(0, id);
        if (_recent.Count > RecentLimit)
        {
            _recent.RemoveAt(_recent.Count - 1);
        }
    }

    private static List<AppCommand> Build(IAppEnvironment e) =>
    [
        // File
        new("file.new", "New", "Start a new, empty workbook.", Shortcut.Cmd(Key.N), () => _ = e.NewWorkbookAsync(), icon: "file-plus"),
        new("file.open", "Open…", "Open a spreadsheet from disk.", Shortcut.Cmd(Key.O), () => _ = e.OpenWorkbookAsync(), icon: "folder-open"),
        new("file.save", "Save", "Save changes to the current file.", Shortcut.Cmd(Key.S), () => _ = e.SaveWorkbookAsync(), icon: "save"),
        new("file.saveAs", "Save As…", "Save a copy under a new name.", Shortcut.Cmd(Key.S, KeyModifiers.Shift), () => _ = e.SaveWorkbookAsAsync(), icon: "save"),
        new("file.importCsv", "Import CSV…", "Load data from a CSV file.", null, () => _ = e.ImportCsvAsync(), icon: "file-down"),
        new("file.exportCsv", "Export CSV…", "Write the active sheet to a CSV file.", null, () => _ = e.ExportCsvAsync(), icon: "file-up"),

        // Edit
        new("edit.undo", "Undo", "Step backward through your changes.", Shortcut.Cmd(Key.Z), () =>
        {
            e.Session.Undo();
            e.Refresh();
        }, canExecute: () => e.Session.CanUndo, icon: "undo"),
        new("edit.redo", "Redo", "Re-apply a change you undid.", Shortcut.Cmd(Key.Z, KeyModifiers.Shift), () =>
        {
            e.Session.Redo();
            e.Refresh();
        }, canExecute: () => e.Session.CanRedo, icon: "redo"),
        new("edit.cut", "Cut", "Move the selection to the clipboard.", Shortcut.Cmd(Key.X), () => e.Cut(), icon: "scissors"),
        new("edit.copy", "Copy", "Copy the selection to the clipboard.", Shortcut.Cmd(Key.C), () => e.Copy(), icon: "copy"),
        new("edit.paste", "Paste", "Paste from the clipboard.", Shortcut.Cmd(Key.V), () => e.Paste(), icon: "clipboard"),
        new("edit.pasteSpecial", "Paste Special…", "Paste only values, formats, or formulas.", Shortcut.Cmd(Key.V, KeyModifiers.Alt), () => e.PasteSpecial(),
            icon: "clipboard-paste"),
        new("edit.autoSum", "AutoSum", "Sum the numbers above or to the left of the active cell.", Shortcut.Cmd(Key.OemPlus, KeyModifiers.Alt), () => AutoSum(e),
            icon: "sigma"),
        new("fill.down", "Fill Down", "Copy the top row of the selection down through the rest.", Shortcut.Cmd(Key.D), () => FillDown(e),
            icon: "arrow-down-to-line"),
        new("fill.right", "Fill Right", "Copy the left column of the selection rightward.", Shortcut.Cmd(Key.R), () => FillRight(e),
            icon: "arrow-right-to-line"),
        new("edit.formatPainter", "Format Painter", "Copy formatting from the active cell, then paste it onto the next selection.",
            Shortcut.Cmd(Key.C, KeyModifiers.Shift), () => FormatPainter(e), isActive: () => e.Session.IsFormatPainterArmed, icon: "paintbrush"),
        new("edit.delete", "Clear Contents", "Clear the values, keep the formatting.", Shortcut.Bare(Key.Delete), () => ClearContents(e), icon: "eraser"),
        new("edit.clearFormats", "Clear Formatting", "Reset styling, keep the values.", null, () => ClearFormatting(e), icon: "remove-formatting"),
        new("edit.clearAll", "Clear All", "Clear both values and formatting.", null, () => ClearAll(e), icon: "eraser"),
        new("sheet.resetData", "Reset Sheet Data", "Clear every cell and its formatting on the active sheet.", null, () => e.ResetSheetData(), icon: "eraser"),
        new("edit.selectAll", "Select All", "Select the data region, then the whole sheet.", Shortcut.Cmd(Key.A), () => SelectAll(e), icon: "square-dashed"),
        new("edit.find", "Find…", "Find text in the sheet.", Shortcut.Cmd(Key.F), () => e.ShowFindReplace(replace: false), icon: "search"),
        new("edit.findReplace", "Find & Replace…", "Find text and replace it.", Shortcut.Cmd(Key.F, KeyModifiers.Alt), () => e.ShowFindReplace(replace: true),
            icon: "replace"),

        // Format
        new("format.bold", "Bold", "Make the text heavier.", Shortcut.Cmd(Key.B), () => ToggleFont(e, f => f.Bold, (f, v) => f.WithBold(v)),
            isActive: () => ActiveStyle(e).Font.Bold, icon: "bold"),
        new("format.italic", "Italic", "Slant the text.", Shortcut.Cmd(Key.I), () => ToggleFont(e, f => f.Italic, (f, v) => f.WithItalic(v)),
            isActive: () => ActiveStyle(e).Font.Italic, icon: "italic"),
        new("format.underline", "Underline", "Underline the text.", Shortcut.Cmd(Key.U), () => ToggleFont(e, f => f.Underline, (f, v) => f.WithUnderline(v)),
            isActive: () => ActiveStyle(e).Font.Underline, icon: "underline"),
        new("format.strikethrough", "Strikethrough", "Draw a line through the text.", Shortcut.Cmd(Key.X, KeyModifiers.Shift),
            () => ToggleFont(e, f => f.Strikethrough, (f, v) => f.WithStrikethrough(v)), isActive: () => ActiveStyle(e).Font.Strikethrough, icon: "strikethrough"),
        // Alignment comes in two axes, and each one's names are qualified by its axis. "Align Center"
        // alone is two different commands in a searchable list — the one that moves text across the
        // cell and the one that moves it down it — and neither reading is more obvious than the other.
        new("format.alignLeft", "Align Left (Horizontal)", "Align contents to the left.", null, () => SetAlignment(e, HorizontalAlignment.Left),
            isActive: () => ActiveStyle(e).Alignment.Horizontal == HorizontalAlignment.Left, icon: "align-left"),
        new("format.alignCenter", "Align Center (Horizontal)", "Centre contents in the cell.", null, () => SetAlignment(e, HorizontalAlignment.Center),
            isActive: () => ActiveStyle(e).Alignment.Horizontal == HorizontalAlignment.Center, icon: "align-center"),
        new("format.alignRight", "Align Right (Horizontal)", "Align contents to the right.", null, () => SetAlignment(e, HorizontalAlignment.Right),
            isActive: () => ActiveStyle(e).Alignment.Horizontal == HorizontalAlignment.Right, icon: "align-right"),
        new("format.alignTop", "Align Top (Vertical)", "Sit contents at the top of the cell.", null, () => SetVerticalAlignment(e, VerticalAlignment.Top),
            isActive: () => ActiveStyle(e).Alignment.Vertical == VerticalAlignment.Top, icon: "align-vertical-justify-start"),
        new("format.alignMiddle", "Align Middle (Vertical)", "Centre contents between the top and bottom of the cell.", null,
            () => SetVerticalAlignment(e, VerticalAlignment.Middle),
            isActive: () => ActiveStyle(e).Alignment.Vertical == VerticalAlignment.Middle, icon: "align-vertical-justify-center"),
        new("format.alignBottom", "Align Bottom (Vertical)", "Sit contents at the bottom of the cell.", null, () => SetVerticalAlignment(e, VerticalAlignment.Bottom),
            isActive: () => ActiveStyle(e).Alignment.Vertical == VerticalAlignment.Bottom, icon: "align-vertical-justify-end"),
        new("format.wrap", "Wrap Text", "Wrap long text onto multiple lines.", null, () => ToggleWrap(e),
            isActive: () => ActiveStyle(e).Alignment.WrapText, icon: "wrap-text"),
        new("format.currency", "Currency", "Format the number as currency.", null, () => ToggleNumberFormat(e, CurrencyFormat, NumberFormatKind.Currency),
            canExecute: () => IsNumericContext(e), isActive: () => ActiveStyle(e).NumberFormat.Kind == NumberFormatKind.Currency, icon: "dollar-sign"),
        new("format.percent", "Percent", "Format the number as a percentage.", null, () => ToggleNumberFormat(e, NumberFormat.Percent, NumberFormatKind.Percent),
            canExecute: () => IsNumericContext(e), isActive: () => ActiveStyle(e).NumberFormat.Kind == NumberFormatKind.Percent, icon: "percent"),
        new("format.date", "Date", "Format the number as a date.", null, () => ToggleNumberFormat(e, DateFormat, NumberFormatKind.Date),
            canExecute: () => IsNumericContext(e), isActive: () => ActiveStyle(e).NumberFormat.Kind == NumberFormatKind.Date, icon: "calendar"),
        new("format.increaseDecimal", "Increase Decimal", "Show one more decimal place.", null,
            () => StepDecimals(e, +1), canExecute: () => CanStepDecimals(e), icon: "move-decimal-left"),
        new("format.decreaseDecimal", "Decrease Decimal", "Show one fewer decimal place.", null,
            () => StepDecimals(e, -1), canExecute: () => CanStepDecimals(e), icon: "move-decimal-right"),
        new("format.cells", "Format Cells…", "Open the full cell-formatting dialog.", Shortcut.Cmd(Key.D1), () => e.ShowFormatCellsDialog(), icon: "settings-2"),

        // Format ▸ Rows / Columns (PRD §6.2, §8.14) — the line-level commands, which are also what a
        // row or column header's context menu is for. Each is disabled rather than hidden when the
        // selection spans every line of its axis (a whole-column selection selects every row): sizing
        // or hiding a million rows is never what was meant, and the same rule already guards Delete Rows.
        new("format.rowHeight", "Row Height…", "Set an exact height for the selected rows.", null, () => e.ShowRowHeightDialog(),
            canExecute: () => !SpansEveryRow(e), icon: "move-vertical"),
        new("format.hideRows", "Hide Rows", "Hide the selected rows; their data and formulas stay.",
            Shortcut.Bare(Key.D9, KeyModifiers.Control), () => HideLines(e, rows: true),
            canExecute: () => !SpansEveryRow(e), icon: "eye-off"),
        new("format.unhideRows", "Unhide Rows", "Show the hidden rows in or beside the selection.",
            Shortcut.Bare(Key.D9, KeyModifiers.Control | KeyModifiers.Shift), () => UnhideLines(e, rows: true),
            canExecute: () => RowsToUnhide(e).Count > 0, icon: "eye"),
        new("format.columnWidth", "Column Width…", "Set an exact width for the selected columns.", null, () => e.ShowColumnWidthDialog(),
            canExecute: () => !SpansEveryColumn(e), icon: "move-horizontal"),
        new("format.hideColumns", "Hide Columns", "Hide the selected columns; their data and formulas stay.",
            Shortcut.Bare(Key.D0, KeyModifiers.Control), () => HideLines(e, rows: false),
            canExecute: () => !SpansEveryColumn(e), icon: "eye-off"),
        new("format.unhideColumns", "Unhide Columns", "Show the hidden columns in or beside the selection.",
            Shortcut.Bare(Key.D0, KeyModifiers.Control | KeyModifiers.Shift), () => UnhideLines(e, rows: false),
            canExecute: () => ColumnsToUnhide(e).Count > 0, icon: "eye"),

        // "Format Selection As: <data-type>" — one entry per data type, surfaced in the Command Palette
        // (PRD §6.7). Each sets a specific number format outright (vs. the command-bar toggles above).
        new("format.asAutomatic", "Format Selection As: Automatic", "Use the general (automatic) number format.",
            null, () => FormatSelectionAs(e, NumberFormat.General), icon: "eraser"),
        new("format.asNumber", "Format Selection As: Number", "Show as a number with thousands separators and two decimals.",
            null, () => FormatSelectionAs(e, NumberFormatPreset), canExecute: () => IsNumericContext(e), icon: "a-large-small"),
        new("format.asCurrency", "Format Selection As: Currency", "Show as currency.",
            null, () => FormatSelectionAs(e, CurrencyFormat), canExecute: () => IsNumericContext(e), icon: "dollar-sign"),
        new("format.asPercent", "Format Selection As: Percent", "Show as a percentage with two decimals.",
            null, () => FormatSelectionAs(e, PercentFormatPreset), canExecute: () => IsNumericContext(e), icon: "percent"),
        new("format.asScientific", "Format Selection As: Scientific", "Show in scientific (exponent) notation.",
            null, () => FormatSelectionAs(e, ScientificFormatPreset), canExecute: () => IsNumericContext(e), icon: "sigma"),
        new("format.asDate", "Format Selection As: Date", "Show as a date (YYYY-MM-DD).",
            null, () => FormatSelectionAs(e, DateFormat), canExecute: () => IsNumericContext(e), icon: "calendar"),
        new("format.asTime", "Format Selection As: Time", "Show as a time (HH:MM:SS).",
            null, () => FormatSelectionAs(e, TimeFormatPreset), canExecute: () => IsNumericContext(e), icon: "calendar"),
        new("format.asText", "Format Selection As: Text", "Treat the cell contents as plain text.",
            null, () => FormatSelectionAs(e, NumberFormat.Text), icon: "type"),

        // Borders — quick presets applied via the standard ApplyStyle path. The dropdown lives on the
        // command bar (see CommandBarModel "Border" group); Format Cells… covers per-edge control.
        new("format.borderAll", "All Borders", "Draw thin borders on every side of each selected cell.",
            null, () => ApplyBorderToEach(e, "All Borders", _ => Border.All(BorderLineStyle.Thin)), icon: "grid-2x2"),
        new("format.borderOutline", "Outer Border", "Draw a thin border around the outside of the selection.",
            null, () => ApplyOutline(e), icon: "square"),
        new("format.borderBottom", "Bottom Border", "Draw a thin border along the bottom of each selected cell.",
            null, () => ApplyBorderToEach(e, "Bottom Border", b => b with { Bottom = ThinEdge }), icon: "square"),
        new("format.borderTop", "Top Border", "Draw a thin border along the top of each selected cell.",
            null, () => ApplyBorderToEach(e, "Top Border", b => b with { Top = ThinEdge }), icon: "square"),
        new("format.borderLeft", "Left Border", "Draw a thin border along the left of each selected cell.",
            null, () => ApplyBorderToEach(e, "Left Border", b => b with { Left = ThinEdge }), icon: "square"),
        new("format.borderRight", "Right Border", "Draw a thin border along the right of each selected cell.",
            null, () => ApplyBorderToEach(e, "Right Border", b => b with { Right = ThinEdge }), icon: "square"),
        new("format.borderNone", "No Border", "Remove every border from the selected cells.",
            null, () => ApplyBorderToEach(e, "No Border", _ => Border.None), icon: "square-x"),

        // Insert
        new("insert.rowsAbove", "Rows Above", "Insert rows above the selection.", null, () => InsertRows(e, above: true), icon: "rows-plus"),
        new("insert.columnsLeft", "Columns Left", "Insert columns left of the selection.", null, () => InsertColumns(e, before: true), icon: "columns-plus"),
        new("edit.deleteRows", "Delete Rows", "Delete the selected rows.", null, () => DeleteRows(e), icon: "trash-2"),
        new("edit.deleteColumns", "Delete Columns", "Delete the selected columns.", null, () => DeleteColumns(e), icon: "trash-2"),
        new("insert.sheet", "Sheet", "Add a new worksheet.", null, () => e.AddSheet(), icon: "plus"),
        new("insert.chartSheet", "Chart Sheet", "Add a sheet that shows a chart of your data.", null, () => e.AddChartSheet(), icon: "chart-column"),
        new("insert.function", "Function…", "Browse the function library and insert one into the active cell.",
            Shortcut.Bare(Key.F, KeyModifiers.Control | KeyModifiers.Alt), () => e.ShowFunctionPicker(), icon: "sigma"),
        new("insert.namedRange", "Named Range…", "Name the current selection, or manage existing named ranges.",
            null, () => e.ShowNamedRangeManager(), icon: "tag"),
        new("insert.hyperlink", "Hyperlink…", "Add, edit, or remove a hyperlink on the active cell.",
            Shortcut.Cmd(Key.K, KeyModifiers.Alt), () => e.ShowHyperlinkDialog(), icon: "link"),
        new("sheet.rename", "Rename Sheet…", "Rename the active worksheet.", null, () => e.RenameActiveSheet(), icon: "pencil"),
        new("sheet.duplicate", "Duplicate Sheet", "Insert a copy of the active worksheet.", null, () => e.DuplicateActiveSheet(), icon: "copy"),
        new("sheet.delete", "Delete Sheet", "Delete the active worksheet.", null, () => e.DeleteActiveSheet(),
            canExecute: () => e.Session.Workbook.Worksheets.Count > 1, icon: "trash-2"),

        // Data
        new("data.sortAsc", "Sort Ascending", "Sort the column A→Z.", null, () => Sort(e, ascending: true), icon: "sort-asc"),
        new("data.sortDesc", "Sort Descending", "Sort the column Z→A.", null, () => Sort(e, ascending: false), icon: "sort-desc"),
        new("data.customSort", "Sort…", "Sort by one or more columns.", null, () => e.ShowSortDialog(), icon: "sort"),
        new("data.sortAndFilter", "Sort & Filter…", "Build an ordered list of sort and filter steps over a data region.",
            Shortcut.Cmd(Key.F, KeyModifiers.Shift), () => e.ShowSortFilterDialog(), icon: "filter"),
        new("data.toggleFilter", "Filter", "Filter rows by a rule.", null, () => e.ToggleFilter(),
            isActive: () => e.Session.ActiveSheet.ActiveFilter is not null, icon: "filter"),
        new("data.clearFilter", "Clear Filter", "Remove the active filter.", null, () => ClearFilter(e), icon: "filter-x"),
        new("data.createTable", "Table", "Turn the range into a table.", Shortcut.Cmd(Key.T), () => CreateTable(e), icon: "table"),
        new("data.headerColumn", "Header Column", "Style the table's first column like its header row.",
            null, () => e.ToggleTableHeaderColumn(), canExecute: () => e.ActiveTable is not null,
            isActive: () => e.ActiveTable?.HasHeaderColumn ?? false, icon: "columns"),
        new("data.autofitColumns", "Auto-fit Columns", "Size the table's columns to their contents.",
            null, () => AutofitColumns(e), icon: "arrow-right-to-line"),
        new("data.removeDuplicates", "Remove Duplicates…", "Drop repeated rows from the range.", null, () => e.ShowRemoveDuplicatesDialog(), icon: "list-x"),
        new("data.textToColumns", "Text to Columns…", "Split a column on a delimiter.", null, () => e.ShowTextToColumnsDialog(), icon: "columns"),
        new("data.validation", "Data Validation…", "Restrict what can be entered.", null, () => e.ShowDataValidationDialog(), icon: "square-check"),

        // Formulas
        new("formula.recalc", "Recalculate", "Recompute every formula now.", Shortcut.Bare(Key.F9), () => e.Session.RecalculateAll(), icon: "refresh-cw"),

        // View
        new("view.commandMenu", "Command Menu…", "Search and run any command.", Shortcut.Cmd(Key.K), () => e.ShowCommandPalette(), icon: "command"),
        new("view.goToSheet", "Go to Sheet…", "Jump to another worksheet.", Shortcut.Cmd(Key.P, KeyModifiers.Shift), () => e.ShowSheetSwitcher(), icon: "list"),
        .. ZoomCommands.Build(() => e.Zoom, zoom => e.Zoom = zoom),

        // The command bar's three states, one command each rather than one cycling toggle: a palette is a
        // list of destinations, and "Hide Command Bar" says where you land where "Toggle" doesn't. Each is
        // lit while it is the current state, so the palette also *reports* which one you're in.
        new("view.commandBarVisible", "Show Command Bar", "Show the full command bar.", null,
            () => e.CommandBarVisibility = CommandBarVisibility.Visible,
            isActive: () => e.CommandBarVisibility == CommandBarVisibility.Visible, icon: "panel-top"),
        new("view.commandBarCollapsed", "Collapse Command Bar", "Fold the command bar to its slim handle.", null,
            () => e.CommandBarVisibility = CommandBarVisibility.Collapsed,
            isActive: () => e.CommandBarVisibility == CommandBarVisibility.Collapsed, icon: "chevrons-up"),
        new("view.commandBarHidden", "Hide Command Bar", "Hide the command bar entirely, giving its row to the grid.", null,
            () => e.CommandBarVisibility = CommandBarVisibility.Hidden,
            isActive: () => e.CommandBarVisibility == CommandBarVisibility.Hidden, icon: "x"),
        new("view.freezePanes", "Freeze Panes", "Keep header rows and columns in view.", null, () => e.ToggleFreezePanes(), icon: "layout-grid"),
        new("view.radialSettings", "Radial Menu Settings…", "Configure the hold-right-click radial command menu.", null, () => e.ShowRadialSettings(), icon: "settings-2"),
        new("app.preferences", "Preferences…", "Zoom, the command bar, and the radial menu.", Shortcut.Cmd(Key.OemComma), () => e.ShowPreferences(), icon: "settings-2"),

        // Insert (merge)
        new("format.merge", "Merge Cells", "Combine the selected cells into one.", null, () => MergeCells(e),
            isActive: () => e.Session.ActiveSheet.FindMerge(e.Selection.ActiveRow, e.Selection.ActiveColumn) is not null, icon: "table-cells-merge"),

        // Help
        // ⌘? (⌘⇧/) opens the shortcuts reference. It carries the command modifier because "?" is a character
        // people type into cells: a bare "?" from the grid opened this dialog instead of starting an edit,
        // and no focus gate can fix that — the grid is exactly where you'd be typing it.
        new("help.shortcuts", "Keyboard Shortcuts", "List every keyboard shortcut.", Shortcut.Cmd(Key.OemQuestion, KeyModifiers.Shift),
            () => e.ShowKeyboardShortcuts(), icon: "keyboard"),
        new("help.about", "About Cellestra", "About Cellestra — version, pronunciation, and license.", null, () => e.ShowAbout(), icon: "info"),
    ];

    // Number-format codes the bar's one-tap toggles apply. They classify (NumberFormatParser) as
    // Currency/Date so the toggle can tell when a cell already has that format and revert it.
    private static readonly NumberFormat CurrencyFormat = new("$#,##0.00");
    private static readonly NumberFormat DateFormat = new("yyyy-mm-dd");

    // Presets behind the palette's "Format Selection As: …" commands — one per user-facing data type.
    private static readonly NumberFormat NumberFormatPreset = new("#,##0.00");
    private static readonly NumberFormat PercentFormatPreset = new("0.00%");
    private static readonly NumberFormat ScientificFormatPreset = new("0.00E+00");
    private static readonly NumberFormat TimeFormatPreset = new("hh:mm:ss");

    /// <summary>
    /// Merge Cells, which is a toggle: run on a merge, it unmerges.
    /// <para>
    /// Clicking a merged cell selects the <em>whole</em> merge (see <c>SelectCellOrMerge</c>), so the
    /// selection there is a multi-cell range — which used to take the merge branch and ask
    /// <c>AddMerge</c> to overlap a region that already existed, whose refusal is an exception nothing
    /// caught. So "the selection is exactly one existing merge" is the shape of a click on a merged
    /// cell, and it means unmerge.
    /// </para>
    /// <para>
    /// A selection that merely <em>spans</em> merges is a genuine merge of a bigger block: it absorbs
    /// them, unmerging each before merging the whole, as one undoable step. That is what every
    /// spreadsheet does, and it is the only reading that doesn't leave the command dead on a selection
    /// that happens to touch a merge.
    /// </para>
    /// </summary>
    private static void MergeCells(IAppEnvironment e)
    {
        var session = e.Session;
        var sheet = session.ActiveSheet;
        var range = e.Selection.Range;
        var overlapping = sheet.MergedRegions.Where(m => m.Range.Intersects(range)).ToList();

        if (overlapping.Count == 1 && (range.IsSingleCell || overlapping[0].Range == range))
        {
            var merge = overlapping[0].Range;
            session.Execute(EditCommands.UnmergeCells(sheet.Name, merge.Top, merge.Left));
            e.Refresh();
            return;
        }

        if (range.IsSingleCell)
        {
            return; // one cell, not in a merge — there is nothing to combine
        }

        var parts = new List<IEditCommand>();
        parts.AddRange(overlapping.Select(m => EditCommands.UnmergeCells(sheet.Name, m.Range.Top, m.Range.Left)));
        parts.Add(EditCommands.MergeCells(sheet.Name, range));
        session.Execute(parts.Count == 1 ? parts[0] : EditCommands.Composite("Merge Cells", parts));
        e.Refresh();
    }

    /// <summary>
    /// Format Painter (two-step): with no stamp held, capture the active cell's formatting (its
    /// single cell, or the whole selection so a multi-cell pattern is reused) and arm the painter so
    /// the grid shows a paintbrush cursor and lets you click/drag to paint; with a stamp already
    /// held, paint it onto the current selection — including replicating merge-cells — then disarm.
    /// The stamp lives on the session so the grid's drag-to-paint shares it (see <see cref="Services.WorkbookSession"/>).
    /// </summary>
    private static void FormatPainter(IAppEnvironment e)
    {
        var session = e.Session;
        var sheet = session.ActiveSheet;

        if (!session.IsFormatPainterArmed)
        {
            var source = e.Selection.IsSingleCell
                ? new Cellestra.Core.Model.CellRange(e.Selection.ActiveAddress, e.Selection.ActiveAddress)
                : e.Selection.Range;
            session.ArmFormatPainter(Cellestra.Core.Editing.FormatBlock.Capture(session.Workbook, sheet, source));
            e.Narrate("Format Painter: click or drag over cells to paint the copied formatting.");
            e.Refresh();
            return;
        }

        session.Execute(session.FormatStamp!.ApplyTo(sheet.Name, e.Selection.Range));
        session.DisarmFormatPainter();
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
        string sheet = session.ActiveSheet.Name;
        var range = e.Selection.Range;
        // ClearAll wipes the cells; ClearRangeStructure adds the sheet-level structure overlapping the
        // selection that cells don't carry — tables (filter dropdowns + header fill), the AutoFilter,
        // merges, validations, and whole-row/column fills — so "Clear All" truly clears it all.
        session.Execute(EditCommands.Composite("Clear All",
        [
            EditCommands.ClearAll(sheet, range),
            DataEditCommands.ClearRangeStructure(sheet, range),
        ]));
        e.Refresh();
    }

    private static void SelectAll(IAppEnvironment e)
    {
        // Expanding Select-All (#26 / PRD §10.1): the first press selects the contiguous data
        // region around the active cell; a second press (selection already at that region) widens
        // to the whole sheet. The expansion decision is a pure, unit-tested helper in the grid.
        var selection = e.Selection;
        var next = Controls.SpreadsheetGrid.SelectAllExpander.Next(
            e.Session.ActiveSheet, selection.Range, selection.ActiveRow, selection.ActiveColumn);
        selection.SetRange(next.Top, next.Left, next.Bottom, next.Right);
        e.Refresh();
    }

    /// <summary>
    /// AutoSum: propose <c>=SUM(...)</c> over the contiguous numbers above (or, failing that, left of)
    /// the active cell — or, for a multi-cell selection of numbers, one SUM per column in the row
    /// below. The range heuristic is the pure, unit-tested <see cref="AutoSumPlanner"/>; each proposal
    /// commits through the canonical <see cref="EditCommands.SetInput(Workbook,string,int,int,string,System.Globalization.CultureInfo)"/>
    /// OpenFormula path, all in one undoable step. After committing a single-cell sum the selection
    /// moves to that cell so the user can confirm/edit it.
    /// </summary>
    private static void AutoSum(IAppEnvironment e)
    {
        var session = e.Session;
        var sheet = session.ActiveSheet;
        var proposals = AutoSumPlanner.Plan(sheet, e.Selection.Range, e.Selection.ActiveRow, e.Selection.ActiveColumn);
        if (proposals.Count == 0)
        {
            e.Narrate("Nothing to AutoSum — no numbers above or to the left.");
            return;
        }

        var culture = session.Workbook.Settings.EffectiveLocale;
        var commands = proposals
            .Select(p => EditCommands.SetInput(session.Workbook, sheet.Name, p.TargetRow, p.TargetColumn, p.Formula, culture))
            .ToList();
        session.Execute(commands.Count == 1 ? commands[0] : EditCommands.Composite("AutoSum", commands));

        // Land on the (first) cell we wrote so the result/formula is visible and editable.
        var first = proposals[0];
        e.Selection.MoveTo(first.TargetRow, first.TargetColumn);
        e.RevealActiveCell();
        e.Refresh();
    }

    private static void FillDown(IAppEnvironment e) => Fill(e, down: true);

    private static void FillRight(IAppEnvironment e) => Fill(e, down: false);

    /// <summary>
    /// Fill Down/Right (PRD §8.15) over the existing <see cref="FillCommand"/>: Fill Down copies the
    /// selection's top row down through the rest; Fill Right copies the left column rightward. A
    /// single-cell selection has nothing to fill into, so it no-ops with a hint.
    /// </summary>
    private static void Fill(IAppEnvironment e, bool down)
    {
        var selection = e.Selection.Range;
        if (selection.IsSingleCell)
        {
            e.Narrate(down ? "Select cells below to fill into." : "Select cells to the right to fill into.");
            return;
        }

        var source = down
            ? new Cellestra.Core.Model.CellRange(
                new Cellestra.Core.Model.CellAddress(selection.Left, selection.Top),
                new Cellestra.Core.Model.CellAddress(selection.Right, selection.Top))
            : new Cellestra.Core.Model.CellRange(
                new Cellestra.Core.Model.CellAddress(selection.Left, selection.Top),
                new Cellestra.Core.Model.CellAddress(selection.Left, selection.Bottom));

        e.Session.Execute(new FillCommand(e.Session.ActiveSheet.Name, source, selection));
        e.Refresh();
    }

    private static void ToggleFont(IAppEnvironment e, Func<Font, bool> get, Func<Font, bool, Font> set)
    {
        var session = e.Session;
        var sheet = session.ActiveSheet;
        var style = session.Workbook.Styles.Get(sheet.EffectiveStyleId(e.Selection.ActiveRow, e.Selection.ActiveColumn));
        bool target = !get(style.Font);

        session.Execute(EditCommands.ApplyStyle(session.Workbook, sheet.Name, e.Selection.Range,
            s => s.WithFont(set(s.Font, target)), "Format"));
        e.Refresh();
    }

    /// <summary>The cell style of the active cell — the basis for "is this toggle already on?" checks.</summary>
    private static CellStyle ActiveStyle(IAppEnvironment e)
    {
        var sheet = e.Session.ActiveSheet;
        return e.Session.Workbook.Styles.Get(sheet.EffectiveStyleId(e.Selection.ActiveRow, e.Selection.ActiveColumn));
    }

    // ---- Borders ----------------------------------------------------------

    private static readonly BorderEdge ThinEdge = BorderEdge.Of(BorderLineStyle.Thin);

    /// <summary>
    /// Apply a per-cell border transform to every cell in the selection as one undoable edit. The
    /// transform takes each cell's current border so additive presets (e.g. "Bottom Border") keep
    /// the other edges; "All"/"None" simply ignore the input.
    /// </summary>
    private static void ApplyBorderToEach(IAppEnvironment e, string label, Func<Border, Border> transform)
    {
        var session = e.Session;
        session.Execute(EditCommands.ApplyStyle(session.Workbook, session.ActiveSheet.Name, e.Selection.Range,
            s => s.WithBorder(transform(s.Border)), label));
        e.Refresh();
    }

    /// <summary>
    /// Draw a thin border around the OUTSIDE of the whole selection: only the perimeter cells gain
    /// the outermost edge (top row → top edge, left column → left edge, …), so the result reads as a
    /// single box, not a border on every cell. A single cell gets a full box. Built as one undoable
    /// composite of the four perimeter strips, each adding its edge while preserving the others.
    /// </summary>
    private static void ApplyOutline(IAppEnvironment e)
    {
        var session = e.Session;
        string sheet = session.ActiveSheet.Name;
        var range = e.Selection.Range;
        int top = range.Top, bottom = range.Bottom, left = range.Left, right = range.Right;
        var wb = session.Workbook;

        var topStrip = new Cellestra.Core.Model.CellRange(new Cellestra.Core.Model.CellAddress(left, top), new Cellestra.Core.Model.CellAddress(right, top));
        var bottomStrip = new Cellestra.Core.Model.CellRange(new Cellestra.Core.Model.CellAddress(left, bottom), new Cellestra.Core.Model.CellAddress(right, bottom));
        var leftStrip = new Cellestra.Core.Model.CellRange(new Cellestra.Core.Model.CellAddress(left, top), new Cellestra.Core.Model.CellAddress(left, bottom));
        var rightStrip = new Cellestra.Core.Model.CellRange(new Cellestra.Core.Model.CellAddress(right, top), new Cellestra.Core.Model.CellAddress(right, bottom));

        var parts = new List<IEditCommand>
        {
            EditCommands.ApplyStyle(wb, sheet, topStrip, s => s.WithBorder(s.Border with { Top = ThinEdge }), "Outer Border"),
            EditCommands.ApplyStyle(wb, sheet, bottomStrip, s => s.WithBorder(s.Border with { Bottom = ThinEdge }), "Outer Border"),
            EditCommands.ApplyStyle(wb, sheet, leftStrip, s => s.WithBorder(s.Border with { Left = ThinEdge }), "Outer Border"),
            EditCommands.ApplyStyle(wb, sheet, rightStrip, s => s.WithBorder(s.Border with { Right = ThinEdge }), "Outer Border"),
        };

        session.Execute(EditCommands.Composite("Outer Border", parts));
        e.Refresh();
    }

    private static void SetAlignment(IAppEnvironment e, HorizontalAlignment horizontal)
    {
        var session = e.Session;
        session.Execute(EditCommands.ApplyStyle(session.Workbook, session.ActiveSheet.Name, e.Selection.Range,
            s => s.WithAlignment(s.Alignment with { Horizontal = horizontal }), "Align"));
        e.Refresh();
    }

    private static void SetVerticalAlignment(IAppEnvironment e, VerticalAlignment vertical)
    {
        var session = e.Session;
        session.Execute(EditCommands.ApplyStyle(session.Workbook, session.ActiveSheet.Name, e.Selection.Range,
            s => s.WithAlignment(s.Alignment with { Vertical = vertical }), "Vertical align"));
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
    /// Apply (not toggle) <paramref name="format"/> to the whole selection — backs the palette's
    /// "Format Selection As: …" commands, which set a specific data type outright rather than
    /// flipping it on/off like the command-bar one-tap toggles.
    /// </summary>
    private static void FormatSelectionAs(IAppEnvironment e, NumberFormat format)
    {
        var session = e.Session;
        session.Execute(EditCommands.ApplyStyle(session.Workbook, session.ActiveSheet.Name, e.Selection.Range,
            s => s.WithNumberFormat(format), "Number Format"));
        e.Refresh();
        // A freshly applied format can be wider than the column — grow it so the result shows, not #####.
        e.AutofitColumns(e.Selection.Left, e.Selection.Right);
    }

    /// <summary>
    /// Step the active cells' number-format precision by <paramref name="delta"/> decimal places
    /// (Increase/Decrease Decimal). Each cell's own code is adjusted via the pure
    /// <see cref="NumberFormatCode"/> helper so a currency, percent, or scientific format keeps its
    /// shape and only its fraction-digit count changes; date/time/text codes are left untouched.
    /// </summary>
    private static void StepDecimals(IAppEnvironment e, int delta)
    {
        var session = e.Session;
        session.Execute(EditCommands.ApplyStyle(session.Workbook, session.ActiveSheet.Name, e.Selection.Range,
            s => s.WithNumberFormat(new NumberFormat(NumberFormatCode.AdjustDecimals(s.NumberFormat.Code, delta))),
            delta > 0 ? "Increase Decimal" : "Decrease Decimal"));
        e.Refresh();

        // More decimals can overflow the column width; grow it so the result shows, not #####.
        if (delta > 0)
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

    /// <summary>
    /// True when Increase/Decrease Decimal can do something: the cell is numeric context AND its
    /// number-format kind actually carries decimal places. Date/Time/DateTime and Text codes have no
    /// fraction digits to step (AdjustDecimals leaves them untouched), so the tools would silently
    /// no-op — disable them there instead.
    /// </summary>
    private static bool CanStepDecimals(IAppEnvironment e)
    {
        if (!IsNumericContext(e))
        {
            return false;
        }

        return ActiveStyle(e).NumberFormat.Kind is not (
            NumberFormatKind.Date or NumberFormatKind.Time or NumberFormatKind.DateTime or NumberFormatKind.Text);
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

    // ---- Rows & columns: hide / unhide ------------------------------------

    /// <summary>True when the selection covers every row of the sheet (what a column header selects).</summary>
    private static bool SpansEveryRow(IAppEnvironment e) =>
        e.Selection.Bottom - e.Selection.Top + 1 >= Cellestra.Core.Model.SheetDimensions.MaxRows;

    /// <summary>True when the selection covers every column of the sheet (what a row header selects).</summary>
    private static bool SpansEveryColumn(IAppEnvironment e) =>
        e.Selection.Right - e.Selection.Left + 1 >= Cellestra.Core.Model.SheetDimensions.MaxColumns;

    private static IReadOnlyList<(int First, int Last)> RowsToUnhide(IAppEnvironment e) =>
        HiddenLines.RowsToUnhide(e.Session.ActiveSheet, e.Selection.Top, e.Selection.Bottom);

    private static IReadOnlyList<(int First, int Last)> ColumnsToUnhide(IAppEnvironment e) =>
        HiddenLines.ColumnsToUnhide(e.Session.ActiveSheet, e.Selection.Left, e.Selection.Right);

    /// <summary>
    /// Hide the selected rows (or columns) — ODF visibility only, so the data and formulas are
    /// untouched (PRD §8.14). Hidden lines leave nothing on screen to click, so the narration names
    /// the way back rather than leaving the user to find it.
    /// </summary>
    private static void HideLines(IAppEnvironment e, bool rows)
    {
        if (rows ? SpansEveryRow(e) : SpansEveryColumn(e))
        {
            return;
        }

        string sheet = e.Session.ActiveSheet.Name;
        var selection = e.Selection;
        int count = rows ? selection.Bottom - selection.Top + 1 : selection.Right - selection.Left + 1;
        e.Session.Execute(rows
            ? EditCommands.SetRowsHidden(sheet, selection.Top, selection.Bottom, true)
            : EditCommands.SetColumnsHidden(sheet, selection.Left, selection.Right, true));
        e.Refresh();
        e.Narrate(rows
            ? $"Hid {Plural(count, "row")} — ⌃⇧9 unhides them."
            : $"Hid {Plural(count, "column")} — ⌃⇧0 unhides them.");
    }

    /// <summary>
    /// Unhide the hidden rows (or columns) the selection reaches — see <see cref="HiddenLines"/> for
    /// which those are. One undoable step, and only the hidden runs are touched, so unhiding from a
    /// select-all costs what is hidden rather than what is selected.
    /// </summary>
    private static void UnhideLines(IAppEnvironment e, bool rows)
    {
        var runs = rows ? RowsToUnhide(e) : ColumnsToUnhide(e);
        if (runs.Count == 0)
        {
            e.Narrate(rows ? "No hidden rows next to the selection." : "No hidden columns next to the selection.");
            return;
        }

        string sheet = e.Session.ActiveSheet.Name;
        string label = rows ? "Unhide Rows" : "Unhide Columns";
        var parts = runs
            .Select(run => rows
                ? EditCommands.SetRowsHidden(sheet, run.First, run.Last, false)
                : EditCommands.SetColumnsHidden(sheet, run.First, run.Last, false))
            .ToList();

        e.Session.Execute(parts.Count == 1 ? parts[0] : EditCommands.Composite(label, parts));
        e.Refresh();
        int count = runs.Sum(run => run.Last - run.First + 1);
        e.Narrate($"Unhid {Plural(count, rows ? "row" : "column")}");
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    // ---- Data -------------------------------------------------------------

    /// <summary>The data region a data command acts on: the selection, or the current region when one cell is selected.</summary>
    public static (Cellestra.Core.Model.Worksheet Sheet, Cellestra.Core.Model.CellRange Range, bool Header) DataTarget(IAppEnvironment e)
    {
        var sheet = e.Session.ActiveSheet;

        // Inside a table, data commands act on the whole table (its range + header), so sort/filter
        // target — and the dialog reads — the table's own state rather than a guessed current region.
        if (sheet.FindTable(e.Selection.ActiveRow, e.Selection.ActiveColumn) is { } table)
        {
            return (sheet, table.Range, table.HasHeader);
        }

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

        // The command only re-shows the rows; the database-range's filter/sort criteria still linger
        // on the sheet, so they re-serialize and resurrect the filter on reopen. Drop that state too.
        sheet.ActiveFilterSpec = null;
        sheet.ActiveSort = null;
        e.Refresh();
    }

    /// <summary>
    /// Size columns to their contents. Inside a table the target is the whole table — that is the shape
    /// the user declared, and fitting only the one selected column of it leaves the table looking ragged
    /// for no reason. Outside one, it's the selected columns, the way the header double-click acts.
    /// </summary>
    private static void AutofitColumns(IAppEnvironment e)
    {
        if (e.ActiveTable is { } table)
        {
            e.FitColumns(table.Range.Left, table.Range.Right);
            return;
        }

        e.FitColumns(e.Selection.Left, e.Selection.Right);
    }

    private static void CreateTable(IAppEnvironment e)
    {
        var (sheet, range, _) = DataTarget(e);

        // A new table always has a header row (Excel's ⌘T behaviour): we label any empty header
        // cells "Column N" and open the first for editing below (#2), so creation never relies on the
        // header-detection heuristic the way Sort/Filter do.
        const bool header = true;

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

        // A new headered table starts ready to name: jump to the first header cell (now "Column 1")
        // and open it with the placeholder selected so the user types straight over it (#2).
        if (header)
        {
            e.Selection.MoveTo(range.Top, range.Left);
            e.RevealActiveCell();
            e.BeginEditSelectAll();
        }
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
