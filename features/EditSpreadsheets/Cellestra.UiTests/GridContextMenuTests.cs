using Avalonia.Headless.XUnit;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Cellestra.Editor;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The grid's three context menus: what each offers (<see cref="GridContextMenu"/>), and that the
/// line-level commands the header menus introduce actually act on the clicked row/column.
/// </summary>
public class GridContextMenuTests
{
    private static readonly AppCommands Catalog = FakeAppEnvironment.Catalog;

    private static IReadOnlyList<string> Commands(SpreadsheetGrid.ContextTarget target) =>
        [.. GridContextMenu.Ids(target).Where(id => id is not null).Select(id => id!)];

    [Fact]
    public void Every_menu_entry_names_a_real_catalog_command()
    {
        // The menus are ids into the one catalog (Golden Rule #4); a typo would silently drop a row.
        foreach (var target in Enum.GetValues<SpreadsheetGrid.ContextTarget>())
        {
            foreach (string id in Commands(target))
            {
                Assert.True(Catalog.ById(id) is not null, $"{target} menu references unknown command '{id}'");
            }
        }
    }

    [Fact]
    public void The_header_corner_offers_nothing()
    {
        Assert.Empty(GridContextMenu.Ids(SpreadsheetGrid.ContextTarget.None));
    }

    [Fact]
    public void A_column_header_offers_the_column_level_commands()
    {
        var ids = Commands(SpreadsheetGrid.ContextTarget.ColumnHeader);

        Assert.Contains("insert.columnsLeft", ids);
        Assert.Contains("edit.deleteColumns", ids);
        Assert.Contains("format.columnWidth", ids);
        Assert.Contains("data.autofitColumns", ids);
        Assert.Contains("format.hideColumns", ids);
        Assert.Contains("format.unhideColumns", ids);

        // Nothing row-shaped: right-clicking column B is not a statement about any row.
        Assert.DoesNotContain("insert.rowsAbove", ids);
        Assert.DoesNotContain("edit.deleteRows", ids);
        Assert.DoesNotContain("format.rowHeight", ids);
    }

    [Fact]
    public void A_row_header_offers_the_row_level_commands()
    {
        var ids = Commands(SpreadsheetGrid.ContextTarget.RowHeader);

        Assert.Contains("insert.rowsAbove", ids);
        Assert.Contains("edit.deleteRows", ids);
        Assert.Contains("format.rowHeight", ids);
        Assert.Contains("format.hideRows", ids);
        Assert.Contains("format.unhideRows", ids);

        Assert.DoesNotContain("insert.columnsLeft", ids);
        Assert.DoesNotContain("edit.deleteColumns", ids);
        Assert.DoesNotContain("format.columnWidth", ids);
    }

    [Fact]
    public void A_cell_offers_merge_cells()
    {
        var ids = Commands(SpreadsheetGrid.ContextTarget.Cell);

        Assert.Contains("format.merge", ids);
        Assert.Contains("format.cells", ids);
    }

    [AvaloniaFact]
    public void Right_clicking_a_row_header_selects_that_whole_row()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        env.Selection.MoveTo(0, 0);

        var target = window.Editor.Grid.SelectForContext(RowHeaderPoint(row: 2));

