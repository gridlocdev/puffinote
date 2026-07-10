using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Puffinote.Components.Controls.CommandBar;
using Typage.Control.Controls;
using Typage.Control.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The Markdown formatting toolbar rides the family's shared <see cref="AdaptiveCommandBar"/> shell and
/// surfaces every formatting command (no more "some but not all") — the common ones inline, the rest behind
/// each group's dropdown.
/// </summary>
public class MarkdownCommandBarTests
{
    [AvaloniaFact]
    public void Surfaces_every_formatting_command_in_the_editors_catalog()
    {
        var view = new MarkdownEditorView();
        var bar = new MarkdownCommandBar(view.EditingCommands);

        // Every format.*/insert.* command the editor publishes must be reachable somewhere on the bar.
        var expected = view.EditingCommands
            .Select(c => c.Id)
            .Where(id => id.StartsWith("format.", StringComparison.Ordinal) || id.StartsWith("insert.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(expected);
        Assert.All(expected, id => Assert.Contains(id, bar.SurfacedIdsForTest));

        // Including the newly-added ones that the old flat bar omitted.
        Assert.Contains("format.h4", bar.SurfacedIdsForTest);
        Assert.Contains("format.h6", bar.SurfacedIdsForTest);
        Assert.Contains("format.removeTask", bar.SurfacedIdsForTest);
    }

    [AvaloniaFact]
    public void Rides_the_shared_adaptive_shell_and_folds_when_narrow()
    {
        var bar = new MarkdownCommandBar(new MarkdownEditorView().EditingCommands);
        var host = new Window { Content = bar, Width = 900, Height = 120 };
        host.Show();
        Pump();

        var shell = bar.GetVisualDescendants().OfType<AdaptiveCommandBar>().Single();
        Assert.False(shell.IsOverflowing); // all four groups fit wide
        Assert.Equal(4, shell.InlineGroupCount);

        host.Width = 430; // squeeze it — the rightmost groups fold into the overflow drawer, none are lost
        Pump();
        Assert.True(shell.IsOverflowing);
        Assert.True(shell.InlineGroupCount < 4);
        Assert.Equal(4, shell.InlineGroupCount + shell.OverflowGroupCount);

        host.Width = 900; // widen again — they come back inline, drawer empties
        Pump();
        Assert.False(shell.IsOverflowing);
        Assert.Equal(4, shell.InlineGroupCount);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }
}
