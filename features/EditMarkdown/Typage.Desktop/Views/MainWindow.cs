using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Typage.Control.Views;
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
    private Border? _toolbar;

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
        root.Children.Add(BuildStatusBar());
        root.Children.Add(_editorView);
        Content = root;

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
        var toggleToolbar = new MenuItem
        {
            Header = "Show _Toolbar",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _settings.Settings.ShowToolbar,
        };
        toggleToolbar.Click += (_, _) =>
        {
            var show = toggleToolbar.IsChecked;
            _settings.Settings.ShowToolbar = show;
            if (_toolbar is not null)
            {
                _toolbar.IsVisible = show;
            }

            _settings.Save();
        };
        view.Items.Add(toggleToolbar);

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

    private Border BuildToolbar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(6, 3) };
        panel.Children.Add(ToolButton("B", "Bold", () => _editorView.Editor.ToggleBold(), bold: true));
        panel.Children.Add(ToolButton("I", "Italic", () => _editorView.Editor.ToggleItalic(), italic: true));
        panel.Children.Add(ToolButton("</>", "Inline code", () => _editorView.Editor.ToggleInlineCode()));
        panel.Children.Add(ToolButton("H1", "Heading 1", () => _editorView.Editor.ApplyHeading(1)));
        panel.Children.Add(ToolButton("H2", "Heading 2", () => _editorView.Editor.ApplyHeading(2)));
        var border = new Border
        {
            Child = panel,
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
        };
        DockPanel.SetDock(border, Dock.Top);
        return border;
    }

    private Button ToolButton(string label, string tooltip, Action action, bool bold = false, bool italic = false)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Avalonia.Thickness(8, 2),
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
            Command = new RelayCommand(action),
        };
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private Border BuildStatusBar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Avalonia.Thickness(8, 3) };
        panel.Children.Add(_statusState);
        panel.Children.Add(_statusPosition);
        panel.Children.Add(_statusCounts);
        var border = new Border
        {
            Child = panel,
            BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
        };
        DockPanel.SetDock(border, Dock.Bottom);
        return border;
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
