using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Cellestra.Core;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Cellestra.Editor;
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
    public void Active_sheet_tab_surface_is_themed_on_the_very_first_paint()
    {
        // The tab strip is built in the view's constructor, before it's attached to a visual tree — so
        // ActualThemeVariant still reports the default (light) there. The active tab must not be left
        // with that first, white surface inside a dark shell: it should repaint once the theme lands.
        var view = new SpreadsheetEditorView();
        var window = new Window { Content = view, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var dark = ((ISolidColorBrush)view.TabBackgroundForTest(0)!).Color;
        Assert.True(dark.R < 80, $"active tab should be dark in a dark shell, was {dark}");

        // And it keeps following the theme after a switch.
        window.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();

        var light = ((ISolidColorBrush)view.TabBackgroundForTest(0)!).Color;
        Assert.True(light.R > 200, $"active tab should be bright in light theme, was {light}");
    }

    [AvaloniaFact]
    public void Automatic_text_on_a_light_fill_stays_dark_in_dark_theme()
    {
        // The dark theme's body ink is pale, and a table header (or any cell the document filled light)
        // is a pale surface — so following the theme alone drew pale-on-pale. Automatic ink is the app's
        // choice, so it has to follow what is behind it, not just the theme.
        var grid = new SpreadsheetGrid();
        var window = new Window { Content = grid, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var header = CellStyle.Default
            .WithFill(Fill.Solid(new ArgbColor(Defaults.TableHeaderFillArgb)));
        var ink = grid.ResolvedTextColor(header, CellValue.Text("All Sites"));

        Assert.True(ink.R < 80, $"text on a light fill should stay dark in dark theme, was {ink}");
        Assert.True(Contrast.Ratio(
            ArgbColor.FromRgb(ink.R, ink.G, ink.B),
            new ArgbColor(Defaults.TableHeaderFillArgb)) >= 3.0);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Light_document_fills_carry_readable_text_in_either_theme(bool dark)
    {
        // Fills the app itself can write (Clean Up's header grey) plus the greys the light-theme
        // header bands use — automatic ink over any of them must clear the contrast floor in both
        // themes, or a table saved by an old build (fills baked) goes unreadable in dark mode.
        var grid = new SpreadsheetGrid();
        var window = new Window { Content = grid, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        foreach (uint fill in new[] { Defaults.TableHeaderFillArgb, 0xFFDCDCDCu })
        {
            var style = CellStyle.Default.WithFill(Fill.Solid(new ArgbColor(fill)));
            var ink = grid.ResolvedTextColor(style, CellValue.Text("Male"));
            double ratio = Contrast.Ratio(ArgbColor.FromRgb(ink.R, ink.G, ink.B), new ArgbColor(fill));
            Assert.True(ratio >= 4.5, $"ink {ink} on fill {fill:X} was only {ratio:F2}:1");
        }
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
