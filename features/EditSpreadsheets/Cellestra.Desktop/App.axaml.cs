using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Puffinote.Components.Commands;
using Puffinote.Components.Theming;

namespace Cellestra.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Tokens.Current = new PuffinoteTheme(Color.FromRgb(0x2A, 0x7D, 0xE1)); // Cellestra's blue accent
        BuildApplicationMenu();
    }

    // The bold macOS application menu (next to the Apple menu) otherwise gets Avalonia.Native's
    // built-in "About Avalonia" item, which opens its own about box. Setting any NativeMenu on the
    // Application replaces those built-in items with ours; Avalonia still appends the standard
    // Services / Hide / Quit block. This has to run during Initialize, before Avalonia.Native's
    // AfterSetup builds the application-menu exporter — that exporter snapshots the menu once and
    // does not observe later changes. The Click handler resolves the live MainWindow lazily, since
    // it doesn't exist yet here but always does by the time the item can be chosen.
    private void BuildApplicationMenu()
    {
        // The command catalog is now built per-editor (proposal 0001 §5), and no editor exists yet at
        // Initialize time — so the About item resolves the live editor's catalog lazily at click time and
        // runs the "help.about" command against it. The header is fixed (the standard app-menu About item).
        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem("About Cellestra")
        {
            Command = new RelayCommand(() =>
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow { Editor: { } editor } })
                {
                    editor.Commands.ById("help.about")?.Execute();
                }
            }),
        });
        NativeMenu.SetMenu(this, menu);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
