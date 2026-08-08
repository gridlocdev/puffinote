using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Pivotask.Control.Commands;
using Pivotask.Control.Views;
using Pivotask.Core;
using Pivotask.Core.Serialization;
using Puffinote.Components.Commands;
using Puffinote.Components.Overlays;
using Puffinote.Components.Theming;

namespace Pivotask.Desktop.Views;

/// <summary>
/// The standalone shell's single window: one window, one board, one process. A thin host around the
/// embeddable <see cref="TaskBoardView"/> — it owns the menu, the file dialogs, the dirty-state guard,
/// and the status bar. Every command that acts on the board comes from the control's catalog, so this
/// window and a Puffinote tab are the same editor.
/// </summary>
public sealed class MainWindow : Window
{
    private static readonly FilePickerFileType BoardType = new("Pivotask board")
    {
        // The compound extension. Note the pattern is "*.pivotask.json", not "*.json" — a board is a
        // suffix, not an extension, and everything in the family that matches one has to know that.
        Patterns = ["*.pivotask.json"],
    };

    private readonly TaskBoardView _view;
    private readonly TextBlock _status = new();
    private readonly OverlayHost _overlay = new() { Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) };
    private readonly Dictionary<string, AppCommand> _commands;
    private bool _closingConfirmed;

    public MainWindow()
    {
        _view = BoardView;
        Title = "Untitled board";
        Width = 1000;
        Height = 680;

        _commands = _view.EditingCommands.ToDictionary(c => c.Id, StringComparer.Ordinal);
        _view.DocumentChanged += (_, _) => UpdateChrome();
        TaskEditingCommands.SearchRequested += OnSearchRequested;

        var root = new DockPanel { LastChildFill = true };
        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);

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
        TaskEditingCommands.SearchRequested -= OnSearchRequested;
    }

    /// <summary>Open a board file, replacing whatever is open.</summary>
    public void OpenPath(string path)
    {
        try
        {
            _view.Document = BoardDocument.Open(path);
            UpdateChrome();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BoardFormatException)
        {
            _ = _overlay.ShowModalAsync(new MessageDialog(
                heading: $"Couldn't open “{Path.GetFileName(path)}”.",
                message: ex.Message));
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
        view.Items.Add(Item("_Starred", "filter.starred"));
        view.Items.Add(Item("_Due Soon", "filter.dueSoon"));
        view.Items.Add(Item("_Unfinished", "filter.unfinished"));
        view.Items.Add(new Separator());
        view.Items.Add(Item("Zoom _In", ZoomCommands.ZoomInId));
        view.Items.Add(Item("Zoom _Out", ZoomCommands.ZoomOutId));
        view.Items.Add(Item("Actual Si_ze", ZoomCommands.ActualSizeId));

        return new Menu { Items = { file, edit, view } };
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
    }

    private Border BuildStatusBar()
    {
        var palette = Tokens.Dark;
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

        int tasks = document.Board.Groups.Sum(g => g.Tasks.Count);
        int open = document.Board.Groups.Sum(g => g.Tasks.Count(t => !t.Done));
        _status.Text = $"{document.Board.Groups.Count} groups · {open} of {tasks} open";
    }

    private void OnSearchRequested(TaskBoardView view)
    {
        if (ReferenceEquals(view, _view))
        {
            _view.Query = string.Empty;
            _view.Focus();
        }
    }

    // ---- Files ------------------------------------------------------------

    private async Task NewAsync()
    {
        if (await EnsureCanDiscardAsync())
        {
            _view.Document = new BoardDocument();
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

        return Write(_view.Document.Path);
    }

    private async Task<bool> SaveAsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save board",
            SuggestedFileName = Path.GetFileName(_view.Document.Path) ?? "board.pivotask.json",
            FileTypeChoices = [BoardType],
        });

        return file?.TryGetLocalPath() is { } path && Write(path);
    }

    private bool Write(string path)
    {
        try
        {
            _view.Document.Save(path);
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
