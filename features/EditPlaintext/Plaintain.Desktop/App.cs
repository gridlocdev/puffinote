using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Plaintain.Desktop.Views;
using Puffinote.Components.Core.Documents;
using Puffinote.Components.Input;
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
        Styles.Add(CaretBlink.TextBoxStyles()); // find bar and dialogs blink at the family rate, like the surface
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
            // Any document lock this process takes is removed before it exits, down every path —
            // including the ones that never close a window. Plaintain takes none today, so the sweep is
            // empty; it is installed so the guarantee belongs to the family rather than to whichever
            // app remembered it. See DocumentLocks.
            DocumentLocks.Install("Plaintain");
            desktop.Exit += (_, _) => DocumentLocks.ReleaseAll();

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
