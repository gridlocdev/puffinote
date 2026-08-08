using System.ComponentModel;
using Avalonia;
using Avalonia.Platform.Storage;
using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;
using Puffinote.Components.Shell;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Views;

public partial class MainWindow
{
    /// <summary>
    /// The Preferences dialog: one tab per app whose settings you can reach from here (PRD §7.5) —
    /// <b>General</b> for Puffinote's own config, then <b>Notes</b>, <b>Spreadsheets</b>, <b>Plain Text</b>
    /// and <b>Tasks</b> for the four embedded editors, each writing that editor's <em>own</em> config file,
    /// the same one it reads when it runs standalone.
    /// <para>
    /// Tabs are named for what they change, not for the program that changes it: someone looking for how
    /// their notes indent is looking for "Notes", and has no reason to know a note editor is called Typage.
    /// </para>
    /// <para>
    /// General is the <b>override</b> layer and reads differently on purpose: each of its options can be
    /// left off, in which case a note behaves exactly as the Notes tab says, and turning one on is a
    /// deliberate act that wins over it. The per-notebook <c>.puffinote/settings.json</c> still wins over
    /// anything set here, so a notebook that ships its own tab width keeps it; it is edited as a file,
    /// because that is the point of it being committable.
    /// </para>
    /// <para>
    /// Not every option here overrides an editor, though: the shell's own chrome — the tab bar — has nothing
    /// underneath it to inherit from, so it is a plain preference with a default, and the Toggle Tab Bar
    /// command is the way to change it for one session without changing it for good.
    /// </para>
    /// <para>
    /// Session state — the last notebook, the sidebar width, the per-notebook recent list — is not a
    /// preference and isn't here; it's recorded as you work.
    /// </para>
    /// </summary>
    private async void ShowSettings()
    {
        if (_overlayHost.IsOpen)
        {
            return;
        }

        // Edited as loose values rather than on the record: PuffinoteSettings is immutable, and "override
        // or inherit" is two controls over one nullable field, which only reads back as a pair.
        var overrideTabWidth = _settings.TabWidth is not null;
        double tabWidth = _settings.TabWidth ?? new NoteConfig().TabWidth;
        var showExtensions = _settings.ShowFileExtensionsOrDefault;

        // The one appearance choice in the dialog, and deliberately the only one: it is set here, in General,
        // for the whole shell, and the editors' tabs below don't repeat it. A window showing a note beside a
        // sheet cannot be half light, and an editor's own theme setting only means something when that editor
        // is the app — so it stays in its own file (untouched by a save from here) for when it runs standalone.
        var theme = _settings.ThemeOrDefault;

        // Not an override, so no paired "override it?" checkbox: the tab strip is Puffinote's own chrome and
        // there is no editor setting underneath it to inherit. Unset simply means shown.
        var showTabBar = _settings.ShowTabBar ?? true;

        // Nor is this one. Saving an open document is the shell's own job here — the tab host calls each
        // pane's SaveAsync — so an editor's standalone interval has no say over what Puffinote's tabs do,
        // and unset simply means the family default.
        var autoSaveSeconds = _settings.AutoSaveSecondsOrDefault;

        // Each editor's own file, read fresh: it may have changed since the tabs were opened — the
        // standalone app may have written it, or the user may have edited it by hand from this very dialog.
        var notes = NoteConfig.Load();
        var sheets = SheetConfig.Load();
        var plainText = PlainTextConfig.Load();
        var tasks = BoardConfig.Load();

        // Snapshots to compare against on the way out. Only a file whose values actually changed is
        // rewritten, so opening Preferences and pressing OK doesn't conjure config files for editors the
        // user has never even opened.
        var before = (Notes: notes with { }, Sheets: sheets with { }, PlainText: plainText with { }, Tasks: tasks with { });

        var view = new TabbedSettingsView(
        [
            new SettingsTab("General", new SettingsEditorView(
            [
                new SettingsSection("Appearance",
                [
                    ChoiceSetting.ForEnum<ThemePreference>("Theme", () => theme, v => theme = v,
                        ThemePreferences.Labels,
                        ThemePreferences.Hint + " It sets the whole window, editors included."),
                    new ToggleSetting("Show the tab bar",
                        () => showTabBar, v => showTabBar = v,
                        "Off hides the strip of open documents. They stay open — switch with ⌘⇧[ / ⌘⇧] or ⌘O."),
                ]),
                new SettingsSection("Sidebar",
                [
                    new ToggleSetting("Show file extensions in the file tree",
                        () => showExtensions, v => showExtensions = v,
                        "Off writes each file by its name alone, leaving its glyph to say what it is."),
                ]),
                new SettingsSection("Saving",
                [
                    AutoSave.IntervalField(() => autoSaveSeconds, v => autoSaveSeconds = v),
                ]),
                new SettingsSection("Notes",
                [
                    new ToggleSetting("Override the note editor's tab width",
                        () => overrideTabWidth, v => overrideTabWidth = v,
                        "Off inherits the Notes tab's setting, so a note indents here as it does in Typage."),
                    new NumberSetting("Tab width", () => tabWidth, v => tabWidth = v, 1, 8),
                ]),
            ]), PuffinoteSettings.UserPath),
            new SettingsTab("Notes", new SettingsEditorView(notes.Fields()), NoteConfig.DefaultPath),
            new SettingsTab("Spreadsheets", new SettingsEditorView(sheets.Fields()), SheetConfig.DefaultPath),
            new SettingsTab("Plain Text", new SettingsEditorView(plainText.Fields()), PlainTextConfig.DefaultPath),
            new SettingsTab("Tasks", new SettingsEditorView(tasks.Fields()), BoardConfig.DefaultPath),
        ]);

        // How to bring each tab's file into existence, for the tab that offers to open a file the owning app
        // has never written. Keyed by the path the tab already carries, so a tab and its file can't drift.
        var createFile = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            [PuffinoteSettings.UserPath] = () => _settings.SaveUser(),
            [NoteConfig.DefaultPath] = () => NoteConfig.Load().Save(),
            [SheetConfig.DefaultPath] = () => SheetConfig.Load().Save(),
            [PlainTextConfig.DefaultPath] = () => PlainTextConfig.Load().Save(),
            [BoardConfig.DefaultPath] = () => BoardConfig.Load().Save(),
        };

