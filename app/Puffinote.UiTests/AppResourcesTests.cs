using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The code-based <see cref="App"/> must supply the resources the embedded Cellestra editor depends on
/// (its standalone app defines them in App.axaml). Without the <c>Chrome*</c> brushes a spreadsheet opens
/// with unstyled, transparent chrome — which reads as "the editor didn't open". These assert against null
/// drawing, which never realizes the brushes, so they're what the tests would otherwise miss.
/// </summary>
public sealed class AppResourcesTests
{
    [AvaloniaTheory]
    [InlineData("ChromeBackground")]
    [InlineData("ChromeAltBackground")]
    [InlineData("ChromeBorder")]
    public void App_provides_the_embedded_editor_chrome_brushes(string key)
    {
        Assert.True(
            Application.Current!.TryGetResource(key, ThemeVariant.Dark, out var value),
            $"App is missing the '{key}' resource the embedded spreadsheet chrome binds to.");
        Assert.IsAssignableFrom<IBrush>(value);
    }

    [AvaloniaFact]
    public void App_provides_the_ColorPicker_control_theme_for_format_cells()
    {
        // The embedded Cellestra Format Cells dialog instantiates a ColorPicker; without its Fluent control
        // theme included that dialog would fail for lack of a template.
        Assert.True(
            Application.Current!.TryGetResource(typeof(ColorPicker), ThemeVariant.Dark, out var theme),
            "App is missing the ColorPicker control theme the embedded Format Cells dialog needs.");
        Assert.IsType<ControlTheme>(theme);
    }
}
