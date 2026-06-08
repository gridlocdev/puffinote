using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The custom-drawn grid canvas follows the system light/dark theme so the whole window is
/// consistently light or dark (rather than a light grid inside dark chrome).
/// </summary>
public class ThemeTests
{
    private static Color CanvasColor(SpreadsheetGrid grid) => ((ISolidColorBrush)grid.Background!).Color;

    [AvaloniaFact]
    public void Grid_canvas_is_light_in_light_theme_and_dark_in_dark_theme()
    {
        var grid = new SpreadsheetGrid();
        var window = new Window { Content = grid, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var light = CanvasColor(grid);

        window.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        var dark = CanvasColor(grid);

        Assert.NotEqual(light, dark);
        Assert.True(light.R > 200 && light.G > 200 && light.B > 200, $"light canvas should be bright, was {light}");
        Assert.True(dark.R < 80 && dark.G < 80 && dark.B < 80, $"dark canvas should be dark, was {dark}");
    }
}
