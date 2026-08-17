using System.Globalization;
using Avalonia.Controls;
using Cellestra.Core;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views.Dialogs;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Overlays;

namespace Cellestra.Editor;

public partial class SpreadsheetEditorView
{
    /// <summary>
    /// The keyboard palette (⌘K): the same commands the search pill drops, but opened centred over the window —
    /// a shortcut is fired from a neutral gaze, so the list meets the eye in the middle rather than off in a corner.
    /// </summary>
    public void ShowCommandPalette() =>
        ShowPalette(PaletteCatalog.Placeholder, PaletteCatalog.Entries(Commands), Commands.RecentTitles);

    /// <summary>Build a right-click menu from catalog command ids (null = separator); each row shows its icon, label, and shortcut.</summary>
    private MenuFlyout BuildCommandFlyout(params string?[] ids)
    {
        var flyout = new MenuFlyout();
        foreach (string? id in ids)
        {
            if (id is null)
            {
                flyout.Items.Add(new Separator());
                continue;
            }

            if (Commands.ById(id) is { } command)
            {
                flyout.Items.Add(MenuRow(command));
            }
        }

        return flyout;
    }

    /// <summary>
    /// One context-menu row for a catalog command: icon, title, shortcut, and its enabled state. A
    /// command that reports a toggle state (Merge Cells is lit while the active cell is in a merge, the
    /// way the command bar shows it) renders as a checkbox, so the menu answers "is this already on?"
    /// rather than only offering the flip.
    /// </summary>
    private static MenuItem MenuRow(AppCommand command)
    {
        var row = new MenuItem
        {
            Header = command.Title,
            InputGesture = command.Gesture,
            Icon = command.Icon is null ? null : new LucideIcon { IconKey = command.Icon, Size = 15 },
            Command = new RelayCommand(() => command.Execute(), () => command.IsEnabled),
        };

        if (command.IsActive is not null)
        {
            row.ToggleType = MenuItemToggleType.CheckBox;
            row.IsChecked = command.IsToggleActive;
        }

        return row;
    }

    private void OnGridContextRequested(object? sender, Avalonia.Input.ContextRequestedEventArgs e)
    {
        // Pointer-driven context menus are owned by the grid's right-click gesture: a quick click shows
        // this menu (via ContextMenuRequested), a press-and-hold shows the radial. Only the keyboard
        // menu key reaches here without a position — show the cell menu at the active cell for it.
        if (e.TryGetPosition(Grid, out _))
        {
            return;
        }

        ShowCellContextMenu(SpreadsheetGrid.ContextTarget.Cell);
        e.Handled = true;
    }

    /// <summary>Quick right-click (no hold): settle the selection under the point and show its context menu.</summary>
    private void ShowGridContextMenuAt(Avalonia.Point point) => ShowCellContextMenu(Grid.SelectForContext(point));

    private void ShowCellContextMenu(SpreadsheetGrid.ContextTarget target)
    {
        var ids = GridContextMenu.Ids(target);
        if (ids.Count == 0)
        {
            return; // right-clicked the header corner — nothing to offer
        }

        var flyout = BuildCommandFlyout([.. ids]);

        // Inside a table, offer the table-specific actions (task 5: designate a header column). The
        // command's IsActive shows a check when the first column is already a header column.
        if (target == SpreadsheetGrid.ContextTarget.Cell && ActiveTable is not null
            && Commands.ById("data.headerColumn") is { } headerColumn)
        {
            flyout.Items.Add(new Separator());

            // Right-clicking any cell in a table offers the Sort & Filter dialog for that table (#1).
            if (Commands.ById("data.sortAndFilter") is { } sortAndFilter)
            {
                flyout.Items.Add(MenuRow(sortAndFilter));
            }

            flyout.Items.Add(MenuRow(headerColumn));
        }

        flyout.ShowAt(Grid, showAtPointer: true);
    }

    // A hand-typed size is clamped to the same floor the drag-resize enforces, and to a ceiling that
    // keeps a typo (600000 for 600) from leaving one unusable line filling the window. Both are
    // undoable, so the ceiling is a guard rail, not a rule about what sizes are legitimate.
    private const double MaxLineSize = 2000;

    public void ShowRowHeightDialog() => _ = SetLineSizeAsync(rows: true);

    public void ShowColumnWidthDialog() => _ = SetLineSizeAsync(rows: false);

