using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Plaintain.Control.Commands;
using Plaintain.Control.Controls;
using Plaintain.Control.Views;
using Plaintain.Desktop.Services;
using Puffinote.Components.Commands;
using Puffinote.Components.Overlays;
using Puffinote.Components.Shell;
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

    // Shown only away from 100%, where it is the answer to "why is this file suddenly huge?"; a permanent
    // "100%" would just be furniture.
    private readonly TextBlock _statusZoom = new() { IsVisible = false };
    private readonly MenuItem _recentMenu = new() { Header = "Open _Recent" };
    private readonly OverlayHost _overlay = new() { Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) };
    private readonly Dictionary<string, AppCommand> _commands;
    // The family's unified title bar: the document's name, centred, in the app's own chrome.
    private readonly DocumentTitleBar _titleBar = new();

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
        EditorView.Zoom = _settings.Settings.Zoom;
        EditorView.SelectionChanged += (_, _) => UpdateChrome();

        // Zoom is remembered for the same reason wrap is: someone who needs bigger text needs it every
        // session, not once.
        EditorView.ZoomChanged += (_, _) =>
        {
            _settings.Settings.Zoom = EditorView.Zoom;
            _settings.Save();
            UpdateChrome();
        };

        // Wrap and the gutter are remembered, because they are settings people hold an opinion about
        // once: whoever wants wrap wants it for every file, and whoever doesn't never wants it again.
        EditorView.Editor.PropertyChanged += (_, change) =>
        {
            if (change.Property == PlainTextEditor.WordWrapProperty
                || change.Property == PlainTextEditor.ShowLineNumbersProperty)
            {
                _settings.Settings.WordWrap = EditorView.Editor.WordWrap;
                _settings.Settings.ShowLineNumbers = EditorView.Editor.ShowLineNumbers;
                _settings.Save();
            }
        };

        _commands = EditorView.EditingCommands.ToDictionary(c => c.Id, StringComparer.Ordinal);
        PlainTextEditingCommands.GoToLineRequested += OnGoToLineRequested;

        var root = new DockPanel { LastChildFill = true };

        // The title bar is the topmost strip, above even the in-window menu, because on macOS it *is* the
        // window's title bar — the traffic lights are drawn over it.
        DockPanel.SetDock(_titleBar, Dock.Top);
        root.Children.Add(_titleBar);
        DocumentTitleBar.Extend(this);

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
        file.Items.Add(new Separator());
        file.Items.Add(Action("_Preferences…", ShowSettings, PreferencesGesture));

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
        view.Items.Add(new Separator());
        view.Items.Add(Item("Zoom _In", ZoomCommands.ZoomInId));
        view.Items.Add(Item("Zoom _Out", ZoomCommands.ZoomOutId));
        view.Items.Add(Item("_Actual Size", ZoomCommands.ActualSizeId));

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

        KeyBindings.Add(new KeyBinding { Gesture = Shortcut.Cmd(Key.N).ToGesture(), Command = new RelayCommand(() => _ = NewDocumentAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = Shortcut.Cmd(Key.O).ToGesture(), Command = new RelayCommand(() => _ = OpenAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = Shortcut.Cmd(Key.S).ToGesture(), Command = new RelayCommand(() => _ = SaveAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = Shortcut.Cmd(Key.S, KeyModifiers.Shift).ToGesture(), Command = new RelayCommand(() => _ = SaveAsAsync()) });
        KeyBindings.Add(new KeyBinding { Gesture = PreferencesGesture, Command = new RelayCommand(ShowSettings) });
    }

    /// <summary>⌘, (Ctrl+, off macOS) — the platform's Preferences chord, and the same one in every app here.</summary>
    private static KeyGesture PreferencesGesture => Shortcut.Cmd(Key.OemComma).ToGesture();

    private Border BuildStatusBar()
    {
        var palette = Tokens.Dark;
        foreach (var block in new[] { _statusPosition, _statusCounts, _statusEncoding, _statusZoom })
        {
            block.VerticalAlignment = VerticalAlignment.Center;
            block.Foreground = palette.MutedText;
            block.FontSize = 11;
        }

        var row = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(_statusPosition, Dock.Left);
        DockPanel.SetDock(_statusZoom, Dock.Left);
        DockPanel.SetDock(_statusEncoding, Dock.Right);
        DockPanel.SetDock(_statusCounts, Dock.Right);
        _statusZoom.Margin = new Thickness(16, 0, 0, 0);
        _statusCounts.Margin = new Thickness(0, 0, 16, 0);
        row.Children.Add(_statusPosition);
        row.Children.Add(_statusZoom);
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
        _titleBar.DocumentName = name;
        _titleBar.IsDirty = EditorView.IsDirty;

        var caret = EditorView.CaretPosition;
        _statusPosition.Text = $"Ln {caret.DisplayLine}, Col {caret.DisplayColumn}";

        var stats = EditorView.Stats;
        _statusCounts.Text = $"{stats.Lines} lines · {stats.Characters} chars";

        // The encoding is not decoration here: it is the one fact that decides whether this file can be
        // written back at all, so it says so rather than leaving the greyed-out Save to be discovered.
        _statusEncoding.Text = EncodingUnconfirmed
            ? $"{_format.DisplayName} (unconfirmed — read-only)"
            : $"{_format.DisplayName} · {stats.NewlineLabel}";

        _statusZoom.Text = ZoomScale.Format(EditorView.Zoom);
        _statusZoom.IsVisible = EditorView.Zoom != ZoomScale.Default;
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
