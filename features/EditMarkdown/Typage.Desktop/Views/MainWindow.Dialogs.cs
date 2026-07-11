using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Overlays;
using Typage.Control.Controls;
using Typage.Desktop.Services;

namespace Typage.Desktop.Views;

public partial class MainWindow
{
    // ----- unsaved-changes guard (heuristics 3, 5) -----

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (EditorView.IsDirty && !_closingConfirmed)
        {
            e.Cancel = true;
            if (await EnsureCanDiscardAsync())
            {
                _closingConfirmed = true;
                Close();
            }
        }
    }

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
        if (!EditorView.IsDirty)
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
        var name = _currentPath is null ? "this document" : Path.GetFileName(_currentPath);
        // Esc / backdrop cancel the close, never silently discarding or saving.
        return await _overlayHost.ShowModalAsync(new ChoiceDialog<UnsavedChoice>(
            heading: string.Empty,
            message: $"Save changes to {name} before closing?",
            dismiss: UnsavedChoice.Cancel,
            new Choice<UnsavedChoice>("Don't Save", UnsavedChoice.Discard),
            new Choice<UnsavedChoice>("Cancel", UnsavedChoice.Cancel, IsCancel: true),
            new Choice<UnsavedChoice>("Save", UnsavedChoice.Save, IsDefault: true)));
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

        // The bold message becomes the modal heading; the OS reason / suggested action is the body.
        _ = _overlayHost.ShowModalAsync(new MessageDialog(heading: message, message: detail));
    }

    /// <summary>
    /// The ⌘K link dialog: a "Display text" field (prefilled from the selection / word under the caret) and a
    /// "Location" field, with Cancel / Add. On Add the link is inserted over the current target; spaces in the
    /// location are encoded as <c>%20</c> so the URL survives as one Markdown destination.
    /// </summary>
    private async Task ShowLinkDialogAsync()
    {
        var ed = EditorView.Editor;
        var entered = await _overlayHost.ShowModalAsync(new LinkDialog(ed.SuggestedLinkText()));
        if (entered is not { } values)
        {
            return;
        }

        ed.InsertLinkWithText(values.Text, values.Url);
    }

    /// <summary>
    /// A yes/no prompt shown as an internal modal in the shared overlay host (no separate OS window).
    /// Confirm is not the default button, and Esc / backdrop deny — so a stray Enter can't trigger a
    /// destructive action. Returns true only when the user confirms.
    /// </summary>
    private async Task<bool> ConfirmAsync(string message, string confirmLabel = "Discard", string denyLabel = "Cancel") =>
        await _overlayHost.ShowModalAsync(
            new ConfirmDialog(heading: string.Empty, message: message, confirmLabel: confirmLabel, denyLabel: denyLabel, confirmIsDefault: false));

    /// <summary>The Preferences dialog (PRD §7.9): theme, tab width, and toolbar visibility.</summary>
    private async void ShowSettings()
    {
        var s = _settings.Settings;

        var themeCombo = new ComboBox
        {
            ItemsSource = new[] { ThemeLabel(AppTheme.System), ThemeLabel(AppTheme.Light), ThemeLabel(AppTheme.Dark), ThemeLabel(AppTheme.HighContrast) },
            SelectedIndex = (int)s.Theme,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var tabWidth = new NumericUpDown { Minimum = 1, Maximum = 8, Increment = 1, Value = s.TabWidth, FormatString = "0" };
        var padding = new NumericUpDown { Minimum = 0, Maximum = 200, Increment = 4, Value = s.EditorPadding, FormatString = "0" };
        var paddingY = new NumericUpDown { Minimum = 0, Maximum = 200, Increment = 4, Value = s.EditorPaddingVertical, FormatString = "0" };
        var autosave = new NumericUpDown { Minimum = 0, Maximum = 600, Increment = 5, Value = s.AutosaveSeconds, FormatString = "0" };
        var completedTasks = new ComboBox
        {
            ItemsSource = new[] { "Leave in place", "Move to top", "Move to bottom" },
            SelectedIndex = (int)s.CompletedTaskSort,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var viewMode = new ComboBox
        {
            ItemsSource = new[]
            {
                ViewModeLabel(MarkdownEditor.ViewMode.Rich),
                ViewModeLabel(MarkdownEditor.ViewMode.Formatted),
                ViewModeLabel(MarkdownEditor.ViewMode.RawText),
            },
            SelectedIndex = (int)s.View,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var showToolbar = new CheckBox { Content = "Show the formatting toolbar", IsChecked = s.ShowToolbar };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Width = 340,
        };
        AddSettingRow(grid, 0, "Theme", themeCombo);
        AddSettingRow(grid, 1, "Tab width", tabWidth);
        AddSettingRow(grid, 2, "Horizontal padding (px)", padding);
        AddSettingRow(grid, 3, "Vertical padding (px)", paddingY);
        AddSettingRow(grid, 4, "Autosave (sec, 0 = off)", autosave);
        AddSettingRow(grid, 5, "Completed tasks", completedTasks);
        AddSettingRow(grid, 6, "Default view", viewMode);
        Grid.SetRow(showToolbar, 7);
        Grid.SetColumnSpan(showToolbar, 2);
        showToolbar.Margin = new Avalonia.Thickness(0, 8, 0, 0);
        grid.Children.Add(showToolbar);

        if (!await _overlayHost.ShowModalAsync(new FormDialog("Preferences", grid)))
        {
            return; // Cancel / Esc — leave settings untouched
        }

        var theme = (AppTheme)System.Math.Clamp(themeCombo.SelectedIndex, 0, 3);
        s.Theme = theme;
        ApplyTheme(theme);
        s.TabWidth = (int)(tabWidth.Value ?? 4);
        EditorView.Editor.IndentWidth = s.TabWidth;
        s.EditorPadding = (int)(padding.Value ?? 20);
        EditorView.Editor.HorizontalPadding = s.EditorPadding;
        EditorView.Editor.MaxContentWidth = s.EditorMaxContentWidth;
        s.EditorPaddingVertical = (int)(paddingY.Value ?? 14);
        EditorView.Editor.VerticalPadding = s.EditorPaddingVertical;
        s.ShowToolbar = showToolbar.IsChecked == true;
        if (_toolbar is not null && !_focusMode)
        {
            _toolbar.IsVisible = s.ShowToolbar;
        }

        _toggleToolbarItem?.IsChecked = s.ShowToolbar;

        s.AutosaveSeconds = (int)(autosave.Value ?? 30);
        SetupAutosave(); // pick up the new interval (or disable when 0)

        s.CompletedTaskSort = (MarkdownEditor.CompletedTaskSortMode)System.Math.Clamp(completedTasks.SelectedIndex, 0, 2);
        EditorView.Editor.CompletedTaskSorting = s.CompletedTaskSort;

        SetViewMode((MarkdownEditor.ViewMode)System.Math.Clamp(viewMode.SelectedIndex, 0, 2)); // applies + syncs menu + saves

        _settings.Save();
    }

    private static void AddSettingRow(Grid grid, int row, string label, Avalonia.Controls.Control control)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 6, 12, 0), MinWidth = 90 };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        control.Margin = new Avalonia.Thickness(0, 6, 0, 0);
        grid.Children.Add(text);
        grid.Children.Add(control);
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
