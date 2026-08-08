using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Cellestra.Desktop.Services;
using Cellestra.Editor;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls;
using Puffinote.Components.Core.Documents;
using Puffinote.Components.Overlays;
using Puffinote.Components.Shell;
using Puffinote.Components.Theming;

namespace Cellestra.Desktop.Views;

/// <summary>
/// The standalone macOS app window. A thin host around an embedded <see cref="SpreadsheetEditorView"/>:
/// it owns the window chrome, the native menu, native file dialogs, the document lock, the recent-files
/// list, settings persistence, and the file-progress overlay. It implements <see cref="IEditorHost"/> so
/// the editor's File commands route here, and drives the loaded document back into the editor.
/// </summary>
public partial class MainWindow : Window, IEditorHost
{
    private readonly SettingsService _settingsService;
    private AppSettings _settings;
    private FileLock? _fileLock;
    private bool _fileBusy;
    private readonly AutoSave _autoSave;

    // The native-menu items gate on the editor's modal/text-input state; held so opening/closing an
    // overlay (which the editor signals via CommandStateChanged) can re-publish their CanExecute.
    private readonly List<RelayCommand> _menuGatedCommands = [];

    public MainWindow()
        : this(null)
    {
    }

    /// <summary>
    /// The window over a given settings store. Passing one points both load and save somewhere else — the
    /// seam the UI tests use to run against a scratch profile, so a headless window neither inherits the
    /// developer's preferences nor writes its own back over them (the other apps' windows take the same).
    /// </summary>
    public MainWindow(SettingsService? settingsService)
    {
        InitializeComponent();

        _settingsService = settingsService ?? new SettingsService();
        _settings = _settingsService.Load();
        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;

        Editor.Host = this;
        ApplyTheme(_settings.Theme); // the saved appearance, before the first paint

        // Appearance is the shell's setting, not the grid's, so the shell is what contributes its row —
        // through the editor's host seam, which Puffinote leaves empty because it settles the theme once
        // for every editor it embeds. Rebuilt per open, so the drop-down shows the theme in force now.
        Editor.HostSettingsSections = () =>
        [
            new SettingsSection("Appearance",
            [
                ChoiceSetting.ForEnum<ThemePreference>("Theme", () => _settings.Theme, ApplyTheme,
                    ThemePreferences.Labels, ThemePreferences.Hint),
            ]),
        ];

        Editor.Zoom = _settings.Zoom;
        Editor.CommandBarVisibility = _settings.CommandBar;
        Editor.RadialConfig = _settings.RadialMenu;
        Editor.AutoSaveSeconds = _settings.AutoSaveSeconds;

        // Document auto-save: write the workbook back once the editing stops. Saving is the host's job
        // here (the editor routes every file operation out through IEditorHost), so the timer lives here
        // even though the interval is edited in the editor's own Preferences dialog.
        _autoSave = new AutoSave(AutoSaveTickAsync) { IntervalSeconds = _settings.AutoSaveSeconds };

        Editor.DocumentChanged += (_, _) =>
        {
            UpdateTitle();

            // Push the countdown back on every edit, so a save lands in the pause after a burst of work
            // rather than in the middle of one — which on a large sheet would mean a full serialize
            // competing with the typing.
            _autoSave.Notify();
        };
        Editor.Saved += (_, _) => FlashSavedInTitle();
        Editor.CommandStateChanged += (_, _) => RefreshCommandStates();
        Editor.CommandBarVisibilityChanged += (_, _) => PersistSettings();
        Editor.RadialConfigChanged += (_, _) => PersistSettings();
        Editor.PreferencesChanged += (_, _) => PersistSettings();

        BuildNativeMenu();
        DocumentTitleBar.Extend(this);
        Closing += OnWindowClosing;
        UpdateTitle();
    }

