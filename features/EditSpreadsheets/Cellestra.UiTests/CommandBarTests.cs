using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cellestra.Desktop.Controls.CommandBar;
using Cellestra.Desktop.Views;
using Puffinote.Components.Controls.CommandBar;
using Xunit;
// The Cellestra command bar and Avalonia both expose a `CommandBar` type; bind the bare name to ours.
using CommandBar = Cellestra.Desktop.Controls.CommandBar.CommandBar;

namespace Cellestra.UiTests;

public class CommandBarTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cellestra-commandbar-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A window on a scratch settings file. These tests move the bar through its states on the window's own
    /// editor, and the host persists every move — so without this they would write the developer's real
    /// Cellestra profile, and a run that left the bar Hidden would then hide it for every other test that
    /// opens a window (the search pill included).
    /// </summary>
    private MainWindow ScratchWindow() => new(new Cellestra.Desktop.Services.SettingsService(_path));

    [AvaloniaFact]
    public void Command_bar_is_shown_in_the_window_by_default()
    {
        var window = ScratchWindow();
        window.Show();

        // No longer hidden-by-default: the adaptive bar is always present (PRD §6).
        Assert.NotEmpty(window.GetVisualDescendants().OfType<CommandBar>());
    }

    [AvaloniaFact]
    public void Command_bar_builds_refreshes_and_moves_through_its_three_states()
    {
        var window = ScratchWindow();
        var bar = new CommandBar(window.Editor);

        bar.RefreshState(); // every referenced command id resolved; state evaluation doesn't throw
        Assert.Equal(CommandBarVisibility.Visible, bar.BarVisibility); // the full bar by default

        int notified = 0;
        bar.VisibilityChanged += (_, _) => notified++;

        bar.BarVisibility = CommandBarVisibility.Collapsed;
        Assert.Equal(CommandBarVisibility.Collapsed, bar.BarVisibility);

        bar.BarVisibility = CommandBarVisibility.Hidden;
        Assert.Equal(CommandBarVisibility.Hidden, bar.BarVisibility);

        bar.BarVisibility = CommandBarVisibility.Hidden; // idempotent — the same state is not a change
        Assert.Equal(2, notified); // host persists the choice off this event
    }

    [AvaloniaFact]
    public void A_hidden_command_bar_takes_no_room_and_comes_back()
    {
        var window = ScratchWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout(); // Bounds are only meaningful once a layout pass has run

        var bar = window.GetVisualDescendants().OfType<CommandBar>().Single();
        double shownHeight = bar.Bounds.Height;
        Assert.True(shownHeight > 0);

        window.Editor.CommandBarVisibility = CommandBarVisibility.Hidden;
        Dispatcher.UIThread.RunJobs();
        window.Editor.Measure(window.Editor.Bounds.Size);
        window.Editor.Arrange(new Avalonia.Rect(window.Editor.Bounds.Size));
        Assert.Equal(0, bar.Bounds.Height); // the row goes back to the grid, not to an empty strip

        window.Editor.CommandBarVisibility = CommandBarVisibility.Visible;
        Dispatcher.UIThread.RunJobs();
        window.Editor.Measure(window.Editor.Bounds.Size);
        window.Editor.Arrange(new Avalonia.Rect(window.Editor.Bounds.Size));
        Assert.True(bar.Bounds.Height > 0);
    }

    [AvaloniaFact]
    public void The_catalog_carries_one_command_per_state_and_lights_the_one_in_force()
    {
        var window = ScratchWindow();
        window.Show();

        var byState = new (string Id, CommandBarVisibility State)[]
        {
            ("view.commandBarVisible", CommandBarVisibility.Visible),
            ("view.commandBarCollapsed", CommandBarVisibility.Collapsed),
            ("view.commandBarHidden", CommandBarVisibility.Hidden),
        };

        foreach (var (id, state) in byState)
        {
            var command = window.Editor.Commands.ById(id);
            Assert.NotNull(command);

            command.Execute();
            Assert.Equal(state, window.Editor.CommandBarVisibility);

            // Each reports whether it is the state in force, so the palette shows where you already are.
            foreach (var (otherId, otherState) in byState)
            {
                Assert.Equal(otherState == state, window.Editor.Commands.ById(otherId)!.IsToggleActive);
            }
        }
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

        Assert.Equal("Sort Ascending · A1", CommandBar.Describe(window.Editor.Commands.ById("data.sortAsc")!, window.Editor));
        Assert.Equal("Bold off · A1", CommandBar.Describe(window.Editor.Commands.ById("format.bold")!, window.Editor));

        window.Editor.ApplyCellStyle(s => s.WithFont(s.Font with { Bold = true }), "Bold");
        Assert.Equal("Bold on · A1", CommandBar.Describe(window.Editor.Commands.ById("format.bold")!, window.Editor));
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
        window.Editor.Commands.ById("format.currency")!.Execute(); // "$1,234,567.89" overflows the default width

        Assert.True(session.ActiveSheet.GetColumnWidth(0) > before, "column should grow to fit the formatted value");
    }

    [Fact]
    public void Bar_layout_ids_all_resolve_in_the_catalog()
    {
        // Pure metadata: the bar layout references ids that must exist in the catalog (no window needed).
        var commands = FakeAppEnvironment.Catalog;
        foreach (var group in CommandBarModel.For(CommandContext.Cell))
        {
            foreach (string id in group.Primary.Concat(group.Overflow))
            {
                Assert.True(commands.ById(id) is not null, $"unknown command id in bar layout: {id}");
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
        Pump();

        int total = CommandBarModel.For(CommandContext.Cell).Count;

        // Wide: everything fits inline, no overflow affordance.
        Assert.False(bar.IsOverflowing);
        Assert.Equal(total, bar.InlineGroupCount);
        Assert.Equal(0, bar.OverflowGroupCount);

        // Narrow: the rightmost groups fold away behind the double-chevron button — no group is lost.
        host.Width = 340;
        Pump();

        Assert.True(bar.IsOverflowing);
        Assert.True(bar.InlineGroupCount < total);
        Assert.Equal(total, bar.InlineGroupCount + bar.OverflowGroupCount);

        // Widen again and the groups come back inline, drawer closes.
        host.Width = 1400;
        Pump();

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
        Pump();

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
        Pump();

        var headers = bar.OverflowDrawerPanels
            .Select(p => p.GetLogicalDescendants().OfType<TextBlock>().First().Text!)
            .ToList();
        Assert.True(headers.Count >= 2, "expected several groups to fold at this width");

        // The drawer follows the usefulness ranking (Numbers, Data, Insert, Border, Formula, …), so the
        // shown headers must be that ranking filtered to the folded groups — never the bar's left-to-right
        // order (which would lead with Data/Insert, the rightmost groups that fold first).
        string[] ranking = ["Numbers", "Data", "Insert", "Border", "Formula", "Edit", "Text", "Align"];
        var expected = ranking.Where(headers.Contains).ToList();
        Assert.Equal(expected, headers);
    }

    // The reflow runs off the strip's SizeChanged and reparents groups, which queues another layout
    // pass; flush the dispatcher (twice, for the cascade) so the split settles before asserting.
    private static void Pump()
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
