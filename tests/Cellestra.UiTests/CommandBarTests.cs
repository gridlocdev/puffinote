using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.CommandBar;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class CommandBarTests
{
    [AvaloniaFact]
    public void Command_bar_is_shown_in_the_window_by_default()
    {
        var window = new MainWindow();
        window.Show();

        // No longer hidden-by-default: the adaptive bar is always present (PRD §6).
        Assert.NotEmpty(window.GetVisualDescendants().OfType<CommandBar>());
    }

    [AvaloniaFact]
    public void Command_bar_builds_refreshes_and_collapses()
    {
        var window = new MainWindow();
        var bar = new CommandBar(window);

        bar.RefreshState(); // every referenced command id resolved; state evaluation doesn't throw
        Assert.False(bar.Collapsed); // expanded by default

        bool notified = false;
        bar.CollapsedChanged += (_, _) => notified = true;
        bar.Collapsed = true;

        Assert.True(bar.Collapsed);
        Assert.True(notified); // host persists the choice off this event
    }

    [AvaloniaFact]
    public void Drawer_preview_does_not_commit_but_apply_does()
    {
        var window = new MainWindow();
        window.Show();

        Assert.False(window.Session.CanUndo);

        // Hovering a swatch/size previews without touching the model or undo stack (PRD §8.5).
        window.PreviewCellStyle(s => s.WithFont(s.Font with { Bold = true }));
        Assert.False(window.Session.CanUndo);
        window.PreviewCellStyle(null);
        Assert.False(window.Session.CanUndo);

        // Clicking it commits exactly one undoable edit.
        window.ApplyCellStyle(s => s.WithFont(s.Font with { Bold = true }), "Bold");
        Assert.True(window.Session.CanUndo);
    }

    [AvaloniaFact]
    public void Narration_reports_toggle_state_and_active_cell()
    {
        var window = new MainWindow();
        window.Show();

        Assert.Equal("Sort Ascending · A1", CommandBar.Describe(AppCommands.ById("data.sortAsc")!, window));
        Assert.Equal("Bold off · A1", CommandBar.Describe(AppCommands.ById("format.bold")!, window));

        window.ApplyCellStyle(s => s.WithFont(s.Font with { Bold = true }), "Bold");
        Assert.Equal("Bold on · A1", CommandBar.Describe(AppCommands.ById("format.bold")!, window));
    }

    [AvaloniaFact]
    public void Applying_a_wide_number_format_grows_the_column()
    {
        var window = new MainWindow();
        window.Show();

        var session = window.Session;
        session.Execute(Cellestra.Core.Editing.EditCommands.SetInput(
            session.Workbook, session.ActiveSheet.Name, 0, 0, "1234567.89", session.Workbook.Settings.EffectiveLocale));

        double before = session.ActiveSheet.GetColumnWidth(0);
        AppCommands.ById("format.currency")!.Execute(window); // "$1,234,567.89" overflows the default width

        Assert.True(session.ActiveSheet.GetColumnWidth(0) > before, "column should grow to fit the formatted value");
    }

    [Fact]
    public void Bar_layout_ids_all_resolve_in_the_catalog()
    {
        foreach (var group in CommandBarModel.For(CommandContext.Cell))
        {
            foreach (string id in group.Primary.Concat(group.Overflow))
            {
                Assert.True(AppCommands.ById(id) is not null, $"unknown command id in bar layout: {id}");
            }
        }
    }
}
