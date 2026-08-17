using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Cellestra.Desktop.Views;
using Puffinote.Components.Core.Documents;
using Puffinote.Components.Input;
using Puffinote.Components.Shell;
using Puffinote.Components.Theming;

namespace Cellestra.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // The cell editor and formula bar are TextBoxes; this puts their caret on the family's blink rate,
        // the same one Typage's and Plaintain's custom surfaces paint themselves with. Added here rather
        // than in App.axaml so the rate has exactly one definition (CaretBlink) for all five apps.
        Styles.Add(CaretBlink.TextBoxStyles());
        Tokens.Current = new PuffinoteTheme(Color.FromRgb(0x2A, 0x7D, 0xE1)); // Cellestra's blue accent

        // App.axaml requests the Default variant — the OS's — and the window narrows that to the saved
        // preference as it opens. Following the resolved variant from here is what keeps the family's
        // chrome tokens in step with it, including when the OS flips appearance mid-session.
        Tokens.Follow(this);
        BuildApplicationMenu();
    }

    // The bold macOS application menu, from the family's shared helper (MacAppMenu — which is where the
    // "why any NativeMenu at all" and "why during Initialize" reasoning now lives, since four more apps
    // needed the same answer). The command catalog is built per-editor (proposal 0001 §5) and no editor
    // exists yet here, so About resolves the live editor's catalog lazily at click time.
    private void BuildApplicationMenu() => MacAppMenu.Install(this, "Cellestra", () =>
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow { Editor: { } editor } })
        {
            editor.Commands.ById("help.about")?.Execute();
        }
    });

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Whatever this process locks, it unlocks. Cellestra places a LibreOffice-style .~lock file
            // beside every open spreadsheet, and until now the only thing that removed one was the
            // window's Closing handler — so quitting from the dock, logging out, or killing the app left
            // the document looking busy to LibreOffice and to Cellestra itself, permanently. Installed
            // before the window so a document opened from argv is covered too.
            DocumentLocks.Install("Cellestra");
            desktop.Exit += (_, _) => DocumentLocks.ReleaseAll();

            var window = new MainWindow();
            desktop.MainWindow = window;

            // Open a spreadsheet passed on the command line — `cellestra <file>`, and the file
            // association on the platforms whose shell launches an app with the document in argv.
            // Fire-and-forget is deliberate: OpenPathAsync owns its own busy overlay and error dialog,
            // and does the deserialize + recalc off the UI thread, so the window still paints while a
            // large file loads.
            if (DocumentToOpen(desktop.Args ?? []) is { } path)
            {
                _ = window.OpenPathAsync(path);
            }

            // macOS is not one of those platforms, which is what kept the .ods association the bundle
            // claims (Info.plist) landing in an empty workbook: Finder hands a *bundled* app its
            // document as an Apple Event, and argv never sees it. See DocumentActivation. OpenPathAsync
            // already guards unsaved changes, so an arrival over live work asks before replacing it.
            DocumentActivation.Install(this, files =>
            {
                if (DocumentToOpen(files) is { } path)
                {
                    _ = window.OpenPathAsync(path);
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The document to open, or null to stay on the workbook that is already there. Pure and internal so
    /// both arrival routes are testable without standing up a window.
    /// </summary>
    internal static string? DocumentToOpen(IReadOnlyList<string?>? candidates) =>
        DocumentActivation.FirstExistingFile(candidates);
}
