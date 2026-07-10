using Cellestra.Charts;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Desktop.Controls.ChartView;
using Cellestra.Desktop.Views.Dialogs;
using Puffinote.Components.Overlays;

namespace Cellestra.Editor;

public partial class SpreadsheetEditorView
{
    public void ShowSheetSwitcher()
    {
        var items = Session.Workbook.Worksheets
            .Select(s => new PaletteItem(s.Name, "list", "Worksheet", string.Empty, () =>
            {
                Session.SetActiveSheet(s);
                BuildSheetTabs();
                Grid.Refresh();
                OnSelectionChanged();
            }, "Sheets"))
            .ToList();
        ShowPalette("Go to sheet", items);
    }

    public void AddSheet()
    {
        Session.AddSheet();
        BuildSheetTabs();
        // The new sheet is empty and active: reset the active cell to A1 and resync the chrome so the
        // formula bar reflects the new empty cell instead of stale text from the previous sheet (#7).
        Selection.MoveTo(0, 0);
        Grid.Refresh();
        OnSelectionChanged();
    }

    public void AddChartSheet()
    {
        var definition = new ChartDefinition { Type = ChartType.Column, Title = "Chart" };

        // When the sheet has exactly one table, autofill the chart from it — first column for the
        // category labels and the first numeric column for the values (task 6). Otherwise fall back to
        // the current selection (and to the table if that seeding finds nothing usable).
        var sheet = Session.ActiveSheet;
        if (!(!sheet.IsChartSheet && sheet.Tables.Count == 1 && SeedChartFromTable(definition, sheet.Tables[0])))
        {
            SeedChartFromSelection(definition);
        }

        Session.AddChartSheet(definition); // raises StructureChanged → RefreshSheetView swaps in the chart view
        BuildSheetTabs();
        UpdateChrome();
        Narrate("Added chart sheet");
    }

    /// <summary>
    /// Pre-fill a new chart from the sheet's single table (task 6): the table's first column becomes
    /// the category labels and its first numeric column becomes one series (named from the header row
    /// when present). Returns false when the table can't be charted (too few rows, or no numeric
    /// column), so the caller can fall back to selection-based seeding.
    /// </summary>
    private bool SeedChartFromTable(ChartDefinition definition, Cellestra.Core.Data.Table table)
    {
        var sheet = Session.ActiveSheet;
        var range = table.Range;
        int dataTop = table.HasHeader ? range.Top + 1 : range.Top;
        if (dataTop > range.Bottom)
        {
            return false; // header-only or empty table — nothing to plot
        }

        int? valueColumn = null;
        for (int column = range.Left + 1; column <= range.Right; column++)
        {
            if (ColumnHasNumber(sheet, column, dataTop, range.Bottom))
            {
                valueColumn = column;
                break;
            }
        }

        if (valueColumn is null)
        {
            return false; // no numeric column beside the labels to chart
        }

        string name = sheet.Name;
        string Ref(int column) =>
            $"{name}.{SheetDimensions.ColumnToLetters(column)}{dataTop + 1}:{SheetDimensions.ColumnToLetters(column)}{range.Bottom + 1}";

        definition.CategoriesReference = Ref(range.Left); // first column = the label column
        var series = new ChartSeries { ValuesReference = Ref(valueColumn.Value) };
        if (table.HasHeader)
        {
            series.NameReference = $"{name}.{SheetDimensions.ColumnToLetters(valueColumn.Value)}{range.Top + 1}";
        }

        definition.Series.Add(series);
        return true;
    }

