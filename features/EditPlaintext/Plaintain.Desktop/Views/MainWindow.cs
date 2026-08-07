using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Plaintain.Control.Commands;
using Plaintain.Control.Views;
using Plaintain.Desktop.Services;
using Puffinote.Components.Commands;
using Puffinote.Components.Overlays;
using Puffinote.Components.Theming;
using Puffinote.Text.Document;

namespace Plaintain.Desktop.Views;

/// <summary>
/// The standalone shell's single window: one window, one document, one process. A thin host around the
/// embeddable <see cref="PlainTextEditorView"/> — it owns the menu, the file dialogs, recent files, the
/// dirty-state guard, the status bar, and settings. Every command that acts on the document comes from
/// the control's catalog, so this window and a Puffinote tab are the same editor.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly TextBlock _statusPosition = new();
    private readonly TextBlock _statusCounts = new();
    private readonly TextBlock _statusEncoding = new();
    private readonly MenuItem _recentMenu = new() { Header = "Open _Recent" };
    private readonly OverlayHost _overlay = new() { Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) };
    private readonly Dictionary<string, AppCommand> _commands;

    /// <summary>The file being edited, or null for an untitled buffer.</summary>
    private string? CurrentPath { get; set; }
    private TextFileFormat _format = TextFileFormat.Utf8NoBom;

    public MainWindow()
        : this(new SettingsService())
    {
    }

    public MainWindow(SettingsService settings)
    {
        _settings = settings;
        Title = "Untitled";
        Width = 900;
        Height = 640;

        EditorView = new PlainTextEditorView();
        EditorView.Editor.WordWrap = _settings.Settings.WordWrap;
        EditorView.Editor.ShowLineNumbers = _settings.Settings.ShowLineNumbers;
        EditorView.Editor.FontSize = _settings.Settings.FontSize;
        EditorView.SelectionChanged += (_, _) => UpdateChrome();

        _commands = EditorView.EditingCommands.ToDictionary(c => c.Id, StringComparer.Ordinal);
        PlainTextEditingCommands.GoToLineRequested += OnGoToLineRequested;

        var root = new DockPanel { LastChildFill = true };
        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);

        var status = BuildStatusBar();
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(EditorView);

        Content = new Panel { Children = { root, _overlay } };
        BindShortcuts();
        RefreshRecentMenu();
        UpdateChrome();
        Closing += OnWindowClosing;
    }

    /// <summary>The embedded editor — the whole product; this window is chrome around it.</summary>
    public PlainTextEditorView EditorView { get; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Activate();
        EditorView.FocusEditor();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        PlainTextEditingCommands.GoToLineRequested -= OnGoToLineRequested;
    }

    private Menu BuildMenu()
    {
        MenuItem Item(string header, string commandId)
        {
            var command = _commands[commandId];
            return new MenuItem
            {
                Header = header,
                Command = new RelayCommand(() => command.Execute()),
                InputGesture = command.Gesture,
            };
        }

        MenuItem Action(string header, Action run, KeyGesture? gesture = null) => new()
        {
            Header = header,
            Command = new RelayCommand(run),
            InputGesture = gesture,
        };

        var file = new MenuItem { Header = "_File" };
        file.Items.Add(Action("_New", () => _ = NewDocumentAsync(), Shortcut.Cmd(Key.N).ToGesture()));
        file.Items.Add(Action("_Open…", () => _ = OpenAsync(), Shortcut.Cmd(Key.O).ToGesture()));
        file.Items.Add(_recentMenu);
        file.Items.Add(new Separator());
        file.Items.Add(Action("_Save", () => _ = SaveAsync(), Shortcut.Cmd(Key.S).ToGesture()));
        file.Items.Add(Action("Save _As…", () => _ = SaveAsAsync(), Shortcut.Cmd(Key.S, KeyModifiers.Shift).ToGesture()));

        var edit = new MenuItem { Header = "_Edit" };
        edit.Items.Add(Item("_Undo", "edit.undo"));
        edit.Items.Add(Item("_Redo", "edit.redo"));
        edit.Items.Add(new Separator());
        edit.Items.Add(Item("Cu_t", "edit.cut"));
        edit.Items.Add(Item("_Copy", "edit.copy"));
        edit.Items.Add(Item("_Paste", "edit.paste"));
        edit.Items.Add(Item("Select _All", "edit.selectAll"));
        edit.Items.Add(new Separator());
        edit.Items.Add(Item("_Find…", "edit.find"));
        edit.Items.Add(Item("_Go to Line…", "view.goToLine"));

        var view = new MenuItem { Header = "_View" };
        view.Items.Add(Item("_Word Wrap", "view.wordWrap"));
        view.Items.Add(Item("_Line Numbers", "view.lineNumbers"));

        return new Menu { Items = { file, edit, view } };
    }

    /// <summary>
    /// Bind the editor catalog's shortcuts on the window. The catalog is the only list of chords, so a
    /// command's shortcut is right in the menu, in the key binding, and in a host palette at once.
    /// </summary>
    private void BindShortcuts()
    {
        foreach (var command in EditorView.EditingCommands)
        {
            if (command.Gesture is { } gesture)
            {
                KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = new RelayCommand(() => command.Execute()) });
            }
        }

        KeyBindings.Add(new KeyBinding { Gesture = Shortcut.Cmd(Key.N).ToGesture(), Command = new RelayCommand(() => _ = NewDocumentAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = Shortcut.Cmd(Key.O).ToGesture(), Command = new RelayCommand(() => _ = OpenAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = Shortcut.Cmd(Key.S).ToGesture(), Command = new RelayCommand(() => _ = SaveAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = Shortcut.Cmd(Key.S, KeyModifiers.Shift).ToGesture(), Command = new RelayCommand(() => _ = SaveAsAsync()) });
    }

    private Border BuildStatusBar()
    {
        var palette = Tokens.Dark;
        foreach (var block in new[] { _statusPosition, _statusCounts, _statusEncoding })
        {
            block.VerticalAlignment = VerticalAlignment.Center;
            block.Foreground = palette.MutedText;
            block.FontSize = 11;
        }

        var row = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(_statusPosition, Dock.Left);
        DockPanel.SetDock(_statusEncoding, Dock.Right);
        DockPanel.SetDock(_statusCounts, Dock.Right);
        _statusCounts.Margin = new Thickness(0, 0, 16, 0);
        row.Children.Add(_statusPosition);
        row.Children.Add(_statusEncoding);
        row.Children.Add(_statusCounts);

        return new Border
        {
            Background = palette.Surface,
            BorderBrush = palette.SurfaceBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 4),
            Child = row,
        };
    }

    /// <summary>Refresh the title and status bar from the editor's current state.</summary>
    private void UpdateChrome()
    {
        string name = CurrentPath is null ? "Untitled" : Path.GetFileName(CurrentPath);
        Title = $"{(EditorView.IsDirty ? "● " : string.Empty)}{name}";

        var caret = EditorView.CaretPosition;
        _statusPosition.Text = $"Ln {caret.DisplayLine}, Col {caret.DisplayColumn}";

        var stats = EditorView.Stats;
        _statusCounts.Text = $"{stats.Lines} lines · {stats.Characters} chars";

        // The encoding is not decoration here: it is the one fact that decides whether this file can be
        // written back at all, so it says so rather than leaving the greyed-out Save to be discovered.
        _statusEncoding.Text = EncodingUnconfirmed
            ? $"{_format.DisplayName} (unconfirmed — read-only)"
            : $"{_format.DisplayName} · {stats.NewlineLabel}";
    }

    private async void OnGoToLineRequested(PlainTextEditorView view)
    {
        if (!ReferenceEquals(view, EditorView))
        {
            return; // another window's editor asked; not ours to answer
        }

        var input = new TextBox { PlaceholderText = "Line number", Width = 200 };
        var dialog = new FormDialog("Go to Line", input, okLabel: "Go");
        if (await _overlay.ShowModalAsync(dialog)
            && int.TryParse(input.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.CurrentCulture, out int line))
        {
            EditorView.GoToLine(line);
            EditorView.FocusEditor();
        }
    }
}
