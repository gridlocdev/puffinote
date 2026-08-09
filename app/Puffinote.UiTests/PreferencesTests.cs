using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Commands;
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
    /// <para>
    /// The per-user app state goes with them, for the same reason and one more: closing this dialog records
    /// which tab was showing, so without this every test after the first would open on whichever tab its
    /// predecessor left behind — and that file, unlike the config directory, also outlives the run.
    /// </para>
    /// </summary>
    public PreferencesTests()
    {
        foreach (var path in ConfigFiles)
        {
            Forget(path);
        }

        Forget(AppState.PathOverrideForTest);

        static void Forget(string? path)
        {
            if (path is not null && File.Exists(path))
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

    private const string TabWidthOverride = "Override the note editor's tab width";
    private const string ShowExtensions = "Show file extensions in the file tree";

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

    /// <summary>
    /// ⌘K's list is where someone looks for a command they haven't memorised, so Preferences has to be
    /// <em>in</em> it — and to carry its chord, which is how the palette teaches ⌘, rather than only
    /// answering to it. With no tab open the merged palette is the shell's catalog alone, so the one row
    /// here is Puffinote's own; an active Cellestra tab adds the editor's under "Editor", which is the
    /// point of the merge rather than a duplicate.
    /// </summary>
    [AvaloniaFact]
    public void Preferences_is_offered_by_the_command_menu_with_its_chord()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();

        window.ShellCommandsForTest.Single(c => c.Id == "shell.commandMenu").Execute();
        Dispatcher.UIThread.RunJobs();

        var overlay = window.GetVisualDescendants().OfType<OverlayHost>().Single(h => h.IsOpen);
        var palette = overlay.GetVisualDescendants().OfType<PaletteListView>().Single();
        var entry = Assert.Single(palette.ListedEntries, e => e.Title == "Preferences…");

        Assert.Equal(ShortcutFormatter.Format(Shortcut.Cmd(Key.OemComma)), entry.Shortcut);
        Assert.Equal(CommandMerge.ShellCategory, entry.Category);
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
            Toggle(general, TabWidthOverride).IsChecked = true;
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
            Toggle(general, TabWidthOverride).IsChecked = true;
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

    /// <summary>
    /// The appearance is asked once, on Puffinote's own tab, and applies to the whole shell — editors
    /// included. The editors' tabs deliberately don't repeat it: a window showing a note beside a sheet
    /// can't be half light, so offering a theme per embedded editor would be offering a choice the shell
    /// can't honour. Their own files keep their own theme setting, untouched, for running standalone.
    /// </summary>
    [AvaloniaFact]
    public void The_theme_is_asked_once_on_the_General_tab_and_applies_to_the_whole_shell()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();
        window.InvokeShellForTest("shell.preferences");
        Dispatcher.UIThread.RunJobs();

        var overlay = Overlay(window);
        var tabs = Tabs(overlay);

        // Exactly one Theme row in the whole dialog, and it is General's.
        Assert.Equal(
            ["General"],
            tabs.Tabs.Where(t => t.Form.Fields.Any(f => f.Label == "Theme")).Select(t => t.Title));

        var theme = Row<ComboBox>(Tab(overlay, "General"), "Theme");
        Assert.Equal(ThemePreferences.Labels, theme.ItemsSource);
        Assert.Equal((int)ThemePreference.System, theme.SelectedIndex); // nobody has said otherwise yet

        theme.SelectedIndex = (int)ThemePreference.Light;
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(ThemeVariant.Light, Application.Current!.RequestedThemeVariant); // not while it's open

        ClickOk(overlay);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(ThemeVariant.Light, Application.Current!.ActualThemeVariant);
        Assert.Equal(ThemeVariant.Light, Tokens.Variant); // and the shell's chrome tokens went with it
        Assert.Equal(ThemePreference.Light, PuffinoteSettings.LoadUser().Theme); // remembered for next launch
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

    // ---- The optional tab bar -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Turning_the_tab_bar_off_in_Preferences_hides_it_and_is_remembered()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-prefs-tabbar");
        try
        {
            var note = Path.Combine(dir.FullName, "note.md");
            File.WriteAllText(note, "# hi");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(note);
            Assert.True(window.IsTabStripVisibleForTest);

            window.InvokeShellForTest("shell.preferences");
            Dispatcher.UIThread.RunJobs();
            var overlay = Overlay(window);

            // The tab strip is Puffinote's own chrome, so it lives on the General tab.
            Toggle(Tab(overlay, "General"), "Show the tab bar").IsChecked = false;
            Assert.True(window.IsTabStripVisibleForTest); // nothing applied while the dialog is open

            ClickOk(overlay);
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.IsTabStripVisibleForTest);

            // Unlike the session command, this one is a preference: it survives into the next window.
            Assert.False(PuffinoteSettings.LoadUser().ShowTabBar);
            var next = new MainWindow(new NotebookService());
            next.Show();
            next.OpenNotebook(dir.FullName);
            await next.OpenFileAsync(note);
            Assert.False(next.IsTabStripVisibleForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Toggle_Tab_Bar_changes_this_session_only_and_writes_no_preference()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-tabbar-command");
        try
        {
            var note = Path.Combine(dir.FullName, "note.md");
            File.WriteAllText(note, "# hi");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(note);

            var command = window.ShellCommandsForTest.Single(c => c.Id == "shell.toggleTabBar");
            Assert.Equal("Toggle Tab Bar", command.Title);

            // Unbound by default and rebindable, so it can't collide with an editor's own gesture.
            Assert.Null(command.Shortcut);
            Assert.False(ShellCommandCatalog.IsReserved("shell.toggleTabBar"));

            window.InvokeShellForTest("shell.toggleTabBar");
            Assert.False(window.IsTabStripVisibleForTest);

            // Hiding the strip to read one note doesn't quietly become the default for every future window.
            Assert.Null(PuffinoteSettings.LoadUser().ShowTabBar);

            window.InvokeShellForTest("shell.toggleTabBar");
            Assert.True(window.IsTabStripVisibleForTest);
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

            Toggle(Tab(overlay, "Plain Text"), "Wrap long lines at words").IsChecked = true;
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

        Toggle(Tab(overlay, "Plain Text"), "Wrap long lines at words").IsChecked = true;
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

    // ---- reopening where you left off ---------------------------------------------------------------

    /// <summary>Open Preferences on <paramref name="window"/> and hand back the strip it's showing.</summary>
    private static TabbedSettingsView OpenPreferences(MainWindow window)
    {
        window.InvokeShellForTest("shell.preferences");
        Dispatcher.UIThread.RunJobs();
        return Tabs(Overlay(window));
    }

    [AvaloniaFact]
    public void Preferences_reopens_on_the_tab_you_were_last_reading()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();

        // First visit: nothing is remembered yet, so it opens where a first visit should — on General.
        var tabs = OpenPreferences(window);
        Assert.Equal("General", tabs.SelectedTitle);

        tabs.SelectedIndex = 2; // Spreadsheets — go read something else, then leave
        Dispatcher.UIThread.RunJobs();
        ClickOk(Overlay(window));
        Dispatcher.UIThread.RunJobs();

        // Settings get worked through a few at a time; coming back to General each visit is a tax on the second.
        Assert.Equal("Spreadsheets", OpenPreferences(window).SelectedTitle);
    }

    [AvaloniaFact]
    public void The_remembered_tab_outlives_the_window_that_chose_it()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();

        var tabs = OpenPreferences(window);
        tabs.SelectedTitle = "Tasks";
        Dispatcher.UIThread.RunJobs();
        ClickOk(Overlay(window));
        Dispatcher.UIThread.RunJobs();

        // It's per-user app state, not per-window: it's written to disk, so the next launch reads it back.
        Assert.Equal("Tasks", AppState.Load().LastSettingsTab);

        var next = new MainWindow(new NotebookService());
        next.Show();
        Assert.Equal("Tasks", OpenPreferences(next).SelectedTitle);
    }

    [AvaloniaFact]
    public void Cancelling_still_remembers_the_tab_you_were_on()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();

        var tabs = OpenPreferences(window);
        tabs.SelectedTitle = "Plain Text";
        Row<NumericUpDown>(Tab(Overlay(window), "Notes"), "Tab width").Value = 7m;
        Dispatcher.UIThread.RunJobs();

        var overlay = Overlay(window);
        overlay.TryCancel(); // Esc / backdrop
        Dispatcher.UIThread.RunJobs();

        // Cancel abandons the *edits*; where you were reading isn't one of them — and a dialog dismissed with
        // Esc is exactly the case where coming back to the same tab matters most.
        Assert.All(ConfigFiles, path => Assert.False(File.Exists(path)));
        Assert.Equal("Plain Text", OpenPreferences(window).SelectedTitle);
    }

    [AvaloniaFact]
    public async Task Remembering_the_tab_doesnt_make_the_app_forget_its_notebook()
    {
        // Both live in the one per-user state file, written at different moments by different parts of the
        // shell — so each has to layer onto the other rather than save a whole record over it.
        var dir = Directory.CreateTempSubdirectory("puffinote-prefs-state");
        try
        {
            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            Dispatcher.UIThread.RunJobs();

            var tabs = OpenPreferences(window);
            tabs.SelectedTitle = "Notes";
            Dispatcher.UIThread.RunJobs();
            ClickOk(Overlay(window));
            Dispatcher.UIThread.RunJobs();

            var state = AppState.Load();
            Assert.Equal("Notes", state.LastSettingsTab);
            Assert.Equal(dir.FullName, state.LastNotebookPath);

            // …and opening a notebook afterwards doesn't blank the tab on the way back.
            window.OpenNotebook(dir.FullName);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Notes", AppState.Load().LastSettingsTab);

            // Opening a notebook kicks a background index rebuild that ends by writing into its .puffinote/.
            // Deleting the folder the moment the assertions pass races that write.
            await window.IndexRebuildForTest;
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void A_remembered_tab_that_no_longer_exists_opens_on_the_first_one()
    {
        // A state file written by an older version can name a tab this one doesn't have. Selecting by title
        // means that can only fail to match — where an index would confidently select the wrong subject.
        (AppState.Load() with { LastSettingsTab = "Diagrams" }).Save();

        var window = new MainWindow(new NotebookService());
        window.Show();

        Assert.Equal("General", OpenPreferences(window).SelectedTitle);
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

    [AvaloniaFact]
    public void Toggle_Tab_Bar_is_available_with_no_notebook_open()
    {
        // The strip is hidden anyway with nothing open, but the command sets what happens when you next open
        // a file — greying it out would make the state unsettable exactly on the empty shell.
        var window = new MainWindow(new NotebookService());
        var command = window.ShellCommandsForTest.Single(c => c.Id == "shell.toggleTabBar");

        Assert.True(command.IsEnabled);
    }

    // ---- The tree's file extensions -------------------------------------------------------------------

    [AvaloniaFact]
    public void Turning_the_trees_extensions_off_reaches_the_sidebar_and_is_remembered()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();
        Assert.True(window.SidebarForTest.ShowFileExtensions); // shown until someone says otherwise

        window.InvokeShellForTest("shell.preferences");
        Dispatcher.UIThread.RunJobs();
        var overlay = Overlay(window);

        // The tree is Puffinote's own chrome, so the option lives on the General tab.
        Toggle(Tab(overlay, "General"), ShowExtensions).IsChecked = false;
        Assert.True(window.SidebarForTest.ShowFileExtensions); // nothing applied while the dialog is open

        ClickOk(overlay);
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.SidebarForTest.ShowFileExtensions);

        // ...and it is a preference, so it outlives the run: the next launch reads it back off disk.
        Assert.False(PuffinoteSettings.LoadUser().ShowFileExtensions);
    }

    [AvaloniaFact]
    public void The_extensions_command_flips_them_for_this_session_only()
    {
        // The palette/menu twin of the preference: it is the "let me see the suffixes for a minute" gesture, so
        // it changes what is on screen and writes nothing — a saved setting would make it a change of mind.
        var window = new MainWindow(new NotebookService());
        window.Show();

        window.InvokeShellForTest("shell.toggleFileExtensions");
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.SidebarForTest.ShowFileExtensions);

        window.InvokeShellForTest("shell.toggleFileExtensions");
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.SidebarForTest.ShowFileExtensions);

        Assert.Null(PuffinoteSettings.LoadUser().ShowFileExtensions); // nothing was saved either way
    }

    [AvaloniaFact]
    public void A_session_flip_survives_opening_a_notebook()
    {
        // Opening a notebook re-resolves the settings (the notebook's own file can differ), which would
        // otherwise quietly undo a toggle made moments earlier — the one thing "for this session" must not do.
        var dir = Directory.CreateTempSubdirectory("puffinote-prefs-session");
        try
        {
            var window = new MainWindow(new NotebookService());
            window.Show();
            window.InvokeShellForTest("shell.toggleFileExtensions");
            Dispatcher.UIThread.RunJobs();

            window.OpenNotebook(dir.FullName);
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.SidebarForTest.ShowFileExtensions);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
