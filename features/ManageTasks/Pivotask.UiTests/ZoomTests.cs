using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pivotask.Control.Views;
using Pivotask.Core;
using Pivotask.Core.Model;
using Puffinote.Components.Commands;
using Puffinote.Components.Theming;
using Xunit;

namespace Pivotask.UiTests;

/// <summary>
/// Zoom on the board. A board has no single font size to turn, so it scales the whole task surface's
/// <i>layout</i> — which is the part worth pinning: the columns must re-measure at the new size (so cards
/// re-wrap and scrolling stays honest) rather than being a magnified picture of the old ones, and the
/// filter strip above must stay put.
/// </summary>
public class ZoomTests
{
    private static TaskBoardView Open()
    {
        var board = new Board();
        var inbox = new TaskGroup { Id = "g1", Name = "Inbox" };
        inbox.Tasks.Add(new TaskItem { Id = "t1", Title = "Rewrite the empty states" });
        board.Groups.Add(inbox);

        var view = new TaskBoardView { Document = new BoardDocument(board) };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    private static AppCommand Command(TaskBoardView view, string id) =>
        view.EditingCommands.Single(c => c.Id == id);

    private static LayoutTransformControl ZoomHost(TaskBoardView view) =>
        view.GetVisualDescendants().OfType<LayoutTransformControl>().Single();

    [AvaloniaFact]
    public void Zooming_scales_the_board_surface_by_a_layout_transform()
    {
        var view = Open();
        Assert.Null(ZoomHost(view).LayoutTransform); // nothing to apply at 100%

        Command(view, ZoomCommands.ZoomInId).Execute();

        var scale = Assert.IsType<ScaleTransform>(ZoomHost(view).LayoutTransform);
        Assert.Equal(1.1, scale.ScaleX);
        Assert.Equal(1.1, scale.ScaleY);
    }

    [AvaloniaFact]
    public void The_filter_strip_is_not_zoomed_with_the_cards()
    {
        // Zoom magnifies the tasks. A filter row that grew with them would eat the room a reader zoomed
        // out to gain, so the transform hosts the board surface alone.
        var view = Open();
        view.Zoom = 2.0;
        Dispatcher.UIThread.RunJobs();

        var host = ZoomHost(view);
        Assert.DoesNotContain(host.GetVisualDescendants().OfType<TextBox>(), b => b.PlaceholderText == "Search");
    }

    [AvaloniaFact]
    public void Actual_size_takes_the_transform_back_off_again()
    {
        var view = Open();
        view.Zoom = 1.5;
        Assert.NotNull(ZoomHost(view).LayoutTransform);

        Command(view, ZoomCommands.ActualSizeId).Execute();

        Assert.Equal(ZoomScale.Default, view.Zoom);
        Assert.Null(ZoomHost(view).LayoutTransform);
    }

    [AvaloniaFact]
    public void The_board_steps_the_same_ladder_as_its_siblings_and_stops_at_its_ends()
    {
        var view = Open();

        Command(view, ZoomCommands.ZoomOutId).Execute();
        Assert.Equal(0.9, view.Zoom);

        view.Zoom = ZoomScale.Max;
        Command(view, ZoomCommands.ZoomInId).Execute();
        Assert.Equal(ZoomScale.Max, view.Zoom);
        Assert.False(Command(view, ZoomCommands.ZoomInId).IsEnabled);
    }
}
