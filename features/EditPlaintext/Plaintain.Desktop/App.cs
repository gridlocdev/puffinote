using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
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
        RequestedThemeVariant = ThemeVariant.Dark;
        Tokens.Current = new PuffinoteTheme(Color.FromRgb(0x8B, 0x8B, 0x8B)); // grey: plaintext has no opinion
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
