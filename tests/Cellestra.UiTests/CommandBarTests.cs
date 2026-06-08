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
