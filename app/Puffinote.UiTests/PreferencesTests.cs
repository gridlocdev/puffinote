using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;
using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Typage.Control.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// Preferences (PRD §7.5): one tab per app, each writing that app's own config file. Two things are worth
/// pinning. Puffinote's own tab is the <b>override</b> layer, and an override is opt-in — left off, a note
/// keeps whatever Typage's config says, and turning it on takes effect on the open pane without a restart.
/// The editors' tabs are the <b>inherited</b> layer — they edit the very file the standalone app reads, and
/// they reach the open pane the same way.
/// </summary>
public sealed class PreferencesTests
{
    /// <summary>
    /// Start every test from a pristine set of config files. <see cref="TestConfigIsolation"/> redirects the
    /// config directory once per <i>run</i>, which keeps the developer's real config safe but does not
    /// separate the tests from each other — so the test that turns the tab-width override on and saves it
    /// would leak that override into the next test, which expects to inherit Typage's own defaults. xUnit
    /// builds a fresh instance per test, so clearing the files here makes each one independent of the order
    /// they happen to run in.
    /// </summary>
    public PreferencesTests()
    {
        foreach (var path in ConfigFiles)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Every file the dialog can write, in tab order.</summary>
    private static string[] ConfigFiles =>
    [
        PuffinoteSettings.UserPath,
        NoteConfig.DefaultPath,
        SheetConfig.DefaultPath,
        PlainTextConfig.DefaultPath,
        BoardConfig.DefaultPath,
    ];

    /// <summary>The overlay currently showing something. The window has more than one — the shell's own,
    /// and the active pane's editor overlay nested inside the tab host — so pick by what's open.</summary>
    private static OverlayHost Overlay(MainWindow window) =>
        window.GetVisualDescendants().OfType<OverlayHost>().Single(h => h.IsOpen);

    private static void ClickOk(OverlayHost overlay) =>
        overlay.GetLogicalDescendants().OfType<Button>().First(b => b.IsDefault)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>The open Preferences dialog's tab strip.</summary>
    private static TabbedSettingsView Tabs(OverlayHost overlay) =>
        overlay.GetLogicalDescendants().OfType<TabbedSettingsView>().Single();

    /// <summary>The form on the tab titled <paramref name="title"/>.</summary>
    private static SettingsEditorView Tab(OverlayHost overlay, string title) =>
        Tabs(overlay).Tabs.Single(t => t.Title == title).Form;

    /// <summary>A labelled row's editor — the spinner or drop-down beside the label, whichever was asked for.</summary>
    private static T Row<T>(SettingsEditorView form, string label)
        where T : Control =>
        form.GetLogicalDescendants().OfType<Grid>()
            .Where(grid => grid.Children.OfType<TextBlock>().Any(text => text.Text == label))
            .SelectMany(grid => grid.Children.OfType<T>())
            .First();

    /// <summary>A checkbox row — those carry their own label instead of taking a label column.</summary>
    private static CheckBox Toggle(SettingsEditorView form, string label) =>
        form.GetLogicalDescendants().OfType<CheckBox>().First(box => (string?)box.Content == label);

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

            var general = Tab(overlay, "General");
            Toggle(general, "Override the note editor's tab width").IsChecked = true;
            Row<NumericUpDown>(general, "Tab width").Value = 8m;
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

            var general = Tab(overlay, "General");
            Toggle(general, "Override the note editor's tab width").IsChecked = true;
            Row<NumericUpDown>(general, "Tab width").Value = 8m;
            overlay.TryCancel(); // Esc / backdrop
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(4, view.Editor.IndentWidth);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // ---- one tab per app ---------------------------------------------------------------------------

    [AvaloniaFact]
    public void The_dialog_has_a_tab_per_app_named_for_what_it_changes()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();
        window.InvokeShellForTest("shell.preferences");
        Dispatcher.UIThread.RunJobs();

        var tabs = Tabs(Overlay(window));

        // Named by purpose, not by program: nobody looking for how their notes indent knows to look
        // under "Typage". Puffinote's own settings lead, as the layer that overrides the rest.
        Assert.Equal(
            ["General", "Notes", "Spreadsheets", "Plain Text", "Tasks"],
            tabs.Tabs.Select(tab => tab.Title));

        Assert.Equal(ConfigFiles, tabs.Tabs.Select(tab => tab.ConfigPath));
        Assert.Equal(0, tabs.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task Editing_the_notes_tab_writes_typages_own_file_and_reaches_the_open_note()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-prefs-notes");
        try
        {
            var note = Path.Combine(dir.FullName, "note.md");
            File.WriteAllText(note, "# hi");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(note);

            var view = Assert.IsType<MarkdownEditorView>(window.ActiveEditorViewForTest);
            Assert.Equal(4, view.Editor.IndentWidth); // Typage's own default, inherited

            window.InvokeShellForTest("shell.preferences");
            Dispatcher.UIThread.RunJobs();
            var overlay = Overlay(window);

            var notes = Tab(overlay, "Notes");
            Row<NumericUpDown>(notes, "Tab width").Value = 6m;
            Row<NumericUpDown>(notes, "Reading width").Value = 900m;
            ClickOk(overlay);
            Dispatcher.UIThread.RunJobs();

            // It reached the note already open — not just the next one (PRD §7.5, applied live)…
            Assert.Equal(6, view.Editor.IndentWidth);
            Assert.Equal(900, view.Editor.MaxContentWidth);

            // …and it landed in Typage's own config file, the one standalone Typage reads.
            Assert.Contains("\"TabWidth\": 6", File.ReadAllText(NoteConfig.DefaultPath), StringComparison.Ordinal);
            Assert.Equal(6, NoteConfig.Load().TabWidth);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Editing_the_plain_text_tab_reaches_an_open_text_file()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-prefs-text");
        try
        {
            var log = Path.Combine(dir.FullName, "notes.txt");
            File.WriteAllText(log, "a long line");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(log);

            var view = Assert.IsType<Plaintain.Control.Views.PlainTextEditorView>(window.ActiveEditorViewForTest);
            Assert.False(view.Editor.WordWrap); // Plaintain's own default, inherited

            window.InvokeShellForTest("shell.preferences");
            Dispatcher.UIThread.RunJobs();
            var overlay = Overlay(window);

            Toggle(Tab(overlay, "Plain Text"), "Wrap long lines").IsChecked = true;
            ClickOk(overlay);
            Dispatcher.UIThread.RunJobs();

            Assert.True(view.Editor.WordWrap);
            Assert.True(PlainTextConfig.Load().WordWrap);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void Cancelling_writes_no_editors_config_file_at_all()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();
        window.InvokeShellForTest("shell.preferences");
        Dispatcher.UIThread.RunJobs();
        var overlay = Overlay(window);

        Toggle(Tab(overlay, "Plain Text"), "Wrap long lines").IsChecked = true;
        Row<NumericUpDown>(Tab(overlay, "Notes"), "Tab width").Value = 7m;
        overlay.TryCancel();
        Dispatcher.UIThread.RunJobs();

        Assert.All(ConfigFiles, path => Assert.False(File.Exists(path)));
    }

    [AvaloniaFact]
    public void Accepting_without_an_edit_leaves_untouched_apps_without_a_config_file()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();
        window.InvokeShellForTest("shell.preferences");
        Dispatcher.UIThread.RunJobs();
        var overlay = Overlay(window);

        Row<NumericUpDown>(Tab(overlay, "Notes"), "Tab width").Value = 7m;
        ClickOk(overlay);
        Dispatcher.UIThread.RunJobs();

        // Visiting a dialog is not a reason for an app you've never opened to acquire a config file.
        Assert.True(File.Exists(NoteConfig.DefaultPath));
        Assert.False(File.Exists(SheetConfig.DefaultPath));
        Assert.False(File.Exists(PlainTextConfig.DefaultPath));
        Assert.False(File.Exists(BoardConfig.DefaultPath));
    }

    // ---- the escape hatch: edit the file itself -----------------------------------------------------

    [AvaloniaFact]
    public void A_tab_opens_its_config_file_creating_one_when_the_app_has_never_written_it()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();
        window.InvokeShellForTest("shell.preferences");
        Dispatcher.UIThread.RunJobs();
        var overlay = Overlay(window);

        var tabs = Tabs(overlay);
        tabs.SelectedIndex = tabs.Tabs.Count - 1; // Tasks
        Dispatcher.UIThread.RunJobs();

        Assert.False(File.Exists(BoardConfig.DefaultPath));

        // Labelled by folder, not just file name: all five apps call their file settings.json.
        Click(overlay.GetLogicalDescendants().OfType<Button>()
            .Single(b => (string?)b.Content == "Edit Pivotask/settings.json…"));
        Dispatcher.UIThread.RunJobs();

        // "Open the config file" answered with "there is no such file" would be a dead end, so it makes one.
        Assert.True(File.Exists(BoardConfig.DefaultPath));
        Assert.Equal(BoardConfig.DefaultPath, window.LastConfigFileOpenedForTest);

        // …and handing the file to an editor is not the same as accepting the dialog.
        overlay.TryCancel();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new BoardConfig(), BoardConfig.Load());
    }
}