        Assert.Equal(SpreadsheetGrid.ContextTarget.RowHeader, target);
        Assert.Equal(2, env.Selection.Top);
        Assert.Equal(2, env.Selection.Bottom);
        Assert.Equal(0, env.Selection.Left);
        Assert.Equal(SheetDimensions.MaxColumns - 1, env.Selection.Right);
    }

    [AvaloniaFact]
    public void Hiding_rows_from_the_header_selection_hides_only_those_rows()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        sheet.SetValue(2, 0, CellValue.Text("still here"));

        env.Selection.SetRange(2, 0, 3, SheetDimensions.MaxColumns - 1); // rows 3–4, as a header click selects
        env.Commands.ById("format.hideRows")!.Execute();

        Assert.True(sheet.IsRowHidden(2));
        Assert.True(sheet.IsRowHidden(3));
        Assert.False(sheet.IsRowHidden(4));
        Assert.Equal("still here", sheet.GetValue(2, 0).AsText); // visibility only — the data stays
    }

    [AvaloniaFact]
    public void Unhide_from_the_neighbouring_row_brings_the_hidden_run_back()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        env.Selection.SetRange(2, 0, 3, SheetDimensions.MaxColumns - 1);
        env.Commands.ById("format.hideRows")!.Execute();

        // The hidden rows can't be clicked, so the user right-clicks the row above them.
        env.Selection.SetRange(1, 0, 1, SheetDimensions.MaxColumns - 1);
        var unhide = env.Commands.ById("format.unhideRows")!;
        Assert.True(unhide.IsEnabled);
        unhide.Execute();

        Assert.False(sheet.IsRowHidden(2));
        Assert.False(sheet.IsRowHidden(3));
    }

    [AvaloniaFact]
    public void Unhide_is_disabled_when_there_is_nothing_hidden_in_reach()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;

        env.Selection.SetRange(1, 0, 1, SheetDimensions.MaxColumns - 1);
        Assert.False(env.Commands.ById("format.unhideRows")!.IsEnabled);
        Assert.False(env.Commands.ById("format.unhideColumns")!.IsEnabled);
    }

    [AvaloniaFact]
    public void Hiding_columns_from_the_header_selection_hides_only_those_columns()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        env.Selection.SetRange(0, 1, SheetDimensions.MaxRows - 1, 1); // column B, as a header click selects
        env.Commands.ById("format.hideColumns")!.Execute();

        Assert.True(sheet.IsColumnHidden(1));
        Assert.False(sheet.IsColumnHidden(0));

        env.Selection.SetRange(0, 0, SheetDimensions.MaxRows - 1, 0);
        env.Commands.ById("format.unhideColumns")!.Execute();
        Assert.False(sheet.IsColumnHidden(1));
    }

    [AvaloniaFact]
    public void The_line_commands_are_disabled_when_the_selection_spans_their_whole_axis()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;

        // A column-header selection covers every row: hiding or sizing all of them is never the ask,
        // so those commands are disabled rather than hidden (PRD §6 — context-awareness).
        env.Selection.SetRange(0, 1, SheetDimensions.MaxRows - 1, 1);
        Assert.False(env.Commands.ById("format.hideRows")!.IsEnabled);
        Assert.False(env.Commands.ById("format.rowHeight")!.IsEnabled);
        Assert.True(env.Commands.ById("format.hideColumns")!.IsEnabled);

        // …and the mirror image on a row-header selection.
        env.Selection.SetRange(1, 0, 1, SheetDimensions.MaxColumns - 1);
        Assert.False(env.Commands.ById("format.hideColumns")!.IsEnabled);
        Assert.False(env.Commands.ById("format.columnWidth")!.IsEnabled);
        Assert.True(env.Commands.ById("format.hideRows")!.IsEnabled);
    }

    [AvaloniaFact]
    public void Hiding_rows_is_one_undoable_step()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        env.Selection.SetRange(2, 0, 4, SheetDimensions.MaxColumns - 1);
        env.Commands.ById("format.hideRows")!.Execute();
        Assert.True(sheet.IsRowHidden(3));

        env.Session.Undo();
        Assert.False(sheet.IsRowHidden(2));
        Assert.False(sheet.IsRowHidden(3));
        Assert.False(sheet.IsRowHidden(4));
    }

    /// <summary>A point inside the given row's header strip (zoom 1, default sizes), in the grid's own coordinates.</summary>
    private static Avalonia.Point RowHeaderPoint(int row) => new(
        SpreadsheetGrid.HeaderWidth / 2,
        SpreadsheetGrid.HeaderHeight + (row * Cellestra.Core.Defaults.RowHeight) + (Cellestra.Core.Defaults.RowHeight / 2));
}