    private static bool ColumnHasNumber(Worksheet sheet, int column, int fromRow, int toRow)
    {
        for (int row = fromRow; row <= toRow; row++)
        {
            if (sheet.GetValue(row, column).IsNumber)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Pre-fill a new chart from the current grid selection when it looks tabular: the first column
    /// becomes the category labels and each remaining column becomes a series, taking its name from a
    /// text header row when present. A single-column selection becomes one unnamed series.
    /// </summary>
    private void SeedChartFromSelection(ChartDefinition definition)
    {
        var sheet = Session.ActiveSheet;
        if (sheet.IsChartSheet)
        {
            return; // a chart sheet has no grid data to seed from
        }

        var range = Selection.Range;
        if (range.RowCount < 2)
        {
            return; // not enough rows to chart anything meaningful
        }

        string name = sheet.Name;
        int top = range.Top, bottom = range.Bottom, left = range.Left, right = range.Right;
        bool headerRow = sheet.GetValue(top, left).IsText;
        int dataTop = headerRow && bottom > top ? top + 1 : top;

        string Ref(int column, int fromRow, int toRow) =>
            $"{name}.{SheetDimensions.ColumnToLetters(column)}{fromRow + 1}:{SheetDimensions.ColumnToLetters(column)}{toRow + 1}";

        if (right > left)
        {
            definition.CategoriesReference = Ref(left, dataTop, bottom);
            for (int column = left + 1; column <= right; column++)
            {
                var series = new ChartSeries { ValuesReference = Ref(column, dataTop, bottom) };
                if (headerRow)
                {
                    series.NameReference = $"{name}.{SheetDimensions.ColumnToLetters(column)}{top + 1}";
                }

                definition.Series.Add(series);
            }
        }
        else
        {
            // Single column: chart it as one series with no category labels.
            definition.Series.Add(new ChartSeries { ValuesReference = Ref(left, dataTop, bottom) });
        }
    }

    /// <summary>
    /// Show the right view for the active sheet: the chart editor for a chart sheet (with the grid and
    /// formula bar hidden), otherwise the spreadsheet grid. Rebuilds the chart view each switch so it
    /// binds to the active sheet's definition.
    /// </summary>
    private void RefreshSheetView()
    {
        var sheet = Session.ActiveSheet;
        bool isChart = sheet.IsChartSheet;

        Grid.IsVisible = !isChart;
        ChartHost.IsVisible = isChart;
        FormulaBarPanel.IsVisible = !isChart;

        if (isChart && sheet.Chart is { } definition)
        {
            var view = new ChartSheetView(Session.Workbook, definition);
            view.ChartChanged += (_, _) =>
            {
                Session.MarkChartModified();
                UpdateChrome();
            };
            ChartHost.Content = view;
        }
        else
        {
            ChartHost.Content = null;
        }
    }

    /// <summary>Re-resolve the active chart against the (possibly just-edited) data and repaint it.</summary>
    private void RefreshChartData()
    {
        if (ChartHost.Content is ChartSheetView view)
        {
            view.RefreshData();
        }
    }

    // ---- Test seams (headless UI tests; InternalsVisibleTo Cellestra.UiTests) ----

    /// <summary>True when the chart view is showing instead of the grid (active sheet is a chart sheet).</summary>
    internal bool ChartViewVisibleForTest => ChartHost.IsVisible;

    /// <summary>True when the formula bar is showing (hidden on chart sheets).</summary>
    internal bool FormulaBarVisibleForTest => FormulaBarPanel.IsVisible;

    /// <summary>The chart view currently hosted, or null on a data sheet.</summary>
    internal ChartSheetView? ActiveChartViewForTest => ChartHost.Content as ChartSheetView;

    public void DuplicateActiveSheet()
    {
        // The tab's context menu activates the right-clicked sheet first, so the active sheet is the
        // one to duplicate (works for data and chart sheets alike). StructureChanged rebuilds the tabs
        // and swaps in the right view; reset to A1 so a duplicated data sheet reads cleanly.
        var copy = Session.DuplicateSheet(Session.ActiveSheet);
        Selection.MoveTo(0, 0);
        OnSelectionChanged();
        Narrate($"Duplicated sheet as “{copy.Name}”");
    }

    public async void RenameActiveSheet()
    {
        var sheet = Session.ActiveSheet;
        string? name = await ShowModalAsync(new TextPromptDialog("Rename sheet", "Name", sheet.Name));
        if (name is null || name == sheet.Name)
        {
            return;
        }

        CommitSheetRename(sheet, name);
    }

    /// <summary>Apply a sheet rename through the validating session path, narrating success or surfacing the error.</summary>
    /// <returns>True when the rename succeeded.</returns>
    private bool CommitSheetRename(Worksheet sheet, string name)
    {
        try
        {
            Session.RenameSheet(sheet, name); // raises StructureChanged → rebuilds tabs/chrome
            OnSelectionChanged();
            Narrate($"Renamed sheet to {name}");
            return true;
        }
        catch (Exception ex)
        {
            ShowMessage("Couldn't rename the sheet", ex.Message);
            return false;
        }
    }

    public async void DeleteActiveSheet()
    {
        if (Session.Workbook.Worksheets.Count <= 1)
        {
            ShowMessage("Can't delete the last sheet", "A workbook must keep at least one worksheet.");
            return;
        }

        var sheet = Session.ActiveSheet;
        bool confirmed = await ShowModalAsync(new ConfirmDialog("Delete sheet?",
            $"Delete “{sheet.Name}” and all its data? This can’t be undone.", "Delete"));
        if (confirmed)
        {
            Session.DeleteSheet(sheet); // raises StructureChanged → rebuilds tabs/chrome
            OnSelectionChanged();
            Narrate($"Deleted sheet {sheet.Name}");
        }
    }

    public async void ResetSheetData()
    {
        var sheet = Session.ActiveSheet;
        var used = sheet.GetUsedRange();
        if (used is null)
        {
            Narrate("Sheet is already empty");
            return; // nothing to clear
        }

        // Confirm only above the §8.7 threshold (>100 non-empty cells); below it, undo covers a mistake.
        int nonEmpty = CountNonEmpty(sheet);
        if (nonEmpty > 100)
        {
            bool confirmed = await ShowModalAsync(new ConfirmDialog("Reset sheet data?",
                $"Clear all data and formatting from “{sheet.Name}” ({nonEmpty} cells)? You can undo this.", "Reset"));
            if (!confirmed)
            {
                return;
            }
        }

        // ClearAll wipes value, formula, style, and hyperlink over the range; ResetSheetLayout adds
        // the sheet-level state ClearAll can't see — row/column fills, hidden flags, and any active
        // AutoFilter/sort — so a reset truly clears background colours and filters. One undoable step.
        Session.Execute(EditCommands.Composite("Reset Sheet Data",
        [
            EditCommands.ClearAll(sheet.Name, used.Value),
            DataEditCommands.ResetSheetLayout(sheet.Name),
        ]));
        Selection.MoveTo(0, 0);
        Refresh();
        Narrate($"Reset {sheet.Name}");
    }

    /// <summary>Number of cells on the sheet that hold a value or formula (the §8.7 "non-empty" count).</summary>
    private static int CountNonEmpty(Worksheet sheet)
    {
        int count = 0;
        foreach (var (_, _, cell) in sheet.EnumerateCells())
        {
            if (!cell.Value.IsEmpty || cell.IsFormula)
            {
                count++;
            }
        }

        return count;
    }
}