        view.OpenConfigFileRequested += tab => _ = OpenConfigFileAsync(tab, createFile);

        if (!await _overlayHost.ShowModalAsync(new FormDialog("Preferences", view)))
        {
            return; // Cancel / Esc — leave every settings file untouched
        }

        view.Commit();

        // Each editor's file first: they're what Puffinote's own overrides are layered over, so writing them
        // before re-resolving is what makes the resolved config below the one the user just asked for.
        SaveIfChanged(notes, before.Notes, () => notes.Save());
        SaveIfChanged(sheets, before.Sheets, () => sheets.Save());
        SaveIfChanged(plainText, before.PlainText, () => plainText.Save());
        SaveIfChanged(tasks, before.Tasks, () => tasks.Save());

        _settings = _settings with
        {
            TabWidth = overrideTabWidth ? (int)tabWidth : null,
            ShowTabBar = showTabBar,
            ShowFileExtensions = showExtensions,
            Theme = theme,
            AutoSaveSeconds = autoSaveSeconds,
        };
        _settings.SaveUser();

        // Settling the preference outranks whatever the session's own toggle last said: someone who has just
        // answered the question in the dialog means it, and leaving the override in place would make the
        // setting they picked appear not to take.
        _extensionsThisSession = null;

        // Re-resolve against the open notebook's own file and push the result at everything that reads it, so
        // open panes pick every one of those changes up without a restart (PRD §7.5 precedence, applied live).
        ApplySettings();
    }

    /// <summary>
    /// Push the effective settings — the per-user file resolved against the open notebook's own
    /// <c>.puffinote/settings.json</c> (PRD §7.5) — at everything that reads them, so a change lands without a
    /// restart. Called at startup, on every notebook open (the notebook layer can differ), and after the dialog.
    /// </summary>
    private void ApplySettings()
    {
        var resolved = PuffinoteSettings.Resolve(_settings, _notebook.RootPath);
        _tabHost.Settings = resolved;
        _autoSave.IntervalSeconds = resolved.AutoSaveSecondsOrDefault;
        SidebarForTest.ShowFileExtensions = _extensionsThisSession ?? resolved.ShowFileExtensionsOrDefault;

        // The appearance, applied to the application rather than to this window: requesting the variant is
        // what re-resolves the chrome resources and re-tints the shared tokens, which between them are what
        // the sidebar, the tab strip and every embedded editor paint from. Nothing here has to walk the open
        // panes — each editor already follows the variant it finds itself in, because each of them has to be
        // able to follow an OS that changes appearance under it.
        if (Application.Current is { } app)
        {
            resolved.ThemeOrDefault.Apply(app);
        }
    }

    /// <summary>
    /// Show or hide the tree's file extensions <em>for this run only</em> (the command palette / View menu twin of
    /// the preference above). It writes nothing to disk: this is the "let me see the suffixes for a minute" gesture,
    /// not a change of mind, so quitting forgets it — while the preference, once set, outlives it (see the dialog).
    /// The override is remembered rather than merely poked into the sidebar so that opening another notebook, which
    /// re-resolves the settings, doesn't quietly undo it mid-session.
    /// </summary>
    private void ToggleFileExtensions()
    {
        _extensionsThisSession = !SidebarForTest.ShowFileExtensions;
        SidebarForTest.ShowFileExtensions = _extensionsThisSession.Value;
    }

    /// <summary>
    /// Run <paramref name="save"/> only if the user actually changed something — the mirrors are records, so
    /// "changed" is their own value equality. A settings file nobody edited keeps its modification time, and
    /// an app the user has never opened doesn't acquire a config file just because they visited a dialog.
    /// </summary>
    private static void SaveIfChanged<T>(T edited, T original, Action save)
        where T : notnull
    {
        if (!edited.Equals(original))
        {
            save();
        }
    }

    /// <summary>The config file most recently handed to the OS by a Preferences tab (test seam).</summary>
    internal string? LastConfigFileOpenedForTest { get; private set; }

    /// <summary>
    /// Hand a Preferences tab's config file to the OS's default editor for its type — the escape hatch for
    /// everything a form can't show, which for a settings file is always something.
    /// <para>
    /// The file is written first when it doesn't exist yet: an app whose settings are all still defaults has
    /// never written one, and "open the config file" answered with "there is no such file" is a dead end.
    /// What it gets is that app's saved settings, <em>not</em> the edits in progress in the dialog — those
    /// still belong to the dialog until it's accepted, and a Cancel afterwards has to leave nothing behind.
    /// </para>
    /// </summary>
    private async Task OpenConfigFileAsync(SettingsTab tab, IReadOnlyDictionary<string, Action> createFile)
    {
        if (tab.ConfigPath is not { Length: > 0 } path)
        {
            return;
        }

        try
        {
            if (!File.Exists(path) && createFile.TryGetValue(path, out var create))
            {
                create();
            }

            LastConfigFileOpenedForTest = path;
            await Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception)
        {
            await _overlayHost.ShowModalAsync(new MessageDialog("Couldn't open the settings file", e.Message));
        }
    }
}
