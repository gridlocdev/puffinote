using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Plaintain.Desktop.Views;
using Puffinote.Components.Theming;

namespace Plaintain.Desktop;

/// <summary>
/// The application object. Built entirely in code (no XAML), like its siblings, so the shell carries no
/// compiled-resource dependencies and instantiates cleanly under the headless UI tests.
/// </summary>
public class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Tokens.Current = new PuffinoteTheme(Color.FromRgb(0x8B, 0x8B, 0x8B)); // grey: plaintext has no opinion

        // No variant is requested here: the window applies the saved appearance (Preferences ▸ Appearance ▸
        // Theme) as it opens, and that setting defaults to following the system — which is what leaving
        // RequestedThemeVariant unset already does, so nothing flashes dark on the way to a light desktop.
        Tokens.Follow(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            // Open a file passed on the command line (file association / "Open With" / `pltn <file>`).
            var args = desktop.Args ?? [];
            if (args.Length > 0 && File.Exists(args[0]))
            {
                window.OpenPath(args[0]);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
