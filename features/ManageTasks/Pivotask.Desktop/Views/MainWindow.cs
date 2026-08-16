using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Pivotask.Control.Views;
using Pivotask.Core;
using Pivotask.Core.Serialization;
using Pivotask.Desktop.Services;
using Puffinote.Components.Commands;
using Puffinote.Components.Overlays;
using Puffinote.Components.Shell;
using Puffinote.Components.Theming;

namespace Pivotask.Desktop.Views;

/// <summary>
/// The standalone shell's single window: one window, one board, one process. A thin host around the
/// embeddable <see cref="TaskBoardView"/> — it owns the menu, the file dialogs, the dirty-state guard,
/// and the status bar. Every command that acts on the board comes from the control's catalog, so this
/// window and a Puffinote tab are the same editor.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly FilePickerFileType BoardType = new("Pivotask board")
    {
        // The board's own extension, and the one it used to have — boards written before the rename are
        // still opened, they are simply no longer what "Save" produces. Never "*.json": a board is a
        // Pivotask document, not every JSON file on the machine.
        Patterns = ["*.pivotask", "*.pivotask.json"],
    };

    private readonly TaskBoardView _view;
    private readonly SettingsService _settings;
    private readonly TextBlock _status = new();
    private readonly OverlayHost _overlay = new() { Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) };
    private readonly Dictionary<string, AppCommand> _commands;
    // The family's unified title bar: the document's name, centred, in the app's own chrome.
    private readonly DocumentTitleBar _titleBar = new();
    private bool _closingConfirmed;

    // Document auto-save: write the open board back once the editing stops (AutoSaveTick decides whether
    // it can be written at all).
    private readonly AutoSave _autoSave;

    // External-change detection: the open board, watched, so a pull is noticed rather than silently
    // overwritten by the next save (MainWindow.External.cs).
    private readonly OpenFileWatcher _watcher;

    public MainWindow()
        : this(new SettingsService())
    {
    }

    public MainWindow(SettingsService settings)
    {
        _view = BoardView;
        _settings = settings;
        _watcher = new OpenFileWatcher(HandleExternalChange);
        Title = "Untitled board";
        Width = 1000;
        Height = 680;

        // Built before ApplySettings, which is where the interval is pushed in — from here and from the
        // Preferences dialog alike, so there is one place that knows what a committed setting means.
        _autoSave = new AutoSave(() =>
        {
            AutoSaveTick();
            return Task.CompletedTask;
        });

        ApplySettings(_settings.Settings);

        _commands = _view.EditingCommands.ToDictionary(c => c.Id, StringComparer.Ordinal);

        // Push the countdown back on every board edit, so a save lands in the pause after a burst of work
        // rather than in the middle of one.
        _view.DocumentChanged += (_, _) =>
        {
            UpdateChrome();
            _autoSave.Notify();
        };
        var root = new DockPanel { LastChildFill = true };

        // The title bar is the topmost strip, above even the in-window menu, because on macOS it *is* the
        // window's title bar — the traffic lights are drawn over it.
        DockPanel.SetDock(_titleBar, Dock.Top);
        root.Children.Add(_titleBar);
        DocumentTitleBar.Extend(this);

        InstallMenu(root);

        var status = BuildStatusBar();
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(_view);

        Content = new Panel { Children = { root, _overlay } };
        BindShortcuts();
        UpdateChrome();
        Closing += OnWindowClosing;
    }

    /// <summary>The embedded board — the whole product; this window is chrome around it.</summary>
    public TaskBoardView BoardView { get; } = new();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _autoSave.Dispose();
        _watcher.Dispose();
    }

    /// <summary>Open a board file, replacing whatever is open.</summary>
    public void OpenPath(string path) => OpenPath(path, reloading: false);

    /// <summary>
    /// Open <paramref name="path"/>, replacing whatever is open. <paramref name="reloading"/> says this is
    /// the <em>same</em> board being taken again after the file changed underneath, and the difference is
    /// the history: the disk version lands as one undoable step instead of as a new document, so ⌘Z gives
    /// back the board that was on screen if that turns out to be what was wanted.
    /// </summary>
    private void OpenPath(string path, bool reloading)
    {
        try
        {
            if (reloading && _view.Document is { } open && string.Equals(open.Path, path, StringComparison.Ordinal))
            {
                open.Reload(File.ReadAllText(path));
            }
            else
            {
                _view.Document = BoardDocument.Open(path);
            }

            TrackOnDisk(path); // this read is the new baseline, and this file is the one to watch
            _autoSave.Cancel(); // any countdown was aimed at the board this one just replaced
            UpdateChrome();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BoardFormatException)
        {
            _ = _overlay.ShowModalAsync(new MessageDialog(
                heading: $"Couldn't open “{Path.GetFileName(path)}”.",
                message: ex.Message));
        }
    }

    /// <summary>
    /// Put the menu where the platform keeps menus: the system bar at the top of the screen on macOS, an
    /// in-window bar below the title bar everywhere else. The two are built from the same commands in the
    /// same order — the only thing that differs is which widget carries them. Pivotask was the last of the
    /// five to keep its menus inside the window on a Mac, and a task board has no reason to be the odd one.
    /// </summary>
    private void InstallMenu(DockPanel root)
    {
        if (OperatingSystem.IsMacOS())
        {
            BuildNativeMenu();
            return;
        }

        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);
    }

    /// <summary>
    /// The same menus, in the system bar. Headers lose their <c>_</c> mnemonics: macOS has no Alt-letter
    /// menu access, and the underscore would be drawn literally.
    /// </summary>
    private void BuildNativeMenu()
    {
        NativeMenuItem Item(string header, string id)
        {
            var command = _commands[id];
            var item = new NativeMenuItem(header) { Command = new RelayCommand(() => command.Execute()) };

            // No key-equivalent for a chord that text input needs: macOS routes a menu key-equivalent
            // before the focused view sees the key, so ⌘Z on the menu would be taken away from whatever
            // field is being typed in. They still work — the window's key bindings carry them.
            if (command.Shortcut is { } shortcut && !shortcut.YieldsToTextInput)
            {
                item.Gesture = shortcut.ToGesture(mac: true);
            }

            return item;
        }

        static NativeMenuItem Run(string header, Action action, KeyGesture? gesture = null) =>
            new(header) { Command = new RelayCommand(action), Gesture = gesture };

        static NativeMenuItem Submenu(string header) => new(header) { Menu = [] };

        var file = Submenu("File");
        file.Menu!.Items.Add(Run("New Board", () => _ = NewAsync(), Shortcut.Cmd(Key.N).ToGesture(mac: true)));
        file.Menu.Items.Add(Run("Open…", () => _ = OpenAsync(), Shortcut.Cmd(Key.O).ToGesture(mac: true)));
        file.Menu.Items.Add(new NativeMenuItemSeparator());
        file.Menu.Items.Add(Run("Save", () => _ = SaveAsync(), Shortcut.Cmd(Key.S).ToGesture(mac: true)));
        file.Menu.Items.Add(Run("Save As…", () => _ = SaveAsAsync(), Shortcut.Cmd(Key.S, KeyModifiers.Shift).ToGesture(mac: true)));
        file.Menu.Items.Add(new NativeMenuItemSeparator());
        file.Menu.Items.Add(Run("Preferences…", ShowSettings, PreferencesGesture));

        var edit = Submenu("Edit");
        foreach (var id in new[] { "edit.undo", "edit.redo", null, "task.add", "task.addGroup", "edit.find" })
        {
            edit.Menu!.Items.Add(id is null ? new NativeMenuItemSeparator() : Item(_commands[id].Title, id));
        }

        var view = Submenu("View");
        foreach (var id in new[]
                 {
                     "view.board", "view.list", null,
                     "filter.all", "filter.unfinished", "filter.starred", "filter.dueTomorrow", "filter.dueThisWeek", null,
                     ZoomCommands.ZoomInId, ZoomCommands.ZoomOutId, ZoomCommands.ActualSizeId,
                 })
        {
            view.Menu!.Items.Add(id is null ? new NativeMenuItemSeparator() : Item(_commands[id].Title, id));
        }

        // Help carries About here too, even though the application menu has one: that is where macOS apps
        // put it, and the two items are the same screen.
        var help = Submenu("Help");
        help.Menu!.Items.Add(Run("About Pivotask", ShowAbout));

        var menu = new NativeMenu();
        menu.Items.Add(file);
        menu.Items.Add(edit);
        menu.Items.Add(view);
        menu.Items.Add(help);
        NativeMenu.SetMenu(this, menu);
    }

    /// <summary>
    /// The About screen. Public because the macOS application menu's About item is built on the
    /// <c>Application</c>, before this window exists, and reaches it back through the desktop lifetime.
    /// </summary>
    public void ShowAbout()
    {
        if (!_overlay.IsOpen)
        {
            _ = _overlay.ShowModalAsync(new AboutDialog(
                "Pivotask",
                "A task board whose file is plain, deterministic JSON — small enough to hand-edit and made to "
                + "live in git next to the work it describes.",
                subtitle: "from pivot + task"));
        }
    }

    private Menu BuildMenu()
    {
        MenuItem Item(string header, string id)
        {
            var command = _commands[id];
            return new MenuItem
            {
                Header = header,
                Command = new RelayCommand(() => command.Execute()),
                InputGesture = command.Gesture,
            };
        }

        MenuItem Run(string header, Action action, KeyGesture? gesture = null) =>
            new() { Header = header, Command = new RelayCommand(action), InputGesture = gesture };

        var file = new MenuItem { Header = "_File" };
        file.Items.Add(Run("_New Board", () => _ = NewAsync(), Shortcut.Cmd(Key.N).ToGesture()));
        file.Items.Add(Run("_Open…", () => _ = OpenAsync(), Shortcut.Cmd(Key.O).ToGesture()));
        file.Items.Add(new Separator());
        file.Items.Add(Run("_Save", () => _ = SaveAsync(), Shortcut.Cmd(Key.S).ToGesture()));
        file.Items.Add(Run("Save _As…", () => _ = SaveAsAsync(), Shortcut.Cmd(Key.S, KeyModifiers.Shift).ToGesture()));
        file.Items.Add(new Separator());
        file.Items.Add(Run("_Preferences…", ShowSettings, PreferencesGesture));

        var edit = new MenuItem { Header = "_Edit" };
        edit.Items.Add(Item("_Undo", "edit.undo"));
        edit.Items.Add(Item("_Redo", "edit.redo"));
        edit.Items.Add(new Separator());
        edit.Items.Add(Item("Add _Task", "task.add"));
        edit.Items.Add(Item("Add _Group", "task.addGroup"));
        edit.Items.Add(Item("_Search…", "edit.find"));

        var view = new MenuItem { Header = "_View" };
        view.Items.Add(Item("_Board", "view.board"));
        view.Items.Add(Item("_List", "view.list"));
        view.Items.Add(new Separator());
        view.Items.Add(Item("_All", "filter.all"));
        view.Items.Add(Item("_Unfinished", "filter.unfinished"));
        view.Items.Add(Item("_Starred", "filter.starred"));
        view.Items.Add(Item("Due _Tomorrow", "filter.dueTomorrow"));
        view.Items.Add(Item("Due This _Week", "filter.dueThisWeek"));
        view.Items.Add(new Separator());
        view.Items.Add(Item("Zoom _In", ZoomCommands.ZoomInId));
        view.Items.Add(Item("Zoom _Out", ZoomCommands.ZoomOutId));
        view.Items.Add(Item("Actual Si_ze", ZoomCommands.ActualSizeId));

        // Off macOS there is no application menu to hold About, so Help carries it — the same place
        // Cellestra keeps it, and the place someone looks for it on Windows and Linux.
        var help = new MenuItem { Header = "_Help" };
        help.Items.Add(Run("_About Pivotask", ShowAbout));

        return new Menu { Items = { file, edit, view, help } };
    }

    /// <summary>Bind the catalog's chords. The catalog is the only list of them, so the menu, the key
    /// bindings, and a host palette can't disagree.</summary>
    private void BindShortcuts()
    {
        foreach (var command in _view.EditingCommands)
        {
            // AllShortcuts, so a command's unadvertised aliases bind too — Zoom In shows ⌘= and also
            // answers to the ⇧= that actually carries the "+" glyph, and to the keypad's own key.
            foreach (var shortcut in command.AllShortcuts)
            {
                var captured = command;
                KeyBindings.Add(new KeyBinding
                {
                    Gesture = shortcut.ToGesture(),
                    Command = new RelayCommand(() => captured.Execute()),
                });
            }
        }

        KeyBindings.Add(new KeyBinding { Gesture = Shortcut.Cmd(Key.N).ToGesture(), Command = new RelayCommand(() => _ = NewAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = Shortcut.Cmd(Key.O).ToGesture(), Command = new RelayCommand(() => _ = OpenAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = Shortcut.Cmd(Key.S).ToGesture(), Command = new RelayCommand(() => _ = SaveAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = Shortcut.Cmd(Key.S, KeyModifiers.Shift).ToGesture(), Command = new RelayCommand(() => _ = SaveAsAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = PreferencesGesture, Command = new RelayCommand(ShowSettings) });
    }

    /// <summary>⌘, (Ctrl+, off macOS) — the platform's Preferences chord, and the same one in every app here.</summary>
    private static KeyGesture PreferencesGesture => Shortcut.Cmd(Key.OemComma).ToGesture();

    private Border BuildStatusBar()
    {
        var palette = Tokens.Active;
        _status.Foreground = palette.MutedText;
        _status.FontSize = 11;
        _status.VerticalAlignment = VerticalAlignment.Center;

        return new Border
        {
            Background = palette.Surface,
            BorderBrush = palette.SurfaceBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 4),
            Child = _status,
        };
    }

    private void UpdateChrome()
    {
        var document = _view.Document;
        string name = document.Path is { } path ? Path.GetFileName(path) : "Untitled board";
        Title = $"{(document.IsDirty ? "● " : string.Empty)}{name}";
        _titleBar.DocumentName = name;
        _titleBar.IsDirty = document.IsDirty;

        int tasks = document.Board.Groups.Sum(g => g.Tasks.Count);
        int open = document.Board.Groups.Sum(g => g.Tasks.Count(t => !t.Done));
        string counts = $"{document.Board.Groups.Count} groups · {open} of {tasks} open";

        // An unresolved external write outranks the counts: until it is settled there is a second version
        // of this board on disk, which is a more urgent fact than how many tasks are open.
        _status.Text = DiskChangedUnderEdits ? $"{counts} · changed on disk — Save will ask" : counts;
    }

    // ---- Files ------------------------------------------------------------

    private async Task NewAsync()
    {
        if (await EnsureCanDiscardAsync())
        {
            _view.Document = new BoardDocument();
            TrackOnDisk(null); // a new board has no file to be changed underneath it
            _autoSave.Cancel(); // ditto: the previous board is gone
            UpdateChrome();
        }
    }

    private async Task OpenAsync()
    {
        if (!await EnsureCanDiscardAsync())
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open board",
            AllowMultiple = false,
            FileTypeFilter = [BoardType, FilePickerFileTypes.All],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            await Dispatcher.UIThread.InvokeAsync(() => OpenPath(path));
        }
    }

    private async Task<bool> SaveAsync()
    {
        if (_view.Document.Path is null)
        {
            return await SaveAsAsync();
        }

        // Somebody else's version of this board may have arrived under the edits — most likely a pull.
        // Ask before writing over it; this is the one moment where "whose bytes win?" has to be answered.
        return await ResolveConflictAsync() && Write(_view.Document.Path);
    }

    private async Task<bool> SaveAsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save board",
            SuggestedFileName = Path.GetFileName(_view.Document.Path) ?? "board.pivotask",
            FileTypeChoices = [BoardType],
        });

        return file?.TryGetLocalPath() is { } path && Write(path);
    }

    /// <summary>
    /// Write the open board back once editing has paused, if it can be written without asking anyone
    /// anything. A board that has never been saved has nowhere to go — Save As is a question, and an
    /// auto-save that opens dialogs is a worse feature than no auto-save — and a modal already up owns
    /// the screen.
    /// </summary>
    private void AutoSaveTick()
    {
        // The last of these is the newest: something else wrote this board while it was being edited, and
        // choosing whose version survives is not a decision to make on a timer with nobody watching. The
        // board stays dirty and waits for a ⌘S, where the question gets asked.
        if (_view.Document.Path is not { } path || !_view.Document.IsDirty || _overlay.IsOpen
            || DiskChangedUnderEdits)
        {
            return;
        }

        try
        {
            _view.Document.Save(path);
            UpdateChrome();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Stand auto-save down and say so once: the disk that refused this write will refuse the next
            // one too, and the same modal every thirty seconds is a siege, not a report. ⌘S still works,
            // and a manual save that succeeds switches auto-save back on.
            _autoSave.Suspended = true;
            _ = _overlay.ShowModalAsync(new MessageDialog(
                heading: "Auto-save couldn't write the board.",
                message: $"{ex.Message}\n\nAuto-save is paused until you save successfully yourself (⌘S)."));
        }
    }

    /// <summary>Run one auto-save cycle now; for tests (a DispatcherTimer can't tick headlessly).</summary>
    internal Task AutoSaveNowForTest() => _autoSave.RunAsync();

    /// <summary>Whether an auto-save is armed and waiting out the pause (test seam).</summary>
    internal bool AutoSavePendingForTest => _autoSave.IsPending;

    private bool Write(string path)
    {
        try
        {
            _view.Document.Save(path);
            TrackOnDisk(path); // our own write; the watcher must not report it back to us

            // The board is on disk, so a countdown aimed at it has nothing left to write — and a manual
            // save that succeeded is the answer to whatever made auto-save stand itself down.
            _autoSave.Cancel();
            _autoSave.Suspended = false;
            UpdateChrome();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = _overlay.ShowModalAsync(new MessageDialog(
                heading: "Couldn't save the board.",
                message: $"{ex.Message}\n\nTry File ▸ Save As to write somewhere else."));
            return false;
        }
    }

    private async Task<bool> EnsureCanDiscardAsync()
    {
        if (!_view.Document.IsDirty)
        {
            return true;
        }

        return await _overlay.ShowModalAsync(new ConfirmDialog(
            heading: string.Empty,
            message: "Discard unsaved changes to this board?",
            confirmLabel: "Discard",
            denyLabel: "Cancel",
            confirmIsDefault: false));
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingConfirmed || !_view.Document.IsDirty)
        {
            return;
        }

        e.Cancel = true; // hold the close while the prompt is up; a modal can't answer synchronously
        if (await EnsureCanDiscardAsync())
        {
            _closingConfirmed = true;
            Close();
        }
    }

    internal void SaveForTest(string path) => Write(path);
}