    /// <summary>
    /// Bring the app to the foreground and give the grid keyboard focus when the window first opens.
    /// Avalonia does not reliably make a freshly-launched macOS window key/active; without this the menu
    /// bar shows the previously-active app's menu until clicked, the first click is consumed activating
    /// the window, and a file dialog can open unfocused. Activating here and focusing the grid fixes all.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Activate();
        Editor.FocusGrid();
    }

    /// <summary>The window title: the document's file name (e.g. <c>Budget.ods</c>, or <c>Untitled</c>),
    /// with a leading ● when there are unsaved changes, and a brief "— Saved" right after a save. No
    /// app-name suffix; transient errors/status never overwrite it — those surface in the editor's status
    /// line instead (#15/#16/#17a).</summary>
    internal static string ComposeTitle(string? filePath, bool modified, bool justSaved = false)
    {
        string name = filePath is null ? "Untitled" : Path.GetFileName(filePath);

        // A ● and a "Saved" say opposite things, so they can never appear together: the dot wins, because
        // by then the save it refers to is out of date.
        string saved = justSaved && !modified ? " — Saved" : string.Empty;
        return $"{(modified ? "● " : string.Empty)}{name}{saved}";
    }

    /// <summary>How long the title says "Saved". Long enough to be read after a ⌘S you didn't look up for,
    /// short enough that it is gone before it could be mistaken for part of the file's name.</summary>
    private static readonly TimeSpan SavedBadgeDuration = TimeSpan.FromSeconds(3);

    private DispatcherTimer? _savedBadge;

    /// <summary>
    /// Flash "— Saved" in the title. ⌘S with no dialog and no visible change is the one command in the app
    /// whose whole point is that nothing happened; the dot going away says it, but only if you happened to
    /// be looking at the dot. The status line's "Saved HH:mm" is the permanent record — this is the notice.
    /// </summary>
    private void FlashSavedInTitle()
    {
        _savedBadge?.Stop();
        _savedBadge = new DispatcherTimer { Interval = SavedBadgeDuration };
        _savedBadge.Tick += (_, _) =>
        {
            _savedBadge?.Stop();
            _savedBadge = null;
            UpdateTitle();
        };
        _savedBadge.Start();
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var session = Editor.Session;
        Title = ComposeTitle(session.FilePath, session.IsModified, _savedBadge is not null);

        // The bar carries the same three facts as the title string, but as separate pieces rather than one
        // composed line — it draws the dot and the badge itself.
        TitleBar.DocumentName = session.FilePath is null ? "Untitled" : Path.GetFileName(session.FilePath);
        TitleBar.IsDirty = session.IsModified;
        TitleBar.Badge = _savedBadge is not null ? "Saved" : null;
    }

    /// <summary>Test seam: end the badge's few seconds now, rather than making a test wait them out.</summary>
    internal void ExpireSavedBadgeForTest()
    {
        _savedBadge?.Stop();
        _savedBadge = null;
        UpdateTitle();
    }

    // ---- Window close / settings -----------------------------------------

    /// <summary>Set once the user has answered the unsaved-changes prompt and chosen to close, so the
    /// programmatic re-close we trigger skips the prompt instead of looping.</summary>
    private bool _forceClose;

    /// <summary>
    /// On close/quit with unsaved changes, cancel the close and ask Save / Don't Save / Cancel (PRD §8.7).
    /// On Save (succeeded) or Don't Save we re-close the window for real; on Cancel we stay open. A clean
    /// (unmodified) document closes immediately.
    /// </summary>
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_forceClose && Editor.Session.IsModified)
        {
            e.Cancel = true; // hold the window open until the user decides
            if (await ConfirmDiscardChangesAsync())
            {
                _forceClose = true;
                Close(); // re-enter Closing, this time allowed straight through
            }

            return; // cancelled, or we'll finish teardown on the forced re-close
        }

        PersistSettings();
        _autoSave.Dispose();
        ReleaseFileLock();
    }

    /// <summary>
    /// Adopt an appearance: remember it, and request the variant it means. Requesting it is the whole of
    /// applying it — the grid, the chart, the sheet tabs and the chrome brushes all resolve themselves from
    /// the app's variant already (they have to: this app has always been able to follow a Mac that switches
    /// itself at dusk), so choosing Light here reaches them the same way the OS doing it would.
    /// <para>
    /// Writing it to the file is the Preferences dialog's job, on the way out: this runs from that dialog's
    /// Commit, and <see cref="PersistSettings"/> follows on <c>PreferencesChanged</c>.
    /// </para>
    /// </summary>
    private void ApplyTheme(ThemePreference theme)
    {
        _settings = _settings with { Theme = theme };

        if (Application.Current is { } app)
        {
            theme.Apply(app);
        }
    }

    private void PersistSettings()
    {
        _settings = _settings with
        {
            CommandBar = Editor.CommandBarVisibility,
            RadialMenu = Editor.RadialConfig,
            Zoom = Editor.Zoom,
            AutoSaveSeconds = Editor.AutoSaveSeconds,
            WindowWidth = Width,
            WindowHeight = Height,
        };
        _settingsService.Save(_settings);

        // A change made in Preferences takes effect on the workbook already open, not the next one.
        _autoSave.IntervalSeconds = _settings.AutoSaveSeconds;
    }

    /// <summary>
    /// Write the workbook back once editing has paused, if it can be written without asking anyone
    /// anything: a workbook that has never been saved has nowhere to go (Save As is a question, and an
    /// auto-save that opens dialogs is a worse feature than no auto-save), and a file operation already in
    /// flight owns the document — <see cref="_fileBusy"/> is taken for the duration so a manual ⌘S can't
    /// race this write, and vice versa.
    /// <para>
    /// No busy overlay, unlike <see cref="SaveWorkbookAsync"/>: nobody asked for this save, so blanking
    /// the window for it would read as the app seizing up. The serialize is on the UI thread for a
    /// consistent snapshot and the bytes go to disk off it — the same shape as the manual save.
    /// </para>
    /// </summary>
    private async Task AutoSaveTickAsync()
    {
        if (Editor.Session.FilePath is not { } filePath || !Editor.Session.IsModified || _fileBusy)
        {
            return;
        }

        _fileBusy = true;
        try
        {
            byte[] image = Editor.Session.SerializeToBytes();
            await Task.Run(() => WorkbookSession.WriteBytesAtomically(filePath, image));
            Editor.Session.MarkSaved(filePath);
            Editor.MarkSavedNow();
            FlashSavedInTitle();

            // Marking it saved raises DocumentChanged — that is how the title drops its dot — and that
            // reaches the countdown, re-arming it against a workbook that is now clean. Drop it, unless
            // an edit really did land while the write was in flight, in which case it is right to run.
            if (!Editor.Session.IsModified)
            {
                _autoSave.Cancel();
            }
        }
        catch (Exception ex)
        {
            // Stand auto-save down and say so once: the disk that refused this write will refuse the next
            // one too, and the same dialog every thirty seconds is a siege, not a report. ⌘S still works,
            // and a manual save that succeeds switches auto-save back on.
            _autoSave.Suspended = true;
            Editor.ShowMessage("Auto-save couldn't write the file",
                $"{ex.Message}\n\nAuto-save is paused until you save successfully yourself (⌘S).");
        }
        finally
        {
            _fileBusy = false;
        }
    }

    /// <summary>Run one auto-save cycle now; for tests (a DispatcherTimer can't tick headlessly).</summary>
    internal Task AutoSaveNowForTest() => _autoSave.RunAsync();

    /// <summary>Whether an auto-save is armed and waiting out the pause (test seam).</summary>
    internal bool AutoSavePendingForTest => _autoSave.IsPending;

    private void RememberRecentFile(string path)
    {
        _settings = _settings.WithRecentFile(path);
        _settingsService.Save(_settings);
        RefreshOpenRecent(); // refresh the Open Recent submenu in place (don't rebuild the whole menu — see #17a)
    }

    // ---- Native menu ------------------------------------------------------

    /// <summary>The "Open Recent" submenu item, kept so its contents refresh in place without a full menu rebuild.</summary>
    private NativeMenuItem? _openRecentItem;

    /// <summary>The native macOS menu bar — the complete command list (PRD §6.2), all from the one catalog.</summary>
    private void BuildNativeMenu()
    {
        var menu = new NativeMenu();

        // macOS always shows a bold application menu titled from Application.Name (set in App.axaml);
        // Avalonia does not merge our items into it, so we don't add our own "Cellestra" submenu here —
        // doing so produced a duplicate menu. "About Cellestra" (help.about) lives in the Help menu below.
        var file = Submenu("File", "file.new", "file.open", "file.save", "file.saveAs", null, "file.importCsv", "file.exportCsv");
        _openRecentItem = BuildOpenRecent();
        file.Menu!.Items.Insert(2, _openRecentItem);
        menu.Items.Add(file);
        var edit = Submenu("Edit", "edit.undo", "edit.redo", null, "edit.cut", "edit.copy", "edit.paste", "edit.pasteSpecial",
            "edit.delete", "edit.clearFormats", "edit.clearAll", "sheet.resetData", "edit.selectAll", null,
            "edit.find", "edit.findReplace", null, "edit.autoSum");
        edit.Menu!.Items.Add(Submenu("Fill", "fill.down", "fill.right")); // Edit ▸ Fill ▸ Down / Right (PRD §6.2)
        menu.Items.Add(edit);
        var format = Submenu("Format", "format.cells", null, "format.bold", "format.italic", "format.underline", null,
            "format.alignLeft", "format.alignCenter", "format.alignRight", "format.wrap", null,
            "format.currency", "format.percent", "format.date", null, "format.merge");
        format.Menu!.Items.Add(Submenu("Borders", "format.borderAll", "format.borderOutline",
            "format.borderTop", "format.borderBottom", "format.borderLeft", "format.borderRight", null, "format.borderNone"));
        menu.Items.Add(format);
        menu.Items.Add(Submenu("Insert", "insert.rowsAbove", "insert.columnsLeft", "edit.deleteRows", "edit.deleteColumns", null,
            "insert.sheet", "data.createTable", "insert.function", "insert.namedRange", "insert.hyperlink", null, "sheet.rename", "sheet.delete"));
        menu.Items.Add(Submenu("Data", "data.sortAsc", "data.sortDesc", "data.customSort", "data.sortAndFilter", null,
            "data.toggleFilter", "data.clearFilter", null, "data.createTable", "data.autofitColumns", "data.removeDuplicates",
            "data.textToColumns", "data.validation", null, "formula.recalc"));
        menu.Items.Add(Submenu("View", "view.commandPalette", "view.goToSheet", null, "view.zoomIn", "view.zoomOut", "view.actualSize", null,
            "view.commandBarVisible", "view.commandBarCollapsed", "view.commandBarHidden", null, "view.freezePanes", null, "view.radialSettings", "app.preferences"));
        menu.Items.Add(Submenu("Help", "help.shortcuts", null, "help.about"));
        NativeMenu.SetMenu(this, menu);
    }

    private NativeMenuItem BuildOpenRecent()
    {
        var item = new NativeMenuItem("Open Recent") { Menu = [] };
        PopulateOpenRecent(item.Menu);
        return item;
    }

    /// <summary>Fill an Open Recent submenu's items from the current recent-files list.</summary>
    private void PopulateOpenRecent(NativeMenu submenu)
    {
        submenu.Items.Clear();
        if (_settings.RecentFiles.Count == 0)
        {
            submenu.Items.Add(new NativeMenuItem("(none)") { IsEnabled = false });
            return;
        }

        foreach (string path in _settings.RecentFiles)
        {
            string captured = path;
            submenu.Items.Add(new NativeMenuItem(Path.GetFileName(path))
            {
                Command = new RelayCommand(() => _ = OpenPathAsync(captured)),
            });
        }
    }

    /// <summary>
    /// Refresh the Open Recent submenu's items <em>in place</em> after opening/saving a file. Rebuilding and
    /// re-<see cref="NativeMenu.SetMenu"/>-ting the whole menu while it's mounted is what threw
    /// "the menu being updated does not match" on macOS (#17a); mutating the existing submenu's items avoids it.
    /// </summary>
    private void RefreshOpenRecent()
    {
        if (_openRecentItem?.Menu is { } submenu)
        {
            PopulateOpenRecent(submenu);
        }
    }

    private NativeMenuItem Submenu(string header, params string?[] commandIds)
    {
        var submenu = new NativeMenu();
        foreach (string? id in commandIds)
        {
            if (id is null)
            {
                submenu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }

            if (Editor.Commands.ById(id) is { } command)
            {
                var menuCommand = new RelayCommand(
                    () => command.Execute(),
                    () => Editor.CanRunShortcut(command.Gesture) && command.IsEnabled);
                _menuGatedCommands.Add(menuCommand);
                submenu.Items.Add(new NativeMenuItem(command.Title)
                {
                    // A native menu item's Gesture becomes a macOS key equivalent the system claims
                    // globally, ahead of the focused control — and a *disabled* item still swallows its
                    // key equivalent rather than passing it on (Avalonia #7133 / discussion #10902). So a
                    // gesture that a focused text field must handle itself (Delete → Clear Contents; ⌘A →
                    // Select All; ⌘C/⌘X/⌘V/⌘Z) gets NO native equivalent — otherwise macOS eats it mid-edit
                    // and the TextBox never selects/copies/deletes its text. Those are driven instead by the
                    // grid's OnKeyDown (selection mode), the editors (edit mode), and the gated KeyBinding as
                    // the cross-platform fallback. Other shortcuts (⌘S, ⌘B…) keep their equivalent for the
                    // menu hint and still back off from a focused text input.
                    Gesture = KeyGestureRules.YieldsToTextInput(command.Gesture) ? null : command.Gesture,
                    Command = menuCommand,
                });
            }
        }

        return new NativeMenuItem(header) { Menu = submenu };
    }

    /// <summary>
    /// Re-publish CanExecute for every gated native-menu item so macOS refreshes its enabled state. The
    /// editor raises CommandStateChanged whenever a modal overlay opens or closes: without this the menu's
    /// cached "enabled" persists and macOS keeps firing a ⌘-shortcut's key equivalent against the grid
    /// behind the modal instead of letting the overlay handle the key.
    /// </summary>
    private void RefreshCommandStates()
    {
        foreach (var command in _menuGatedCommands)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    // ---- Save-changes prompt ---------------------------------------------

    /// <summary>The user's answer to a "you have unsaved changes" prompt (PRD §8.7).</summary>
    internal enum SaveChoice
    {
        Save,
        DontSave,
        Cancel,
    }

    /// <summary>
    /// Decide whether a discard-the-document action (close/quit, New, Open, Import) may proceed, given
    /// whether the workbook is modified and — only when it is — the user's <see cref="SaveChoice"/>.
    /// Pure so the close/open/new gating logic is unit-testable without a window (AGENTS.md: focus/pointer
    /// flows are hard headlessly). Returns true to proceed (discarding), false to stay put.
    /// </summary>
    internal static bool ShouldProceedAfterPrompt(bool isModified, SaveChoice choice) =>
        !isModified || choice != SaveChoice.Cancel;

    /// <summary>
    /// Gate a document-replacing action on the save-changes prompt (PRD §8.7). If nothing is modified it
    /// proceeds silently; otherwise it asks Save / Don't Save / Cancel, saving first on Save. Returns
    /// true when the caller may discard the current document, false when the user cancelled.
    /// </summary>
    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!Editor.Session.IsModified)
        {
            return true;
        }

        var choice = await Editor.ShowModalAsync(new SaveChangesDialog(DocumentDisplayName()));
        if (choice == SaveChoice.Save)
        {
            await SaveWorkbookAsync();
            // If the save was cancelled (no path chosen in Save As) or failed, the document is still
            // modified — don't discard it.
            return !Editor.Session.IsModified;
        }

        return ShouldProceedAfterPrompt(isModified: true, choice);
    }

    /// <summary>The document's display name for prompts (file name, or "Untitled" for a new workbook).</summary>
    private string DocumentDisplayName() =>
        Editor.Session.FilePath is null ? "Untitled" : Path.GetFileName(Editor.Session.FilePath);

    // ---- File operations (IEditorHost) -----------------------------------

    /// <summary>Make our window key/frontmost before presenting a native file dialog (the macOS panel is
    /// a sheet on this window; if it isn't key, the sheet opens unfocused). Companion to OnOpened.</summary>
    private void ActivateForDialog() => Activate();

    public async Task NewWorkbookAsync()
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return; // user cancelled out of the save-changes prompt
        }

        ReleaseFileLock();
        _autoSave.Cancel(); // any countdown was aimed at the workbook this one replaces
        Editor.ReplaceSession(WorkbookSession.NewBlank());
    }

    public async Task OpenWorkbookAsync()
    {
        ActivateForDialog();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Spreadsheet",
            AllowMultiple = false,
            FileTypeFilter = [OdsFileType],
        });

        if (files.Count == 0)
        {
            return;
        }

        await OpenPathAsync(files[0].Path.LocalPath);
    }

    public async Task OpenPathAsync(string path)
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return; // user cancelled out of the save-changes prompt; keep the current document
        }

        if (BeginFileOperation($"Opening {Path.GetFileName(path)}…") is not { } busy)
        {
            return; // another file operation is already running
        }

        using (busy)
        {
            try
            {
                var existingLock = FileLock.Inspect(path);

                // Deserialize + initial recalc are the heavy part — run them off the UI thread so a
                // large open never freezes the window (PRD §8.6 / threading rule), then adopt on the UI
                // thread where the model is touched by rendering.
                var loaded = await Task.Run(() =>
                {
                    using var stream = File.OpenRead(path);
                    return WorkbookSession.LoadAndRecalc(stream);
                });

                Editor.AdoptLoaded(loaded, path);
                _autoSave.Cancel(); // any countdown was aimed at the workbook this one replaces

                ReleaseFileLock();
                _fileLock = DocumentLocks.TryAcquire(path);

                RememberRecentFile(path);

                // The title stays the document name (#15); transient open conditions are narrated in the
                // status line so they don't hijack the title bar. A *stale* lock says nothing about who
                // else has the file — it is our own leftover from a session that was killed, and
                // TryAcquire has just taken it back — so warning about it would be warning about
                // ourselves.
                if (existingLock is { IsStale: false })
                {
                    string who = string.IsNullOrWhiteSpace(existingLock.User) ? "another application" : existingLock.User;
                    string host = string.IsNullOrWhiteSpace(existingLock.Host) ? string.Empty : $" on {existingLock.Host}";
                    Editor.Narrate($"Open in {who}{host}; changes may conflict");
                }
                else if (Editor.Session.LoadRecalcMismatches > 0)
                {
                    Editor.Narrate($"{Editor.Session.LoadRecalcMismatches} cell(s) recomputed differently than the saved file");
                }
            }
            catch (Exception ex)
            {
                // A failed open shouldn't permanently rename the title bar (#17a) — explain it in a dialog
                // and leave the title showing whatever document is still open.
                Editor.ShowMessage("Couldn't open the file", ex.Message);
            }
        }
    }

    public async Task SaveWorkbookAsync()
    {
        if (Editor.Session.FilePath is null)
        {
            await SaveWorkbookAsAsync();
            return;
        }

        if (BeginFileOperation($"Saving {DocumentDisplayName()}…") is not { } busy)
        {
            return; // another file operation is already running
        }

        using (busy)
        {
            string filePath = Editor.Session.FilePath;
            try
            {
                // Serialize on the UI thread (a consistent snapshot, no edit can race it), then write
                // the immutable bytes to disk off-thread so a large save never freezes the window.
                byte[] image = Editor.Session.SerializeToBytes();
                await Task.Run(() => WorkbookSession.WriteBytesAtomically(filePath, image));
                Editor.Session.MarkSaved(filePath);
            }
            catch (Exception ex)
            {
                // A failed save must not crash the app — surface it like OpenPathAsync does for opens.
                Editor.ShowMessage("Couldn't save the file", ex.Message);
                return;
            }

            Editor.MarkSavedNow();

            // On disk, so a countdown aimed at it has nothing left to write — and a manual save that
            // succeeded is the answer to whatever made auto-save stand itself down.
            _autoSave.Cancel();
            _autoSave.Suspended = false;
            RememberRecentFile(filePath);
        }
    }

    public async Task SaveWorkbookAsAsync()
    {
        ActivateForDialog();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Spreadsheet",
            DefaultExtension = "ods",
            SuggestedFileName = "Untitled.ods",
            FileTypeChoices = [OdsFileType],
        });

        if (file is null)
        {
            return;
        }

        string path = file.Path.LocalPath;
        if (BeginFileOperation($"Saving {Path.GetFileName(path)}…") is not { } busy)
        {
            return; // another file operation is already running
        }

        using (busy)
        {
            try
            {
                // Serialize on the UI thread (consistent snapshot), write the bytes off-thread.
                byte[] image = Editor.Session.SerializeToBytes();
                await Task.Run(() => WorkbookSession.WriteBytesAtomically(path, image));
                Editor.Session.MarkSaved(path);
            }
            catch (Exception ex)
            {
                // A failed save must not crash the app — surface it like OpenPathAsync does for opens.
                Editor.ShowMessage("Couldn't save the file", ex.Message);
                return;
            }

            Editor.MarkSavedNow();
            _autoSave.Cancel();
            _autoSave.Suspended = false;

            // We now own this file on disk — take its lock so other editors are warned.
            if (_fileLock?.DocumentPath != Path.GetFullPath(path))
            {
                ReleaseFileLock();
                _fileLock = DocumentLocks.TryAcquire(path);
            }

            RememberRecentFile(path);
        }
    }

    public async Task ImportCsvAsync()
    {
        ActivateForDialog();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import CSV",
            AllowMultiple = false,
            FileTypeFilter = [CsvFileType],
        });

        if (files.Count == 0)
        {
            return;
        }

        if (!await ConfirmDiscardChangesAsync())
        {
            return; // user cancelled out of the save-changes prompt; keep the current document
        }

        string text = await File.ReadAllTextAsync(files[0].Path.LocalPath);
        var workbook = Cellestra.Core.Serialization.Csv.CsvReader.ReadToWorkbook(text);
        ReleaseFileLock();
        _autoSave.Cancel(); // any countdown was aimed at the workbook this one replaces
        Editor.ReplaceSession(new WorkbookSession(workbook));
    }

    public async Task ExportCsvAsync()
    {
        ActivateForDialog();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export CSV",
            DefaultExtension = "csv",
            SuggestedFileName = Editor.Session.ActiveSheet.Name + ".csv",
            FileTypeChoices = [CsvFileType],
        });

        if (file is null)
        {
            return;
        }

        string csv = Cellestra.Core.Serialization.Csv.CsvWriter.Write(Editor.Session.Workbook, Editor.Session.ActiveSheet);
        await File.WriteAllTextAsync(file.Path.LocalPath, csv);
    }

    /// <summary>Remove our document lock, if we hold one, and stop tracking it for the quit sweep.</summary>
    private void ReleaseFileLock()
    {
        DocumentLocks.Release(_fileLock);
        _fileLock = null;
    }

    /// <summary>
    /// Show the blocking busy overlay for a file open/save that runs off the UI thread, and guard
    /// against a second file operation starting while one is in flight (#A). Returns null when one is
    /// already running (the caller should bail); otherwise dispose the result (via <c>using</c>) to
    /// hide the overlay and clear the guard, even on error.
    /// </summary>
    private IDisposable? BeginFileOperation(string text)
    {
        if (_fileBusy)
        {
            return null;
        }

        _fileBusy = true;
        BusyText.Text = text;
        BusyOverlay.IsVisible = true;
        return new FileOperationScope(this);
    }

    private sealed class FileOperationScope(MainWindow owner) : IDisposable
    {
        public void Dispose()
        {
            owner._fileBusy = false;
            owner.BusyOverlay.IsVisible = false;
        }
    }

    private static readonly FilePickerFileType OdsFileType = new("OpenDocument Spreadsheet")
    {
        Patterns = ["*.ods"],
        MimeTypes = ["application/vnd.oasis.opendocument.spreadsheet"],
    };

    private static readonly FilePickerFileType CsvFileType = new("CSV")
    {
        Patterns = ["*.csv"],
        MimeTypes = ["text/csv"],
    };
}

