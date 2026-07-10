using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
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

        // The embedded Cellestra editor uses a ColorPicker (Format Cells) — supply its control theme, exactly
        // as the standalone Cellestra app does in its App.axaml, or that dialog fails for lack of a template.
        Styles.Add(new StyleInclude(new Uri("avares://Puffinote/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml"),
        });

        RequestedThemeVariant = ThemeVariant.Dark; // a dark shell to sit alongside the dark editors
        Tokens.Current = new PuffinoteTheme(Accent);

        // The embedded Cellestra editor's chrome (formula bar, sheet-tab strip, status bar, chart border)
        // binds to these host-provided brushes via DynamicResource. Standalone Cellestra defines them in its
        // App.axaml; Puffinote is code-based, so mirror them here (values identical) — without them a sheet
        // opens with unstyled, transparent chrome and reads as "the editor didn't open".
        Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
        {
            ["ChromeBackground"] = new SolidColorBrush(Color.Parse("#252526")),
            ["ChromeAltBackground"] = new SolidColorBrush(Color.Parse("#2D2D2E")),
            ["ChromeBorder"] = new SolidColorBrush(Color.Parse("#3A3A3A")),
        };
        Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
        {
            ["ChromeBackground"] = new SolidColorBrush(Color.Parse("#FAFAFA")),
            ["ChromeAltBackground"] = new SolidColorBrush(Color.Parse("#F4F4F4")),
            ["ChromeBorder"] = new SolidColorBrush(Color.Parse("#E0E0E0")),
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var notebook = new NotebookService();
            var window = new MainWindow(notebook);
            desktop.MainWindow = window;

            // Open a notebook (folder) passed on the command line; otherwise reopen the last one from the
            // previous session (PRD §5), prompting if that folder has since moved.
            var args = desktop.Args ?? [];
            if (args.Length > 0 && Directory.Exists(args[0]))
            {
                window.OpenNotebook(args[0]);
            }
            else
            {
                window.RestoreLastNotebook();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
