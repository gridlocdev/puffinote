using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Puffinote.Components.Core.Documents;
using Puffinote.Components.Input;
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

            // Open a file passed on the command line (file association / "Open With" / CLI arg).
            var args = desktop.Args ?? [];
            if (args.Length > 0 && File.Exists(args[0]))
            {
                window.OpenPath(args[0]);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
