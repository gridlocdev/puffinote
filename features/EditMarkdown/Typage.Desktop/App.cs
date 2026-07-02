using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Puffinote.Components.Theming;
using Typage.Desktop.Services;
using Typage.Desktop.Views;

namespace Typage.Desktop;

/// <summary>
/// The application object. Built entirely in code (no XAML) so the standalone shell carries no
/// compiled-resource dependencies and instantiates cleanly under the headless UI tests.
/// </summary>
public class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark; // a dark shell to sit alongside the dark editor
        Tokens.Current = new PuffinoteTheme(Color.FromRgb(0x57, 0x9D, 0xF6)); // Typage's blue accent
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new SettingsService();
            var window = new MainWindow(settings);
            desktop.MainWindow = window;

            // Open a file passed on the command line (file association / "Open With" / CLI arg).
            var args = desktop.Args ?? Array.Empty<string>();
            if (args.Length > 0 && File.Exists(args[0]))
            {
                window.OpenPath(args[0]);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
