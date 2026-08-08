using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Cellestra.Desktop.Views;
using Puffinote.Components.Commands;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Preferences: the catalog command, and the dialog over the options the host keeps in Cellestra's
/// settings file. The recent-files list and the remembered window size are deliberately absent — they're
/// session state, and the window size in particular is rewritten from the real window on close, so a
/// value typed into a form could only ever be discarded.
/// </summary>
public class PreferencesTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cellestra-prefs-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    /// <summary>A window on a scratch settings file — accepting the dialog really does persist, and it
    /// must not be the developer's own profile it persists into.</summary>
    private MainWindow ScratchWindow() => new(new Cellestra.Desktop.Services.SettingsService(_path));

    [Fact]
    public void Preferences_is_a_catalog_command_on_the_platform_chord()
    {
        // Metadata only — build the catalog against a no-op environment (no MainWindow needed).
        var command = FakeAppEnvironment.Catalog.ById("app.preferences");
        Assert.NotNull(command);
        Assert.Equal("Preferences…", command!.Title);

        // ⌘, — the platform's Preferences chord. Cellestra is macOS-only, so assert the macOS rendering
        // explicitly (the test host is Linux).
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Meta), command.Shortcut!.Value.ToGesture(mac: true));
        Assert.Equal("⌘,", ShortcutFormatter.Format(command.Shortcut!.Value, mac: true));
    }

    [AvaloniaFact]
    public void Accepting_the_dialog_applies_the_edits_and_asks_the_host_to_persist()
    {
        var window = ScratchWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var persisted = 0;
        window.Editor.PreferencesChanged += (_, _) => persisted++;

        window.Editor.ShowPreferences();
        Dispatcher.UIThread.RunJobs();

        var overlay = window.Editor.OverlayHost;
        overlay.GetLogicalDescendants().OfType<NumericUpDown>().First().Value = 150m; // zoom, as a percent
        overlay.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true; // collapse the bar
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1.0, window.Editor.Zoom); // nothing applied while the dialog is still open

        // The dialog's default button is OK; clicking it resolves the awaited FormDialog.
        overlay.GetLogicalDescendants().OfType<Button>().First(b => b.IsDefault).RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1.5, window.Editor.Zoom, precision: 3);
        Assert.True(window.Editor.CommandBarCollapsed);
        Assert.Equal(1, persisted);
    }

    [AvaloniaFact]
    public void Cancelling_the_dialog_leaves_every_setting_alone()
    {
        var window = ScratchWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var persisted = 0;
        window.Editor.PreferencesChanged += (_, _) => persisted++;

        window.Editor.ShowPreferences();
        Dispatcher.UIThread.RunJobs();

        var overlay = window.Editor.OverlayHost;
        var zoomBefore = window.Editor.Zoom;
        overlay.GetLogicalDescendants().OfType<NumericUpDown>().First().Value = 300m;
        overlay.TryCancel(); // Esc / backdrop
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(zoomBefore, window.Editor.Zoom);
        Assert.Equal(0, persisted);
    }
}
