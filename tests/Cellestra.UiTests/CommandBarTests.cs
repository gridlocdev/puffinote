using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.CommandBar;
using Cellestra.Desktop.Views;
using Xunit;
// The Cellestra command bar and Avalonia both expose a `CommandBar` type; bind the bare name to ours.
using CommandBar = Cellestra.Desktop.Controls.CommandBar.CommandBar;

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

    [Fact]
    public void Data_group_surfaces_both_sort_directions_as_primary_tools()
    {
        var data = CommandBarModel.For(CommandContext.Cell).Single(g => g.Name == "Data");
        Assert.Contains("data.sortAsc", data.Primary);
        Assert.Contains("data.sortDesc", data.Primary);
    }

    [AvaloniaFact]
    public void Every_groups_drawer_builds_and_refreshes_without_throwing()
    {
        var window = new MainWindow();
        window.Show();

        // Text, Align, and Numbers carry bespoke drawer affordances (colours/size, vertical align,
        // format-as); building each and running its open-time refresh must not throw.
        foreach (var group in CommandBarModel.For(CommandContext.Cell).Where(g => g.HasDrawer))
        {
            var (content, refresh) = CommandBarDrawer.Build(group, window, () => { });
            Assert.NotNull(content);
            refresh(); // mirrors Flyout.Opened
        }
    }

    [AvaloniaFact]
    public void Narrow_bar_folds_groups_into_the_overflow_drawer_instead_of_scrolling()
    {
        var window = new MainWindow();
        var bar = new CommandBar(window);
        // Host the bar in a window we can resize, so the strip gets a real arranged width to fit against.
        var host = new Window { Content = bar, Width = 1400, Height = 120 };
        host.Show();
        Pump(host);

        int total = CommandBarModel.For(CommandContext.Cell).Count;

        // Wide: everything fits inline, no overflow affordance.
        Assert.False(bar.IsOverflowing);
        Assert.Equal(total, bar.InlineGroupCount);
        Assert.Equal(0, bar.OverflowGroupCount);

        // Narrow: the rightmost groups fold away behind the double-chevron button — no group is lost.
        host.Width = 340;
        Pump(host);

        Assert.True(bar.IsOverflowing);
        Assert.True(bar.InlineGroupCount < total);
        Assert.Equal(total, bar.InlineGroupCount + bar.OverflowGroupCount);

        // Widen again and the groups come back inline, drawer closes.
        host.Width = 1400;
        Pump(host);

        Assert.False(bar.IsOverflowing);
        Assert.Equal(total, bar.InlineGroupCount);
    }

    [AvaloniaFact]
    public void Overflow_drawer_flattens_a_folded_group_into_rows_with_no_nested_submenu()
    {
        var window = new MainWindow();
        var bar = new CommandBar(window);
        var host = new Window { Content = bar, Width = 340, Height = 120 };
        host.Show();
        Pump(host);

        Assert.True(bar.IsOverflowing);

        // The Data group is rightmost, so it folds first. In the drawer it must appear flattened: its
        // group header, its PRIMARY tools (e.g. Sort Ascending) AND its overflow tools (e.g. Custom
        // Sort) all as rows in one menu — not behind a nested chevron submenu.
        var labels = bar.OverflowDrawerPanels
            .SelectMany(p => p.GetLogicalDescendants().OfType<TextBlock>())
            .Select(t => t.Text)
            .ToList();

        Assert.Contains("Data", labels);             // group header
        Assert.Contains("Sort Ascending", labels);   // a primary tool, now a flat row
        Assert.Contains("Sort…", labels);            // an overflow tool, same flat menu

        // No folded group keeps a chevron submenu in the drawer: the only flyouts hanging off drawer
        // buttons are bespoke pickers (the colour "Custom…" picker), never a group's "More …" drawer.
        foreach (var button in bar.OverflowDrawerPanels.SelectMany(p => p.GetLogicalDescendants().OfType<Button>()))
        {
            if (button.Flyout is not null)
            {
                Assert.DoesNotContain("tools", ToolTip.GetTip(button) as string ?? string.Empty);
            }
        }
    }

    // The reflow runs off the strip's SizeChanged and reparents groups, which queues another layout
    // pass; flush the dispatcher (twice, for the cascade) so the split settles before asserting.
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Drawer_applies_vertical_alignment_and_font_size_through_the_session()
    {
        var window = new MainWindow();
        window.Show();

        window.ApplyCellStyle(s => s.WithAlignment(s.Alignment with { Vertical = Cellestra.Core.Styling.VerticalAlignment.Middle }), "Vertical align");
        var sheet = window.Session.ActiveSheet;
        var style = window.Session.Workbook.Styles.Get(sheet.Find(0, 0)?.StyleId ?? Cellestra.Core.Styling.StylePool.DefaultId);
        Assert.Equal(Cellestra.Core.Styling.VerticalAlignment.Middle, style.Alignment.Vertical);

        window.ApplyCellStyle(s => s.WithFont(s.Font with { SizePoints = 22 }), "Font size 22");
        style = window.Session.Workbook.Styles.Get(sheet.Find(0, 0)?.StyleId ?? Cellestra.Core.Styling.StylePool.DefaultId);
        Assert.Equal(22, style.Font.SizePoints);
    }
}
