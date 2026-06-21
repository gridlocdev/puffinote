using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
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

    [AvaloniaFact]
    public void Default_cell_text_is_dark_on_light_and_light_on_dark()
    {
        var grid = new SpreadsheetGrid();
        var window = new Window { Content = grid, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var defaultStyle = CellStyle.Default; // default font color is black ("automatic")
        var light = grid.ResolvedTextColor(defaultStyle, CellValue.Text("x"));

        window.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        var dark = grid.ResolvedTextColor(defaultStyle, CellValue.Text("x"));

        Assert.True(light.R < 80, $"default text should be dark in light theme, was {light}");
        Assert.True(dark.R > 200, $"default text should be light in dark theme, was {dark}");

        // An explicitly chosen color is document data: it stays put across themes.
        var red = defaultStyle with { Font = defaultStyle.Font with { Color = ArgbColor.FromRgb(0xC0, 0x00, 0x00) } };
        Assert.Equal(Color.FromRgb(0xC0, 0x00, 0x00), grid.ResolvedTextColor(red, CellValue.Text("x")));
    }

    [AvaloniaFact]
    public void Chrome_surface_brush_differs_between_themes()
    {
        var window = new MainWindow();
        window.Show();

        Assert.True(window.TryFindResource("ChromeBackground", ThemeVariant.Light, out var light));
        Assert.True(window.TryFindResource("ChromeBackground", ThemeVariant.Dark, out var dark));
        var lightColor = ((ISolidColorBrush)light!).Color;
        var darkColor = ((ISolidColorBrush)dark!).Color;

        Assert.True(lightColor.R > 200, $"light chrome should be bright, was {lightColor}");
        Assert.True(darkColor.R < 80, $"dark chrome should be dark, was {darkColor}");
    }
}
