using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Puffinote.Components.Core.Documents;
using Puffinote.Components.Input;
using Puffinote.Components.Shell;
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

        // Every TextBox in the shell — search, rename, the embedded editors' own fields — blinks at the same
        // rate the embedded custom surfaces do (CaretBlink). The shell is where the mismatch was most visible:
        // one window, a spreadsheet cell in one tab and a Markdown document in the next.
        Styles.Add(CaretBlink.TextBoxStyles());

        // The embedded Cellestra editor uses a ColorPicker (Format Cells) — supply its control theme, exactly
        // as the standalone Cellestra app does in its App.axaml, or that dialog fails for lack of a template.
        Styles.Add(new StyleInclude(new Uri("avares://Puffinote/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml"),
        });

        Tokens.Current = new PuffinoteTheme(Accent);

        // No variant is requested here: the window applies the saved appearance (Preferences ▸ General ▸
        // Appearance ▸ Theme) as it opens, and that setting defaults to following the system — which is
        // what leaving RequestedThemeVariant unset already does, so the moment before the window exists is
        // spent in the OS's appearance rather than in a dark one we would then have to switch away from.
        Tokens.Follow(this);

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

        // The macOS application menu, as Cellestra has had it: the app's own name in the menu bar and its
        // own About screen, rather than Avalonia's. Has to be here, in Initialize — see MacAppMenu.
        MacAppMenu.Install(this, "Puffinote", ShowAbout);
    }

    /// <summary>
    /// The About screen, resolved through the live window so it lands on that window's overlay. Nothing
    /// exists yet when the menu is built, which is why this is a callback rather than a reference.
    /// </summary>
    private void ShowAbout()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
        {
            window.ShowAbout();
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Any document lock this process takes is removed before it exits, down every path —
            // including the ones that never close a window. The shell takes none today (its sheet panes
            // open a workbook without claiming it — see docs/TODO.md); it is installed so the guarantee
            // belongs to the family rather than to whichever app remembered it. See DocumentLocks.
            DocumentLocks.Install("Puffinote");
            desktop.Exit += (_, _) => DocumentLocks.ReleaseAll();

            var notebook = new NotebookService();
            var window = new MainWindow(notebook);
            desktop.MainWindow = window;

            // Open what the command line named — `puffinote <notebook>`, or `puffinote <file>` for a
            // document inside one; otherwise reopen the last notebook from the previous session (PRD §5),
            // prompting if that folder has since moved.
            if (PathToOpen(desktop.Args ?? []) is { } path)
            {
                window.OpenFromDesktop(path);
            }
            else
            {
                window.RestoreLastNotebook();
            }

            // The command line is not how a Mac hands an app a document: Finder's "Open With", a
            // double-click, and `open -a Puffinote <path>` all arrive as an Apple Event that argv never
            // sees — which is what left the shell on its remembered notebook with the file dropped on
            // the floor. See DocumentActivation.
            DocumentActivation.Install(this, paths =>
            {
                if (PathToOpen(paths) is { } path)
                {
                    window.OpenFromDesktop(path);
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The path to open, or null to leave the shell on the notebook it would otherwise restore. A folder
    /// and a file both count here — unlike the editors, the shell's document is a notebook, and a file is
    /// how someone names one document inside it. Pure and internal so both arrival routes are testable
    /// without a window.
    /// </summary>
    internal static string? PathToOpen(IReadOnlyList<string?>? candidates) =>
        DocumentActivation.FirstExistingPath(candidates);
}
