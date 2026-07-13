using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Puffinote.Components.Controls.CommandBar;
using Typage.Desktop.Services;
using Typage.Desktop.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The editor's command bar rides the shared <see cref="AdaptiveCommandBar"/>, which paints its chrome from
/// the host's <c>ChromeBackground</c>/<c>ChromeBorder</c> resources. Puffinote's App publishes them; this
/// shell must too, or the bar draws with no background and the window's near-black surface shows through —
/// the same bar reading black here and grey in Puffinote. The colour is the family's dark chrome, the value
/// the sibling shells give their hosted editors.
/// </summary>
public class ChromeResourcesTests
{
    private static readonly Color FamilyDarkChrome = Color.FromRgb(0x25, 0x25, 0x26);

    [AvaloniaFact]
    public void Command_bar_paints_the_familys_chrome_background()
    {
        var settings = new SettingsService();
        settings.Settings.ShowToolbar = true;
        var window = new MainWindow(settings);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The bar's outer Border is what carries the chrome; it binds to the host's resources on attach.
        var bar = window.EditorView.CommandBar;
        var chrome = Assert.IsType<Border>(((AdaptiveCommandBar)bar.Content!).Content);

        var background = Assert.IsAssignableFrom<ISolidColorBrush>(chrome.Background);
        Assert.Equal(FamilyDarkChrome, background.Color);
        Assert.IsAssignableFrom<IBrush>(chrome.BorderBrush);
    }

    [AvaloniaFact]
    public void Command_bar_and_status_bar_share_one_chrome_brush()
    {
        // Same brush instance, not merely the same colour: ApplyChromeColors re-tints it on a theme change,
        // so the two chrome strips can never drift apart (light, dark, or high contrast).
        var window = new MainWindow(new SettingsService());
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.TryGetResource("ChromeBackground", window.ActualThemeVariant, out var shellBrush));
        var chrome = Assert.IsType<Border>(((AdaptiveCommandBar)window.EditorView.CommandBar.Content!).Content);
        Assert.Same(shellBrush, chrome.Background);
    }
}
