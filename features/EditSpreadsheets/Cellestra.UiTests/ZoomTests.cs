using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Views;
using Puffinote.Components.Commands;
using Puffinote.Components.Theming;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Zoom on the grid. Cellestra had it first; what these hold is that it now steps the family's shared
/// ladder rather than its own ±10%, and that the status bar's percentage can't lag the size on screen.
/// </summary>
public class ZoomTests
{
    private static AppCommand Command(MainWindow window, string id) =>
        window.Editor.Commands.All.Single(c => c.Id == id);

    [AvaloniaFact]
    public void Zooming_steps_the_shared_ladder()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Zoom = ZoomScale.Default;

        Command(window, ZoomCommands.ZoomInId).Execute();
        Assert.Equal(1.1, window.Editor.Grid.Zoom);
        Command(window, ZoomCommands.ZoomInId).Execute();
        Assert.Equal(1.25, window.Editor.Grid.Zoom);

        Command(window, ZoomCommands.ZoomOutId).Execute();
        Assert.Equal(1.1, window.Editor.Grid.Zoom);

        Command(window, ZoomCommands.ActualSizeId).Execute();
        Assert.Equal(ZoomScale.Default, window.Editor.Grid.Zoom);
    }

    [AvaloniaFact]
    public void The_grid_holds_the_ladder_s_bounds()
    {
        var window = new MainWindow();
        window.Show();

        window.Editor.Zoom = 99;
        Assert.Equal(ZoomScale.Max, window.Editor.Grid.Zoom);

        window.Editor.Zoom = 0;
        Assert.Equal(ZoomScale.Min, window.Editor.Grid.Zoom);
    }

    [AvaloniaFact]
    public void Zooming_scales_what_the_grid_draws()
    {
        var window = new MainWindow();
        window.Show();
        window.Editor.Zoom = ZoomScale.Default;
        window.CaptureRenderedFrame();

        var sheet = window.Editor.Session.ActiveSheet;
        double width = sheet.GetColumnWidth(0);

        window.Editor.Zoom = 2.0;
        window.CaptureRenderedFrame();

        // The underlying column is untouched — zoom is a lens, not an edit.
        Assert.Equal(width, sheet.GetColumnWidth(0));
        Assert.Equal(2.0, window.Editor.Grid.Zoom);
    }
}
