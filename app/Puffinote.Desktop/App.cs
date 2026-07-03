using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;

namespace Puffinote.Desktop;

/// <summary>
/// The application object. Built entirely in code (no XAML) — matching the sibling shells — so Puffinote
/// carries no compiled-resource dependencies and instantiates cleanly under the headless UI tests.
/// </summary>
public class App : Application
{
    // Puffinote's own accent — a puffin-beak orange — distinguishing the composite shell from the two
    // blue editors it hosts (Typage 0x579DF6, Cellestra 0x2A7DE1). Chrome reads it through Tokens.
    private static readonly Color Accent = Color.FromRgb(0xE8, 0x6A, 0x2B);

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark; // a dark shell to sit alongside the dark editors
        Tokens.Current = new PuffinoteTheme(Accent);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var notebook = new NotebookService();
            var window = new MainWindow(notebook);
            desktop.MainWindow = window;

            // Open a notebook (folder) passed on the command line.
            var args = desktop.Args ?? Array.Empty<string>();
            if (args.Length > 0 && Directory.Exists(args[0]))
            {
                window.OpenNotebook(args[0]);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