/// <summary>
/// The "you have unsaved changes" prompt (PRD §8.7): Save / Don't Save / Cancel, rendered as an
/// internal modal in the editor's overlay like the other dialogs. Esc and a backdrop click both resolve
/// to <see cref="MainWindow.SaveChoice.Cancel"/> — the safe, non-destructive default.
/// </summary>
internal sealed class SaveChangesDialog : ModalDialog<MainWindow.SaveChoice>
{
    public SaveChangesDialog(string documentName)
    {
        Title = "Unsaved changes";

        var save = new Button { Content = "Save", IsDefault = true };
        save.Click += (_, _) => Close(MainWindow.SaveChoice.Save);
        var dontSave = new Button { Content = "Don’t Save" };
        dontSave.Click += (_, _) => Close(MainWindow.SaveChoice.DontSave);
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(MainWindow.SaveChoice.Cancel);

        DialogContent = new StackPanel
        {
            Width = 380,
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"Do you want to save the changes you made to “{documentName}”? Your changes will be lost if you don’t save them.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { dontSave, cancel, save },
                },
            },
        };
    }

    // Esc / backdrop click must mean "stay" (don't lose data), not the enum's default of Save.
    protected override MainWindow.SaveChoice CancelResult => MainWindow.SaveChoice.Cancel;
}
