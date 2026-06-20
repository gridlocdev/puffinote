using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;

namespace Cellestra.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
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
        if (AppCommands.ById("help.about") is not { } about)
        {
            return;
        }

        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem(about.Label)
        {
            Command = new RelayCommand(() =>
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow { Editor: { } editor } })
                {
                    about.Execute(editor);
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
