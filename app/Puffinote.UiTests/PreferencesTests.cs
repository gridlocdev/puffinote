using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Puffinote.Components.Overlays;
using Puffinote.Components.Shell;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Typage.Control.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// Preferences over Puffinote's own config (PRD §7.5) — the per-user override layer, not the editors'
/// settings. The behaviour worth pinning is that an override is opt-in: left off, a note keeps whatever
/// Typage's own config says, and turning it on takes effect on the open pane without a restart.
/// </summary>
public sealed class PreferencesTests
{
    /// <summary>
    /// Start every test from a pristine override layer. <see cref="TestConfigIsolation"/> redirects the
    /// config directory once per <i>run</i>, which keeps the developer's real config safe but does not
    /// separate the tests from each other — so the test that turns the tab-width override on and saves it
    /// would leak that override into the next test, which expects to inherit Typage's own defaults. xUnit
    /// builds a fresh instance per test, so clearing the file here makes each one independent of the order
    /// they happen to run in.
    /// </summary>
    public PreferencesTests()
    {
        var settings = SettingsStore.AppConfigPath("Puffinote", "settings.json");
        if (File.Exists(settings))
        {
            File.Delete(settings);
        }
    }

    /// <summary>The overlay currently showing something. The window has more than one — the shell's own,
    /// and the active pane's editor overlay nested inside the tab host — so pick by what's open.</summary>
    private static OverlayHost Overlay(MainWindow window) =>
        window.GetVisualDescendants().OfType<OverlayHost>().Single(h => h.IsOpen);

    private static void ClickOk(OverlayHost overlay) =>
        overlay.GetLogicalDescendants().OfType<Button>().First(b => b.IsDefault)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void Preferences_is_a_shell_command_on_the_platform_chord()
    {
        var window = new MainWindow(new NotebookService());
        var command = window.ShellCommandsForTest.Single(c => c.Id == "shell.preferences");

        Assert.Equal("Preferences…", command.Title);
        Assert.Equal(Key.OemComma, command.Shortcut!.Value.Key);
        Assert.True(command.Shortcut!.Value.UsesCommand);

        // Bound as an ordinary (rebindable, non-reserved) key, so the shortcuts editor can move it.
        Assert.False(ShellCommandCatalog.IsReserved("shell.preferences"));
        Assert.Contains(window.KeyBindings, b => b.Gesture is { } g && g.Key == Key.OemComma);
    }

    [AvaloniaFact]
    public async Task Turning_the_tab_width_override_on_reaches_an_open_note_without_a_restart()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-prefs");
        try
        {
            var note = Path.Combine(dir.FullName, "note.md");
            File.WriteAllText(note, "# hi");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(note);

            var view = Assert.IsType<MarkdownEditorView>(window.ActiveEditorViewForTest);
            Assert.Equal(4, view.Editor.IndentWidth); // inherited from Typage's own defaults

            window.InvokeShellForTest("shell.preferences");
            Dispatcher.UIThread.RunJobs();
            var overlay = Overlay(window);

            overlay.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true; // override it
            overlay.GetLogicalDescendants().OfType<NumericUpDown>().Single().Value = 8m;
            Assert.Equal(4, view.Editor.IndentWidth); // nothing applied while the dialog is open

            ClickOk(overlay);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(8, view.Editor.IndentWidth);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Cancelling_leaves_the_overrides_alone()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-prefs-cancel");
        try
        {
            var note = Path.Combine(dir.FullName, "note.md");
            File.WriteAllText(note, "# hi");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(note);

            var view = Assert.IsType<MarkdownEditorView>(window.ActiveEditorViewForTest);

            window.InvokeShellForTest("shell.preferences");
            Dispatcher.UIThread.RunJobs();
            var overlay = Overlay(window);

            overlay.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true;
            overlay.GetLogicalDescendants().OfType<NumericUpDown>().Single().Value = 8m;
            overlay.TryCancel(); // Esc / backdrop
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(4, view.Editor.IndentWidth);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
