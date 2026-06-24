using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Typage.Control.Views;
using Typage.Desktop.Commands;
using Typage.Desktop.Services;
using Typage.Engine.Document;
using Typage.Engine.Html;

namespace Typage.Desktop.Views;

/// <summary>
/// The standalone shell's single window (PRD §5 — one window, one document, one process). A thin
/// host around the embeddable <see cref="MarkdownEditorView"/>: it owns the menu, file dialogs,
/// recent files, the dirty-state guard, the status bar, and HTML export. All editing logic lives
/// in the control and engine; this layer is replaceable by the future notes-app shell.
/// </summary>
public class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly MarkdownEditorView _editorView = new();
    private readonly TextBlock _statusPosition = new();
    private readonly TextBlock _statusCounts = new();
    private readonly TextBlock _statusState = new();
    private string? _currentPath;
    private TextFileFormat _format = TextFileFormat.Utf8NoBom;
    private readonly MenuItem _recentMenu = new() { Header = "Open _Recent" };
    private readonly CommandPalette _palette = new();
    private FindBar? _findBar;
    private Border? _toolbar;
    private MenuItem? _toggleToolbarItem;

    private static readonly IBrush ChromeBackground = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
    private static readonly IBrush ChromeBorder = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x36));
    private static readonly IBrush MutedText = new SolidColorBrush(Color.FromRgb(0x9A, 0x9E, 0xA6));

    private static readonly FilePickerFileType MarkdownType = new("Markdown")
    {
        Patterns = new[] { "*.md", "*.markdown", "*.mdown", "*.txt" },
    };

    public MainWindow(SettingsService settings)
    {
        _settings = settings;
        Width = 900;
        Height = 680;
        Title = "Typage";

        var root = new DockPanel();
        root.Children.Add(BuildMenu());
        _toolbar = BuildToolbar();
        _toolbar.IsVisible = _settings.Settings.ShowToolbar; // restore the persisted toolbar visibility
        root.Children.Add(_toolbar);
        _findBar = new FindBar(_editorView);
        DockPanel.SetDock(_findBar, Dock.Top);
        root.Children.Add(_findBar);
        root.Children.Add(BuildStatusBar());
        root.Children.Add(_editorView);

        // The command palette floats over everything as a collapsed overlay.
        Content = new Panel { Children = { root, _palette } };

        // ⌘⇧P / Ctrl+Shift+P opens the palette (bind both so it works on macOS and elsewhere).
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.P, KeyModifiers.Meta | KeyModifiers.Shift),
            Command = new RelayCommand(OpenPalette),
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.P, KeyModifiers.Control | KeyModifiers.Shift),
            Command = new RelayCommand(OpenPalette),
        });

        // Find (⌘F / Ctrl+F) and Find & Replace (⌘⌥F / Ctrl+H).
        foreach (var g in new[] { new KeyGesture(Key.F, KeyModifiers.Meta), new KeyGesture(Key.F, KeyModifiers.Control) })
        {
            KeyBindings.Add(new KeyBinding { Gesture = g, Command = new RelayCommand(() => _findBar?.Show(replace: false)) });
        }

        foreach (var g in new[] { new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Alt), new KeyGesture(Key.H, KeyModifiers.Control) })
        {
            KeyBindings.Add(new KeyBinding { Gesture = g, Command = new RelayCommand(() => _findBar?.Show(replace: true)) });
        }

        _editorView.TextChanged += (_, _) => UpdateChrome();
        _editorView.SelectionChanged += (_, _) => UpdateCaretStatus();
        Closing += OnClosing;

        RefreshRecentMenu();
        UpdateChrome();
    }

    /// <summary>The embedded editor view, exposed for the headless UI tests.</summary>
    internal MarkdownEditorView EditorView => _editorView;

    // ----- chrome -----

    private Menu BuildMenu()
    {
        var file = new MenuItem { Header = "_File" };
        file.Items.Add(Item("_New", () => _ = NewDocumentAsync(), "Ctrl+N"));
        file.Items.Add(Item("_Open…", () => _ = OpenAsync(), "Ctrl+O"));
        file.Items.Add(_recentMenu);
        file.Items.Add(Item("_Save", () => _ = SaveAsync(), "Ctrl+S"));
        file.Items.Add(Item("Save _As…", () => _ = SaveAsAsync(), "Ctrl+Shift+S"));
        file.Items.Add(Item("_Revert to Saved", () => _ = RevertAsync()));
        file.Items.Add(new Separator());
        file.Items.Add(Item("_Export HTML…", () => _ = ExportHtmlAsync()));
        file.Items.Add(new Separator());
        file.Items.Add(Item("E_xit", Close));

        var edit = new MenuItem { Header = "_Edit" };
        edit.Items.Add(Item("_Undo", () => _editorView.Undo(), "Ctrl+Z"));
        edit.Items.Add(Item("_Redo", () => _editorView.Redo(), "Ctrl+Y"));
        edit.Items.Add(new Separator());
        // The editor owns these shortcuts; show the gesture for discoverability without a second binding.
        edit.Items.Add(EditorItem("Cu_t", "Ctrl+X", () => _ = _editorView.Editor.CutAsync()));
        edit.Items.Add(EditorItem("_Copy", "Ctrl+C", () => _ = _editorView.Editor.CopyAsync()));
        edit.Items.Add(EditorItem("_Paste", "Ctrl+V", () => _ = _editorView.Editor.PasteAsync()));
        edit.Items.Add(EditorItem("Select _All", "Ctrl+A", () => _editorView.Editor.SelectAll()));
        edit.Items.Add(new Separator());
        edit.Items.Add(EditorItem("_Find…", "Ctrl+F", () => _findBar?.Show(replace: false)));
        edit.Items.Add(EditorItem("Find & _Replace…", "Ctrl+H", () => _findBar?.Show(replace: true)));

        var format = new MenuItem { Header = "F_ormat" };
        format.Items.Add(Item("_Bold", () => _editorView.Editor.ToggleBold(), "Ctrl+B"));
        format.Items.Add(Item("_Italic", () => _editorView.Editor.ToggleItalic(), "Ctrl+I"));
        format.Items.Add(Item("Inline _Code", () => _editorView.Editor.ToggleInlineCode(), "Ctrl+E"));
        format.Items.Add(Item("_Strikethrough", () => _editorView.Editor.ToggleStrikethrough()));
        format.Items.Add(new Separator());
        for (var level = 1; level <= 3; level++)
        {
            var l = level;
            format.Items.Add(Item($"Heading {l}", () => _editorView.Editor.ApplyHeading(l), $"Ctrl+{l}"));
        }

        var view = new MenuItem { Header = "_View" };
        view.Items.Add(Item("Command _Palette…", OpenPalette, "Ctrl+Shift+P"));
        view.Items.Add(new Separator());
        _toggleToolbarItem = new MenuItem
        {
            Header = "Show _Toolbar",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _settings.Settings.ShowToolbar,
        };
        _toggleToolbarItem.Click += (_, _) => ToggleToolbar();
        view.Items.Add(_toggleToolbarItem);

        var help = new MenuItem { Header = "_Help" };
        help.Items.Add(Item("Markdown _Reference", ShowHelp));

        var menu = new Menu();
        menu.Items.Add(file);
        menu.Items.Add(edit);
        menu.Items.Add(format);
        menu.Items.Add(view);
        menu.Items.Add(help);
        DockPanel.SetDock(menu, Dock.Top);
        return menu;
    }

    private MenuItem Item(string header, Action action, string? gesture = null)
    {
        var command = new RelayCommand(action);
        var item = new MenuItem { Header = header, Command = command };
        if (gesture is not null)
        {
            var kg = KeyGesture.Parse(gesture);
            item.InputGesture = kg;
            KeyBindings.Add(new KeyBinding { Gesture = kg, Command = command });
        }

        return item;
    }

    /// <summary>A menu item whose shortcut is shown for discoverability but handled by the focused editor
    /// (so it is not registered as a second window-level key binding).</summary>
    private static MenuItem EditorItem(string header, string gesture, Action action)
    {
        var item = new MenuItem { Header = header, InputGesture = KeyGesture.Parse(gesture) };
        item.Click += (_, _) => action();
        return item;
    }

    private Border BuildToolbar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Avalonia.Thickness(8, 5) };
        panel.Children.Add(ToolButton("B", "Bold", () => _editorView.Editor.ToggleBold(), bold: true));
        panel.Children.Add(ToolButton("I", "Italic", () => _editorView.Editor.ToggleItalic(), italic: true));
        panel.Children.Add(ToolButton("</>", "Inline code", () => _editorView.Editor.ToggleInlineCode()));
        panel.Children.Add(ToolButton("H1", "Heading 1", () => _editorView.Editor.ApplyHeading(1)));
        panel.Children.Add(ToolButton("H2", "Heading 2", () => _editorView.Editor.ApplyHeading(2)));

        // A "Find a tool…" entry on the right opens the Command Palette (mirrors Cellestra).
        var findTool = new Button
        {
            Content = "Find a tool…    ⌘⇧P",
            Margin = new Avalonia.Thickness(8, 4),
            Padding = new Avalonia.Thickness(10, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Width = 200,
            FontSize = 12,
            Foreground = MutedText,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            BorderBrush = ChromeBorder,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            Command = new RelayCommand(OpenPalette),
        };
        ToolTip.SetTip(findTool, "Run any command by name (⌘⇧P)");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(panel, 0);
        Grid.SetColumn(findTool, 1);
        grid.Children.Add(panel);
        grid.Children.Add(findTool);

        var border = new Border
        {
            Child = grid,
            Background = ChromeBackground,
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            BorderBrush = ChromeBorder,
        };
        DockPanel.SetDock(border, Dock.Top);
        return border;
    }

    private Button ToolButton(string label, string tooltip, Action action, bool bold = false, bool italic = false)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Avalonia.Thickness(9, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(5),
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
            Command = new RelayCommand(action),
        };
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private Border BuildStatusBar()
    {
        foreach (var block in new[] { _statusState, _statusPosition, _statusCounts })
        {
            block.Foreground = MutedText;
            block.FontSize = 12;
            block.VerticalAlignment = VerticalAlignment.Center;
        }

        // State on the left; counts/encoding pushed to the right (like Cellestra's status bar).
        _statusCounts.HorizontalAlignment = HorizontalAlignment.Right;
        var grid = new Grid
        {
            Margin = new Avalonia.Thickness(12, 4),
            ColumnDefinitions = new ColumnDefinitions("Auto,16,Auto,*,Auto"),
        };
        Grid.SetColumn(_statusState, 0);
        Grid.SetColumn(_statusPosition, 2);
        Grid.SetColumn(_statusCounts, 4);
        grid.Children.Add(_statusState);
        grid.Children.Add(_statusPosition);
        grid.Children.Add(_statusCounts);

        var border = new Border
        {
            Child = grid,
            BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
            BorderBrush = ChromeBorder,
            Background = ChromeBackground,
        };
        DockPanel.SetDock(border, Dock.Bottom);
        return border;
    }

    // ----- command catalog & palette -----

    private void OpenPalette() => _palette.Open(BuildCommands());

    private void ToggleToolbar()
    {
        var show = !(_toolbar?.IsVisible ?? true);
        if (_toolbar is not null)
        {
            _toolbar.IsVisible = show;
        }

        if (_toggleToolbarItem is not null)
        {
            _toggleToolbarItem.IsChecked = show;
        }

        _settings.Settings.ShowToolbar = show;
        _settings.Save();
    }

    /// <summary>
    /// The one command catalog (mirroring Cellestra's golden rule #4): the palette reads it, and the
    /// menu/toolbar invoke the same underlying actions. Built per open so CanExecute reflects the
    /// current state.
    /// </summary>
    private List<AppCommand> BuildCommands()
    {
        var ed = _editorView.Editor;
        return new List<AppCommand>
        {
            new("file.new", "New", "Start a new, empty document.", "⌘N", () => _ = NewDocumentAsync()),
            new("file.open", "Open…", "Open a Markdown file from disk.", "⌘O", () => _ = OpenAsync()),
            new("file.save", "Save", "Save changes to the current file.", "⌘S", () => _ = SaveAsync()),
            new("file.saveAs", "Save As…", "Save a copy under a new name.", "⇧⌘S", () => _ = SaveAsAsync()),
            new("file.revert", "Revert to Saved", "Discard changes since the last save.", null,
                () => _ = RevertAsync(), () => _currentPath is not null && _editorView.IsDirty),
            new("file.exportHtml", "Export HTML…", "Render the document to a self-contained HTML file.", null, () => _ = ExportHtmlAsync()),

            new("edit.undo", "Undo", "Step backward through your changes.", "⌘Z", () => _editorView.Undo(), () => _editorView.CanUndo),
            new("edit.redo", "Redo", "Re-apply a change you undid.", "⇧⌘Z", () => _editorView.Redo(), () => _editorView.CanRedo),
            new("edit.cut", "Cut", "Move the selection to the clipboard.", "⌘X", () => _ = ed.CutAsync()),
            new("edit.copy", "Copy", "Copy the selection to the clipboard.", "⌘C", () => _ = ed.CopyAsync()),
            new("edit.paste", "Paste", "Paste from the clipboard.", "⌘V", () => _ = ed.PasteAsync()),
            new("edit.selectAll", "Select All", "Select the whole document.", "⌘A", ed.SelectAll),
            new("edit.find", "Find…", "Find text in the document.", "⌘F", () => _findBar?.Show(replace: false)),
            new("edit.replace", "Find & Replace…", "Find text and replace it (plain or regex).", "⌘⌥F", () => _findBar?.Show(replace: true)),

            new("format.bold", "Bold", "Make the selection bold.", "⌘B", ed.ToggleBold),
            new("format.italic", "Italic", "Make the selection italic.", "⌘I", ed.ToggleItalic),
            new("format.code", "Inline Code", "Format the selection as inline code.", "⌘E", ed.ToggleInlineCode),
            new("format.strike", "Strikethrough", "Strike through the selection.", null, ed.ToggleStrikethrough),
            new("format.link", "Insert Link", "Wrap the selection as a link.", "⌘K", ed.InsertLink),
            new("format.h1", "Heading 1", "Make this line a level-1 heading.", "⌘1", () => ed.ApplyHeading(1)),
            new("format.h2", "Heading 2", "Make this line a level-2 heading.", "⌘2", () => ed.ApplyHeading(2)),
            new("format.h3", "Heading 3", "Make this line a level-3 heading.", "⌘3", () => ed.ApplyHeading(3)),
            new("format.paragraph", "Paragraph", "Remove the heading from this line.", "⌘0", ed.SetParagraph),

            new("view.toggleToolbar", "Toggle Toolbar", "Show or hide the formatting toolbar.", null, ToggleToolbar),
            new("help.reference", "Markdown Reference", "Open the offline syntax & shortcut reference.", null, ShowHelp),
        };
    }

    // ----- file operations -----

    public void OpenPath(string path)
    {
        try
        {
            var (text, format) = TextFileCodec.Decode(File.ReadAllBytes(path));
            _format = format; // remember the encoding/BOM so a save round-trips it
            _editorView.LoadText(text);
            _currentPath = path;
            _settings.Settings.PushRecent(path);
            _settings.Save();
            RefreshRecentMenu();
            UpdateChrome();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Couldn't open “{Path.GetFileName(path)}”.",
                $"{ex.Message}\n\nCheck that the file still exists and that you have permission to read it.");
        }
    }

    /// <summary>Rebuild the File ▸ Open Recent submenu from the persisted MRU list (heuristic 7).</summary>
    private void RefreshRecentMenu()
    {
        _recentMenu.Items.Clear();
        var recent = _settings.Settings.RecentFiles;
        if (recent.Count == 0)
        {
            _recentMenu.Items.Add(new MenuItem { Header = "(no recent files)", IsEnabled = false });
            return;
        }

        foreach (var entry in recent)
        {
            var path = entry;
            _recentMenu.Items.Add(new MenuItem { Header = path, Command = new RelayCommand(() => _ = OpenRecentAsync(path)) });
        }

        _recentMenu.Items.Add(new Separator());
        _recentMenu.Items.Add(new MenuItem { Header = "_Clear Recent", Command = new RelayCommand(ClearRecent) });
    }

    private async Task OpenRecentAsync(string path)
    {
        if (await EnsureCanDiscardAsync())
        {
            OpenPath(path);
        }
    }

    private void ClearRecent()
    {
        _settings.Settings.RecentFiles.Clear();
        _settings.Save();
        RefreshRecentMenu();
    }

    private async Task NewDocumentAsync()
    {
        if (!await EnsureCanDiscardAsync())
        {
            return;
        }

        _editorView.LoadText(string.Empty);
        _currentPath = null;
        _format = TextFileFormat.Utf8NoBom; // new documents default to UTF-8 (no BOM)
        UpdateChrome();
    }

    private async Task OpenAsync()
    {
        if (!await EnsureCanDiscardAsync())
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Markdown",
            AllowMultiple = false,
            FileTypeFilter = new[] { MarkdownType, FilePickerFileTypes.All },
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            OpenPath(path);
        }
    }

    private async Task<bool> SaveAsync()
    {
        if (_currentPath is null)
        {
            return await SaveAsAsync();
        }

        return WriteToPath(_currentPath);
    }

    private async Task<bool> SaveAsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Markdown",
            DefaultExtension = "md",
            SuggestedFileName = Path.GetFileName(_currentPath) ?? "Untitled.md",
            FileTypeChoices = new[] { MarkdownType },
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            if (WriteToPath(path))
            {
                _currentPath = path;
                _settings.Settings.PushRecent(path);
                _settings.Save();
                RefreshRecentMenu();
                UpdateChrome();
                return true;
            }
        }

        return false;
    }

    private bool WriteToPath(string path)
    {
        try
        {
            AtomicWrite(path, TextFileCodec.Encode(_editorView.Text, _format));
            _editorView.MarkSaved();
            UpdateChrome();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Couldn't save the document.",
                $"{ex.Message}\n\nTry File ▸ Save As to write to a different location.");
            return false;
        }
    }

    private async Task RevertAsync()
    {
        if (_currentPath is null || !_editorView.IsDirty)
        {
            return;
        }

        if (await ConfirmAsync("Revert to the last saved version? Unsaved changes will be lost."))
        {
            OpenPath(_currentPath);
        }
    }

    private async Task ExportHtmlAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export HTML",
            DefaultExtension = "html",
            SuggestedFileName = (Path.GetFileNameWithoutExtension(_currentPath) ?? "Untitled") + ".html",
            FileTypeChoices = new[] { new FilePickerFileType("HTML") { Patterns = new[] { "*.html", "*.htm" } } },
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            var title = Path.GetFileNameWithoutExtension(path);
            var html = HtmlRenderer.RenderDocument(Engine.Document.TextDocument.FromString(_editorView.Text), title);
            try
            {
                AtomicWrite(path, new UTF8Encoding(false).GetBytes(html)); // HTML export is always UTF-8
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowError("Couldn't export HTML.",
                    $"{ex.Message}\n\nCheck that the destination folder is writable.");
            }
        }
    }

    private static void AtomicWrite(string path, byte[] content)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? ".";
        var temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, content);
            File.Move(temp, full, overwrite: true); // atomic on the same volume (PRD §7.1)
        }
        catch
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            throw;
        }
    }

    // ----- unsaved-changes guard (heuristics 3, 5) -----

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_editorView.IsDirty && !_closingConfirmed)
        {
            e.Cancel = true;
            if (await EnsureCanDiscardAsync())
            {
                _closingConfirmed = true;
                Close();
            }
        }
    }

    private bool _closingConfirmed;

    private enum UnsavedChoice
    {
        Save,
        Discard,
        Cancel,
    }

    /// <summary>
    /// Guard an action that abandons the current document (new/open/close). Returns true only if it
    /// is safe to proceed: the document is clean, the user chose Don't Save, or the user chose Save
    /// and the save succeeded. Offers Save / Don't Save / Cancel (PRD §7.1; heuristics 3 & 5) so the
    /// user is never forced to lose work or to cancel and save by hand.
    /// </summary>
    private async Task<bool> EnsureCanDiscardAsync()
    {
        if (!_editorView.IsDirty)
        {
            return true;
        }

        return await PromptUnsavedAsync() switch
        {
            UnsavedChoice.Discard => true,
            UnsavedChoice.Save => await SaveAsync(), // proceed only if the save actually completed
            _ => false,                              // Cancel
        };
    }

    private async Task<UnsavedChoice> PromptUnsavedAsync()
    {
        var tcs = new TaskCompletionSource<UnsavedChoice>();
        var save = new Button { Content = "Save", IsDefault = true };
        var discard = new Button { Content = "Don't Save" };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        save.Click += (_, _) => tcs.TrySetResult(UnsavedChoice.Save);
        discard.Click += (_, _) => tcs.TrySetResult(UnsavedChoice.Discard);
        cancel.Click += (_, _) => tcs.TrySetResult(UnsavedChoice.Cancel);

        var name = _currentPath is null ? "this document" : Path.GetFileName(_currentPath);
        var dialog = new Window
        {
            Title = "Typage",
            Width = 400,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Save changes to {name} before closing?",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { discard, cancel, save },
                    },
                },
            },
        };
        dialog.Closed += (_, _) => tcs.TrySetResult(UnsavedChoice.Cancel);
        save.Click += (_, _) => dialog.Close();
        discard.Click += (_, _) => dialog.Close();
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    /// <summary>
    /// Show a modal error stating the OS reason and a suggested action (heuristic 9; PRD §7.1).
    /// Falls back to the status bar only if there is no visible window to own a dialog.
    /// </summary>
    private void ShowError(string message, string detail)
    {
        if (!IsVisible)
        {
            _statusState.Text = message;
            return;
        }

        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true };
        var dialog = new Window
        {
            Title = "Typage",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { ok },
                    },
                },
            },
        };
        ok.Click += (_, _) => dialog.Close();
        _ = dialog.ShowDialog(this);
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        var yes = new Button { Content = "Discard", IsDefault = false };
        var no = new Button { Content = "Cancel", IsCancel = true };
        yes.Click += (_, _) => tcs.TrySetResult(true);
        no.Click += (_, _) => tcs.TrySetResult(false);

        var dialog = new Window
        {
            Title = "Typage",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { no, yes },
                    },
                },
            },
        };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);
        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    private void ShowHelp()
    {
        var help = new Window
        {
            Title = "Markdown Reference",
            Width = 420,
            Height = 480,
            Content = new ScrollViewer
            {
                Padding = new Avalonia.Thickness(16),
                Content = new TextBlock { Text = HelpText, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("monospace") },
            },
        };
        help.Show(this);
    }

    // ----- status / title -----

    private void UpdateChrome()
    {
        UpdateTitle();
        UpdateDocumentStatus();
        UpdateCaretStatus();
    }

    private void UpdateTitle()
    {
        var name = _currentPath is null ? "Untitled" : Path.GetFileName(_currentPath);
        Title = (_editorView.IsDirty ? "• " : string.Empty) + name + " — Typage";
    }

    /// <summary>Document-level status — recomputed only on text change (not on every caret move).</summary>
    private void UpdateDocumentStatus()
    {
        var stats = _editorView.Stats; // cached; whole-document scan only on text change
        _statusState.Text = _editorView.IsDirty ? "Modified" : "Saved";
        _statusCounts.Text =
            $"{stats.Words} words · {stats.Characters} chars · ~{stats.ReadingMinutes} min · {_format.DisplayName} · {stats.NewlineLabel}";
    }

    /// <summary>Caret/selection status — cheap, updated on every selection change.</summary>
    private void UpdateCaretStatus()
    {
        var pos = _editorView.CaretPosition;
        var selected = _editorView.SelectedCharacterCount;
        _statusPosition.Text = selected > 0
            ? $"Ln {pos.DisplayLine}, Col {pos.DisplayColumn}  ({selected} selected)"
            : $"Ln {pos.DisplayLine}, Col {pos.DisplayColumn}";
    }

    private const string HelpText =
        "Markdown quick reference\n\n" +
        "# H1 … ###### H6   headings\n" +
        "**bold**  *italic*  ~~strike~~  ==highlight==\n" +
        "`inline code`   ```fenced code```\n" +
        "- bullet   1. number   - [ ] task / - [x] done\n" +
        "> blockquote\n" +
        "[text](url)   ![alt](image)   <https://url>\n" +
        "| table | header |  with a |---|---| delimiter row\n" +
        "---   horizontal rule\n\n" +
        "Shortcuts: Ctrl+B bold · Ctrl+I italic · Ctrl+E code · Ctrl+1/2/3 heading\n" +
        "Ctrl+S save · Ctrl+O open · Ctrl+N new · Ctrl+Z/Y undo/redo";
}
