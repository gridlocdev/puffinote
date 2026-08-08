using Avalonia.Controls;
using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;
using Puffinote.Components.Shell;
using Puffinote.Components.Theming;
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
    /// A yes/no prompt shown as an internal modal in the shared overlay host (no separate OS window).
    /// Confirm is not the default button, and Esc / backdrop deny — so a stray Enter can't trigger a
    /// destructive action. Returns true only when the user confirms.
    /// </summary>
    private async Task<bool> ConfirmAsync(string message, string confirmLabel = "Discard", string denyLabel = "Cancel") =>
        await _overlayHost.ShowModalAsync(
            new ConfirmDialog(heading: string.Empty, message: message, confirmLabel: confirmLabel, denyLabel: denyLabel, confirmIsDefault: false));

    /// <summary>
    /// The Preferences dialog (PRD §7.9) — every option in Typage's <c>settings.json</c> that is a
    /// preference rather than a record of what you did (the recent-files list is the latter, and the
    /// File ▸ Open Recent menu is where it belongs). Built from the shared
    /// <see cref="SettingsEditorView"/> so the five apps' Preferences read as one surface; this method
    /// keeps only what is Typage's own — which settings exist, and what applying one means for the live
    /// editor, which is more than writing a value for most of them.
    /// </summary>
    private async void ShowSettings()
    {
        var s = _settings.Settings;
        var form = BuildSettingsForm(s);

        if (!await _overlayHost.ShowModalAsync(new FormDialog("Preferences", form)))
        {
            return; // Cancel / Esc — leave settings untouched
        }

        form.Commit();
        ApplySettings(s);
        _settings.Save();
    }

    /// <summary>The Preferences form over <paramref name="s"/> (also the seam the UI tests drive).</summary>
    internal static SettingsEditorView BuildSettingsForm(AppSettings s) => new(
    [
        new SettingsSection("Appearance",
        [
            ChoiceSetting.ForEnum<AppTheme>("Theme", () => s.Theme, v => s.Theme = v,
                [ThemeLabel(AppTheme.System), ThemeLabel(AppTheme.Light), ThemeLabel(AppTheme.Dark), ThemeLabel(AppTheme.HighContrast)],
                ThemePreferences.Hint),
            ChoiceSetting.ForEnum<MarkdownEditor.ViewMode>("Default view", () => s.View, v => s.View = v,
                [
                    ViewModeLabel(MarkdownEditor.ViewMode.Rich),
                    ViewModeLabel(MarkdownEditor.ViewMode.Formatted),
                    ViewModeLabel(MarkdownEditor.ViewMode.RawText),
                ]),
            new ToggleSetting("Show the formatting toolbar", () => s.ShowToolbar, v => s.ShowToolbar = v),
        ]),
        new SettingsSection("Editor",
        [
            new NumberSetting("Tab width", () => s.TabWidth, v => s.TabWidth = (int)v, 1, 8),
            new NumberSetting("Horizontal padding", () => s.EditorPadding, v => s.EditorPadding = (int)v, 0, 200, step: 4, hint: "Pixels of inset on each side of the text."),
            new NumberSetting("Vertical padding", () => s.EditorPaddingVertical, v => s.EditorPaddingVertical = (int)v, 0, 200, step: 4, hint: "Pixels of inset above and below the text."),
            new NumberSetting("Reading width", () => s.EditorMaxContentWidth, v => s.EditorMaxContentWidth = (int)v, 0, 4000, step: 20,
                hint: "The widest the text column may grow, in pixels. 0 uses the full window width."),
        ]),
        new SettingsSection("Editing",
        [
            AutoSave.IntervalField(() => s.AutoSaveSeconds, v => s.AutoSaveSeconds = v),
            new NumberSetting("Recovery snapshots", () => s.RecoverySnapshotSeconds, v => s.RecoverySnapshotSeconds = (int)v, 0, 600, step: 5,
                hint: "Seconds between crash-recovery snapshots of unsaved work — the safety net for a document "
                    + "that has never been saved, and so has nowhere to auto-save to. 0 turns them off."),
            ChoiceSetting.ForEnum<MarkdownEditor.CompletedTaskSortMode>("Completed tasks",
                () => s.CompletedTaskSort, v => s.CompletedTaskSort = v,
                ["Leave in place", "Move to top", "Move to bottom"]),
        ]),
    ]);

    /// <summary>Push the committed settings into the live window — most of them mean more than a value.</summary>
    private void ApplySettings(AppSettings s)
    {
        ApplyTheme(s.Theme);
        EditorView.Editor.IndentWidth = s.TabWidth;
        EditorView.Editor.HorizontalPadding = s.EditorPadding;
        EditorView.Editor.VerticalPadding = s.EditorPaddingVertical;
        EditorView.Editor.MaxContentWidth = s.EditorMaxContentWidth;

        if (!_focusMode)
        {
            EditorView.ShowCommandBar = s.ShowToolbar;
        }

        _toggleToolbarItem?.IsChecked = s.ShowToolbar;

        SetupRecoverySnapshots(); // pick up the new interval (or disable when 0)
        _autoSave.IntervalSeconds = s.AutoSaveSeconds; // ditto, on the document already open
        EditorView.Editor.CompletedTaskSorting = s.CompletedTaskSort;

        // Applies the mode; the surface's ViewModeChanged then syncs the View ▸ radio and persists it.
        EditorView.ViewMode = s.View;
    }
}
