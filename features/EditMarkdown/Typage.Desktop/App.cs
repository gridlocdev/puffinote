using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Puffinote.Components.Core.Documents;
using Puffinote.Components.Input;
using Puffinote.Components.Shell;
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
        Styles.Add(CaretBlink.TextBoxStyles()); // find bar and dialogs blink at the family rate, like the surface
        Tokens.Current = new PuffinoteTheme(Color.FromRgb(0x57, 0x9D, 0xF6)); // Typage's blue accent

        // No variant is requested here: the window applies the saved appearance (Preferences ▸ Appearance ▸
        // Theme) as it opens, and the default of that setting is "follow the system" — which is what an
        // unset RequestedThemeVariant already means, so the split second before the window exists is spent
        // in the OS's appearance rather than in a dark one we would then have to switch away from.
        Tokens.Follow(this);

        // The macOS application menu, as Cellestra has had it: the app's own name in the menu bar and its
        // own About screen, rather than Avalonia's. Has to be here, in Initialize — see MacAppMenu.
        MacAppMenu.Install(this, "Typage", ShowAbout);
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
            // including the ones that never close a window. Typage takes none today, so the sweep is
            // empty; it is installed so the guarantee belongs to the family rather than to whichever
            // app remembered it. See DocumentLocks.
            DocumentLocks.Install("Typage");
            desktop.Exit += (_, _) => DocumentLocks.ReleaseAll();

            var settings = new SettingsService();
            var window = new MainWindow(settings);
            desktop.MainWindow = window;

            // Open a file passed on the command line — `typage <file>`, and the file association on the
            // platforms whose shell launches an app with the document in argv.
            if (DocumentToOpen(desktop.Args ?? []) is { } path)
            {
                window.OpenPath(path);
            }

            // macOS is not one of those platforms, which is what made "Open With" — and `typage <file>`,
            // which goes through Launch Services — land on an empty "Untitled" document: Finder hands a
            // *bundled* app its document as an Apple Event, and argv never sees it. See
            // DocumentActivation, which is where the whole family's half of that bargain lives.
            DocumentActivation.Install(this, files =>
            {
                if (DocumentToOpen(files) is { } path)
                {
                    window.OpenFromDesktop(path);
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The document to open, or null to stay on the document that is already there. Pure and internal so
    /// both arrival routes are testable without a window.
    /// </summary>
    internal static string? DocumentToOpen(IReadOnlyList<string?>? candidates) =>
        DocumentActivation.FirstExistingFile(candidates);
}