    /// <summary>
    /// Row Height… / Column Width… (PRD §6.2): prompt for an exact size in pixels — the unit the model
    /// keeps and the drag-resize reports — seeded from the first selected line, and apply it to every
    /// selected line as one undoable step.
    /// </summary>
    private async Task SetLineSizeAsync(bool rows)
    {
        var sheet = Session.ActiveSheet;
        var culture = Session.Workbook.Settings.EffectiveLocale;
        int first = rows ? Selection.Top : Selection.Left;
        int last = rows ? Selection.Bottom : Selection.Right;

        // The catalog disables both commands when the selection spans the whole axis; this is the same
        // rule where the method is reachable directly, so the loop below can never run a million times.
        if (last - first + 1 >= (rows ? SheetDimensions.MaxRows : SheetDimensions.MaxColumns))
        {
            return;
        }

        double current = rows ? sheet.GetRowHeight(first) : sheet.GetColumnWidth(first);
        string? entry = await ShowModalAsync(new TextPromptDialog(
            rows ? "Row height" : "Column width", "Pixels", current.ToString("0.##", culture)));
        if (entry is null)
        {
            return;
        }

        if (!double.TryParse(entry, NumberStyles.Float, culture, out double size))
        {
            double fallback = rows ? Defaults.RowHeight : Defaults.ColumnWidth;
            ShowMessage(rows ? "That isn't a height" : "That isn't a width",
                $"“{entry}” isn't a number. Enter a size in pixels — the default is {fallback.ToString("0.##", culture)}.");
            return;
        }

        size = Math.Clamp(size, rows ? SpreadsheetGrid.MinRowHeight : SpreadsheetGrid.MinColumnWidth, MaxLineSize);

        var parts = new List<IEditCommand>();
        for (int line = first; line <= last; line++)
        {
            parts.Add(rows
                ? EditCommands.SetRowHeight(sheet.Name, line, size)
                : EditCommands.SetColumnWidth(sheet.Name, line, size));
        }

        Session.Execute(parts.Count == 1 ? parts[0] : EditCommands.Composite(rows ? "Row Height" : "Column Width", parts));
        Refresh();
    }

    public void ToggleFilter()
    {
        var (sheet, range, _) = AppCommands.DataTarget(this);
        if (sheet.ActiveFilter is not null)
        {
            Session.Execute(DataEditCommands.ClearFilter(sheet.Name, sheet.ActiveFilter.Value));
            sheet.ActiveFilter = null;
            // Also drop the conditions and sort so they don't linger and resurrect when the filter
            // is turned back on.
            sheet.ActiveFilterSpec = null;
            sheet.ActiveSort = null;
        }
        else
        {
            sheet.ActiveFilter = range;
        }

        Refresh();
    }

    /// <summary>The table the active cell sits in, or null when it isn't inside one.</summary>
    public Cellestra.Core.Data.Table? ActiveTable =>
        Session.ActiveSheet.FindTable(Selection.ActiveRow, Selection.ActiveColumn);

    public void ToggleTableHeaderColumn()
    {
        if (ActiveTable is not { } table)
        {
            return;
        }

        Session.Execute(DataEditCommands.SetTableHeaderColumn(
            Session.Workbook, Session.ActiveSheet.Name, table, !table.HasHeaderColumn));
        Refresh();
        Narrate(table.HasHeaderColumn ? "Removed header column" : "Added header column");
    }

    public async void ShowSortDialog()
    {
        var (sheet, range, header) = AppCommands.DataTarget(this);
        var spec = await ShowModalAsync(new SortDialog(sheet, range, header));
        if (spec is { Levels.Count: > 0 })
        {
            Session.Execute(DataEditCommands.Sort(sheet.Name, range, spec));
            // Persist the sort so it round-trips like the dropdown quick-sort, and remember the sorted
            // range as the active filter region if one isn't already set.
            sheet.ActiveSort = spec;
            sheet.ActiveFilter ??= range;
            Refresh();
        }
    }

    public async void ShowSortFilterDialog()
    {
        var (sheet, range, header) = AppCommands.DataTarget(this);
        var culture = Session.Workbook.Settings.EffectiveLocale;
        var plan = await ShowModalAsync(new SortFilterDialog(sheet, range, header, culture));
        if (plan is null)
        {
            return;
        }

        // Route to the table's own state or the sheet AutoFilter, as one undo step (see SortFilterCommit).
        SortFilterCommit.Commit(Session, sheet, range, plan.Sort, plan.Filter, culture);
        Refresh();
    }

    public async void ShowRemoveDuplicatesDialog()
    {
        var (sheet, range, header) = AppCommands.DataTarget(this);
        var result = await ShowModalAsync(new RemoveDuplicatesDialog(sheet, range, header));
        if (result is not null)
        {
            Session.Execute(DataEditCommands.RemoveDuplicates(sheet.Name, range, result.KeyColumns, result.HasHeader, out var removedCount));
            Refresh();

            int removed = removedCount();
            int remaining = range.RowCount - (result.HasHeader ? 1 : 0) - removed;
            ShowMessage("Remove Duplicates", RemoveDuplicatesSummary(removed, remaining));
        }
    }

    /// <summary>The result line after Remove Duplicates: how many were removed and how many unique rows remain.</summary>
    internal static string RemoveDuplicatesSummary(int removed, int remaining) => removed == 0
        ? $"No duplicates found — all {remaining} {(remaining == 1 ? "row is" : "rows are")} unique."
        : $"Removed {removed} duplicate {(removed == 1 ? "row" : "rows")}; {remaining} unique {(remaining == 1 ? "row remains" : "rows remain")}.";

    public async void ShowTextToColumnsDialog()
    {
        var sheet = Session.ActiveSheet;
        var selection = Selection.Range;
        var source = new CellRange(new CellAddress(selection.Left, selection.Top), new CellAddress(selection.Left, selection.Bottom));
        var delimiter = await ShowModalAsync(new TextToColumnsDialog());
        if (delimiter is { } d)
        {
            // The split writes the extra fields into the columns to the right of the source; warn before
            // clobbering anything already there (PRD §8.14).
            if (HasDataToRight(sheet, source))
            {
                bool confirmed = await ShowModalAsync(new ConfirmDialog(
                    "Overwrite data to the right?",
                    "This may overwrite data to the right. Continue?", "Continue"));
                if (!confirmed)
                {
                    return;
                }
            }

            Session.Execute(DataEditCommands.TextToColumns(sheet, source, d));
            Refresh();
        }
    }

    /// <summary>
    /// True when any cell immediately right of <paramref name="source"/>'s single column — within its row
    /// range — holds a value or formula, so a Text-to-Columns split there would overwrite data (PRD §8.14).
    /// Conservatively scans only the column directly to the right (the first the split would fill).
    /// </summary>
    internal static bool HasDataToRight(Worksheet sheet, CellRange source)
    {
        int rightColumn = source.Left + 1;
        if (rightColumn >= SheetDimensions.MaxColumns)
        {
            return false;
        }

        for (int row = source.Top; row <= source.Bottom; row++)
        {
            var cell = sheet.Find(row, rightColumn);
            if (cell is not null && (!cell.Value.IsEmpty || cell.IsFormula))
            {
                return true;
            }
        }

        return false;
    }

    public async void ShowDataValidationDialog()
    {
        var sheet = Session.ActiveSheet;
        var rule = await ShowModalAsync(new DataValidationDialog(Selection.Range));
        if (rule is not null)
        {
            Session.Execute(DataEditCommands.SetValidation(sheet.Name, rule));
            Refresh();
        }
    }

    public void ShowFindReplace(bool replace) => _ = ShowModalAsync(new FindReplaceDialog(this, replace));

    public void ToggleFreezePanes()
    {
        Grid.ToggleFreezePanes();
        Refresh();
    }

    public void ShowKeyboardShortcuts() => _ = ShowModalAsync(new KeyboardShortcutsDialog(Commands));

    public void ShowAbout() => _ = ShowModalAsync(new AboutDialog(
        "Cellestra",
        "A clean, fast, native spreadsheet editor for macOS whose home format is OpenDocument Spreadsheet (.ods).",
        subtitle: "from cell + orchestra · pronounced “cel-LES-trah”"));

    public void ShowMessage(string heading, string message) => _ = ShowModalAsync(new MessageDialog(heading, message));

    public async void ShowFormatCellsDialog()
    {
        var sheet = Session.ActiveSheet;
        var active = sheet.Find(Selection.ActiveRow, Selection.ActiveColumn);
        var current = Session.Workbook.Styles.Get(active?.StyleId ?? Cellestra.Core.Styling.StylePool.DefaultId);

        var transform = await ShowModalAsync(new FormatCellsDialog(current, DistinctSelectionStyles(sheet)));
        if (transform is not null)
        {
            Session.Execute(EditCommands.ApplyStyle(Session.Workbook, sheet.Name, Selection.Range, transform, "Format Cells"));
            Refresh();
        }
    }

    // ---- Insert ▸ Function… / Named Range… / Hyperlink… -------------------

    /// <summary>
    /// Insert Function picker (PRD §4 Formulas). Lists every function from the one
    /// <see cref="Cellestra.Core.Formulas.Functions.FunctionRegistry"/>; choosing one seeds
    /// <c>=NAME(</c> into the active cell and enters edit mode with the caret after the open paren,
    /// ready for arguments — the inline autocomplete/signature help then takes over as usual.
    /// </summary>
    public async void ShowFunctionPicker()
    {
        var meta = await ShowModalAsync(new FunctionPickerDialog());
        if (meta is not null)
        {
            // Seed "=NAME(" so the existing autocomplete/signature help kicks in for the arguments;
            // BeginEdit places the caret at the end of the seed (right after the paren).
            Grid.BeginEdit($"={meta.Name}(");
        }
    }

    /// <summary>
    /// Named Range manager (PRD §4 Formulas). Defaults the Target to the current selection (scoped to
    /// the active sheet), lets the user add/delete names, and refreshes the grid and Name Box if
    /// anything changed (a new/removed name affects formula resolution and the Name Box jump list).
    /// </summary>
    public async void ShowNamedRangeManager()
    {
        var selection = Selection.Range.WithSheet(Session.ActiveSheet.Name);
        bool changed = await ShowModalAsync(new NamedRangeDialog(Session.Workbook, selection));
        if (changed)
        {
            Session.RecalculateAll();
            Refresh();
        }
    }

    /// <summary>
    /// Insert/edit Hyperlink on the active cell (PRD §4 Editing). Sets, edits, or clears
    /// <see cref="Cell.Hyperlink"/> through the undoable <see cref="EditCommands.SetHyperlink"/>; when
    /// the cell is empty and the user supplied display text, that text is set in the same undoable step.
    /// </summary>
    public async void ShowHyperlinkDialog()
    {
        var sheet = Session.ActiveSheet;
        int row = Selection.ActiveRow, column = Selection.ActiveColumn;
        var cell = sheet.Find(row, column);
        var value = sheet.GetValue(row, column);
        bool cellIsEmpty = value.IsEmpty && (cell is null || !cell.IsFormula);
        string displayValue = cellIsEmpty ? string.Empty : value.ToInvariantString();

        var outcome = await ShowModalAsync(new HyperlinkDialog(cell?.Hyperlink, displayValue, cellIsEmpty));
        if (outcome is null)
        {
            return; // cancelled — no change
        }

        var commands = new List<IEditCommand>();
        if (outcome.DisplayText is { } text)
        {
            commands.Add(EditCommands.SetInput(Session.Workbook, sheet.Name, row, column, text, Session.Workbook.Settings.EffectiveLocale));
        }

        commands.Add(EditCommands.SetHyperlink(Session.Workbook, sheet.Name, row, column, outcome.Link));
        Session.Execute(commands.Count == 1 ? commands[0] : EditCommands.Composite("Set Hyperlink", commands));
        Refresh();
    }

    /// <summary>
    /// The distinct styles across the current selection, so the Format Cells dialog can show a mixed
    /// selection as "(varies)" rather than flattening everything to the active cell. Walks only stored
    /// cells (bounded), and adds the default style when the selection also covers blank cells.
    /// </summary>
    private IReadOnlyCollection<Cellestra.Core.Styling.CellStyle> DistinctSelectionStyles(Worksheet sheet)
    {
        var ids = new HashSet<int>();
        long stored = 0;
        foreach (var (row, column, cell) in sheet.EnumerateCells())
        {
            if (Selection.Ranges.Any(r => r.Contains(row, column)))
            {
                ids.Add(cell.StyleId);
                stored++;
            }
        }

        long area = Selection.Ranges.Sum(r => (long)(r.Bottom - r.Top + 1) * (r.Right - r.Left + 1));
        if (area > stored) // the selection includes at least one blank (default-styled) cell
        {
            ids.Add(Cellestra.Core.Styling.StylePool.DefaultId);
        }

        return [.. ids.Select(id => Session.Workbook.Styles.Get(id))];
    }

    public void ApplyCellStyle(System.Func<Cellestra.Core.Styling.CellStyle, Cellestra.Core.Styling.CellStyle> transform, string label)
    {
        Grid.ClearStylePreview();
        Session.Execute(EditCommands.ApplyStyle(Session.Workbook, Session.ActiveSheet.Name, Selection.Range, transform, label));
        Refresh();
    }

    public void PreviewCellStyle(System.Func<Cellestra.Core.Styling.CellStyle, Cellestra.Core.Styling.CellStyle>? transform)
    {
        if (transform is null)
        {
            Grid.ClearStylePreview();
        }
        else
        {
            Grid.SetStylePreview(Selection.Range, transform);
        }
    }

    public void AutofitColumns(int left, int right) => Grid.GrowColumnsToFit(left, right);

    public void FitColumns(int left, int right) => Grid.FitColumnsExactly(left, right);
}
