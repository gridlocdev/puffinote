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
        var bar = new CommandBar(window.Editor);

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

        Assert.False(window.Editor.Session.CanUndo);

        // Hovering a swatch/size previews without touching the model or undo stack (PRD §8.5).
        window.Editor.PreviewCellStyle(s => s.WithFont(s.Font with { Bold = true }));
        Assert.False(window.Editor.Session.CanUndo);
        window.Editor.PreviewCellStyle(null);
        Assert.False(window.Editor.Session.CanUndo);

        // Clicking it commits exactly one undoable edit.
        window.Editor.ApplyCellStyle(s => s.WithFont(s.Font with { Bold = true }), "Bold");
        Assert.True(window.Editor.Session.CanUndo);
    }

    [AvaloniaFact]
    public void Narration_reports_toggle_state_and_active_cell()
    {
        var window = new MainWindow();
        window.Show();

        Assert.Equal("Sort Ascending · A1", CommandBar.Describe(AppCommands.ById("data.sortAsc")!, window.Editor));
        Assert.Equal("Bold off · A1", CommandBar.Describe(AppCommands.ById("format.bold")!, window.Editor));

        window.Editor.ApplyCellStyle(s => s.WithFont(s.Font with { Bold = true }), "Bold");
        Assert.Equal("Bold on · A1", CommandBar.Describe(AppCommands.ById("format.bold")!, window.Editor));
    }

    [AvaloniaFact]
    public void Applying_a_wide_number_format_grows_the_column()
    {
        var window = new MainWindow();
        window.Show();

        var session = window.Editor.Session;
        session.Execute(Cellestra.Core.Editing.EditCommands.SetInput(
            session.Workbook, session.ActiveSheet.Name, 0, 0, "1234567.89", session.Workbook.Settings.EffectiveLocale));

        double before = session.ActiveSheet.GetColumnWidth(0);
        AppCommands.ById("format.currency")!.Execute(window.Editor); // "$1,234,567.89" overflows the default width

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
            var (content, refresh) = CommandBarDrawer.Build(group, window.Editor, () => { });
            Assert.NotNull(content);
            refresh(); // mirrors Flyout.Opened
        }
    }

    [AvaloniaFact]
    public void Narrow_bar_folds_groups_into_the_overflow_drawer_instead_of_scrolling()
    {
        var window = new MainWindow();
        var bar = new CommandBar(window.Editor);
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
    public void Overflow_drawer_shows_a_folded_group_as_an_icon_grid_with_no_nested_submenu()
    {
        var window = new MainWindow();
        var bar = new CommandBar(window.Editor);
        var host = new Window { Content = bar, Width = 340, Height = 120 };
        host.Show();
        Pump(host);

        Assert.True(bar.IsOverflowing);

        // The Data group is rightmost, so it folds first. In the drawer it appears as its header over a
        // grid of icon buttons for its WHOLE toolset (3 primary + 5 overflow) — not a nested submenu.
        var dataSection = bar.OverflowDrawerPanels
            .FirstOrDefault(p => p.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Data"));
        Assert.NotNull(dataSection);

        var buttons = dataSection!.GetLogicalDescendants().OfType<Button>().ToList();
        var data = CommandBarModel.For(CommandContext.Cell).Single(g => g.Name == "Data");
        Assert.Equal(data.Primary.Count + data.Overflow.Count, buttons.Count);

        // Flattened: every tool is a direct icon button — none opens a "More …" chevron submenu.
        Assert.All(buttons, b => Assert.Null(b.Flyout));
    }

    [AvaloniaFact]
    public void Overflow_sections_are_ordered_by_usefulness_not_bar_order()
    {
        var window = new MainWindow();
        var bar = new CommandBar(window.Editor);
        // Narrow enough that several groups fold, so their relative order is observable.
        var host = new Window { Content = bar, Width = 300, Height = 120 };
        host.Show();
        Pump(host);

        var headers = bar.OverflowDrawerPanels
            .Select(p => p.GetLogicalDescendants().OfType<TextBlock>().First().Text!)
            .ToList();
        Assert.True(headers.Count >= 2, "expected several groups to fold at this width");

        // The drawer follows the usefulness ranking (Numbers, Data, Insert, Border, Formula, …), so the
        // shown headers must be that ranking filtered to the folded groups — never the bar's left-to-right
        // order (which would lead with Data/Insert, the rightmost groups that fold first).
        string[] ranking = { "Numbers", "Data", "Insert", "Border", "Formula", "Edit", "Text", "Align" };
        var expected = ranking.Where(headers.Contains).ToList();
        Assert.Equal(expected, headers);
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

        window.Editor.ApplyCellStyle(s => s.WithAlignment(s.Alignment with { Vertical = Cellestra.Core.Styling.VerticalAlignment.Middle }), "Vertical align");
        var sheet = window.Editor.Session.ActiveSheet;
        var style = window.Editor.Session.Workbook.Styles.Get(sheet.Find(0, 0)?.StyleId ?? Cellestra.Core.Styling.StylePool.DefaultId);
        Assert.Equal(Cellestra.Core.Styling.VerticalAlignment.Middle, style.Alignment.Vertical);

        window.Editor.ApplyCellStyle(s => s.WithFont(s.Font with { SizePoints = 22 }), "Font size 22");
        style = window.Editor.Session.Workbook.Styles.Get(sheet.Find(0, 0)?.StyleId ?? Cellestra.Core.Styling.StylePool.DefaultId);
        Assert.Equal(22, style.Font.SizePoints);
    }
}
