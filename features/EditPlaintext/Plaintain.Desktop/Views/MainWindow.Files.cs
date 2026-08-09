using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Puffinote.Components.Commands;
using Puffinote.Components.Overlays;
using Puffinote.Text.Document;

namespace Plaintain.Desktop.Views;

public sealed partial class MainWindow
{
    /// <summary>
    /// True while the open document's encoding was <i>guessed</i> — its bytes were not valid UTF-8, so
    /// they were read through Latin-1 to keep them intact. A save would write the guess back, so it is
    /// held until someone confirms. This is the case Plaintain exists to get right: a swiss-army text
    /// reader that mangles a Latin-1 file is worse than one that refuses to open it.
    /// </summary>
    private bool EncodingUnconfirmed { get; set; }

    /// <summary>The file types the open dialog offers. Plaintain opens anything, so "anything" is first.</summary>
    private static readonly FilePickerFileType TextType = new("Text files")
    {
        Patterns = ["*.txt", "*.text", "*.log", "*.md", "*.conf", "*.cfg", "*.ini", "*.env", "*.json", "*.yaml", "*.yml", "*.csv"],
    };

    public void OpenPath(string path)
    {
        try
        {
            var read = TextFileCodec.Read(File.ReadAllBytes(path));
            _format = read.Format;
            EncodingUnconfirmed = read.IsReadOnly;
            EditorView.Editor.IsReadOnlyEncoding = read.IsReadOnly;
            EditorView.LoadText(read.Text);
            CurrentPath = path;
            _autoSave.Cancel(); // any countdown was aimed at the document this one just replaced
            _settings.Settings.PushRecent(path);
            _settings.Save();
            RefreshRecentMenu();
            UpdateChrome();

            if (read.IsReadOnly)
            {
                _ = _overlay.ShowModalAsync(new MessageDialog(
                    heading: $"“{Path.GetFileName(path)}” isn't valid UTF-8.",
                    message: $"It has been opened as {read.Format.DisplayName} so you can read it, and every byte is intact.\n\n" +
                             "Saving is held back until the encoding is confirmed — writing this guess back is the step " +
                             "that would actually damage the file."));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = _overlay.ShowModalAsync(new MessageDialog(
                heading: $"Couldn't open “{Path.GetFileName(path)}”.",
                message: $"{ex.Message}\n\nCheck that the file still exists and that you have permission to read it."));
        }
    }

    private async Task NewDocumentAsync()
    {
        if (!await EnsureCanDiscardAsync())
        {
            return;
        }

        EditorView.LoadText(string.Empty);
        CurrentPath = null;
        _autoSave.Cancel(); // ditto: the previous document is gone
        _format = TextFileFormat.Utf8NoBom;
        EncodingUnconfirmed = false;
        EditorView.Editor.IsReadOnlyEncoding = false;
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
            Title = "Open",
            AllowMultiple = false,
            FileTypeFilter = [TextType, FilePickerFileTypes.All],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            // A storage-picker await can resume off the UI thread, and OpenPath touches the editor.
            await Dispatcher.UIThread.InvokeAsync(() => OpenPath(path));
        }
    }

    private async Task<bool> SaveAsync()
    {
        if (CurrentPath is null)
        {
            return await SaveAsAsync();
        }

        return await ConfirmEncodingAsync() && WriteToPath(CurrentPath);
    }

    private async Task<bool> SaveAsAsync()
    {
        if (!await ConfirmEncodingAsync())
        {
            return false;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save",
            SuggestedFileName = Path.GetFileName(CurrentPath) ?? "Untitled.txt",
            FileTypeChoices = [TextType],
        });

        if (file?.TryGetLocalPath() is not { } path || !WriteToPath(path))
        {
            return false;
        }

        CurrentPath = path;
        _settings.Settings.PushRecent(path);
        _settings.Save();
        RefreshRecentMenu();
        UpdateChrome();
        return true;
    }

    /// <summary>
    /// Ask before writing back a guessed encoding. This is the one moment in the flow where "are those
    /// bytes really Latin-1?" is a question a person can actually answer, so it is where it is asked.
    /// </summary>
    private async Task<bool> ConfirmEncodingAsync()
    {
        if (!EncodingUnconfirmed)
        {
            return true;
        }

        bool confirmed = await _overlay.ShowModalAsync(new ConfirmDialog(
            heading: "Confirm the encoding",
            message: $"This file wasn't valid UTF-8 and was read as {_format.DisplayName}. Saving now writes it back that " +
                     "way — if the guess is wrong, the characters it got wrong become wrong on disk.",
            confirmLabel: $"Save as {_format.DisplayName}",
            denyLabel: "Cancel",
            confirmIsDefault: false));

        EncodingUnconfirmed = !confirmed; // confirmed once, confirmed for the session
        EditorView.Editor.IsReadOnlyEncoding = EncodingUnconfirmed;
        UpdateChrome();
        return confirmed;
    }

    /// <summary>
    /// Write the open file back once editing has paused, if it can be written without asking anyone
    /// anything. Every early return is a case where a save would have to open a dialog, and an auto-save
    /// that opens dialogs is a worse feature than no auto-save: an untitled buffer has nowhere to go (Save
    /// As is a question), a file read through the Latin-1 fallback must not have that guess written back
    /// until someone confirms it, and a modal already up owns the screen.
    /// </summary>
    private void AutoSaveTick()
    {
        if (CurrentPath is not { } path || !EditorView.IsDirty || EncodingUnconfirmed || _overlay.IsOpen)
        {
            return;
        }

        try
        {
            AtomicWrite(path, TextFileCodec.Encode(EditorView.Text, _format));
            EditorView.MarkSaved();
            UpdateChrome();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Stand auto-save down and say so once: the disk that refused this write will refuse the next
            // one too, and the same modal every thirty seconds is a siege, not a report. ⌘S still works,
            // and a manual save that succeeds switches auto-save back on.
            _autoSave.Suspended = true;
            _ = _overlay.ShowModalAsync(new MessageDialog(
                heading: "Auto-save couldn't write the document.",
                message: $"{ex.Message}\n\nAuto-save is paused until you save successfully yourself (⌘S)."));
        }
    }

    /// <summary>Run one auto-save cycle now; for tests (a DispatcherTimer can't tick headlessly).</summary>
    internal Task AutoSaveNowForTest() => _autoSave.RunAsync();

    /// <summary>Whether an auto-save is armed and waiting out the pause (test seam).</summary>
    internal bool AutoSavePendingForTest => _autoSave.IsPending;

    private bool WriteToPath(string path)
    {
        try
        {
            AtomicWrite(path, TextFileCodec.Encode(EditorView.Text, _format));
            EditorView.MarkSaved();

            // The document is on disk, so a countdown aimed at it has nothing left to write — and a manual
            // save that succeeded is the answer to whatever made auto-save stand itself down.
            _autoSave.Cancel();
            _autoSave.Suspended = false;
            UpdateChrome();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = _overlay.ShowModalAsync(new MessageDialog(
                heading: "Couldn't save the document.",
                message: $"{ex.Message}\n\nTry File ▸ Save As to write to a different location."));
            return false;
        }
    }

    /// <summary>
    /// Write via a temp file <b>in the target's own directory</b>, then rename — the family's golden
    /// rule. Same directory because a rename across filesystems is a copy, and a copy is not atomic:
    /// a crash halfway through would leave the original truncated rather than untouched.
    /// </summary>
    private static void AtomicWrite(string path, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        string temp = Path.Combine(directory, $".{Path.GetFileName(path)}.plaintain-tmp");
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    private async Task<bool> EnsureCanDiscardAsync()
    {
        if (!EditorView.IsDirty)
        {
            return true;
        }

        return await _overlay.ShowModalAsync(new ConfirmDialog(
            heading: string.Empty,
            message: $"Discard unsaved changes to {(CurrentPath is null ? "this document" : Path.GetFileName(CurrentPath))}?",
            confirmLabel: "Discard",
            denyLabel: "Cancel",
            confirmIsDefault: false));
    }

    private void RefreshRecentMenu()
    {
        var recent = _settings.Settings.RecentFiles;

        // macOS: refill the mounted submenu's items in place. Re-setting the whole NativeMenu while it is
        // already installed throws there, so the submenu is built once and only its contents change.
        if (_nativeRecentMenu?.Menu is { } native)
        {
            native.Items.Clear();
            if (recent.Count == 0)
            {
                native.Items.Add(new NativeMenuItem("(no recent files)") { IsEnabled = false });
                return;
            }

            foreach (string entry in recent)
            {
                string path = entry;
                native.Items.Add(new NativeMenuItem(path) { Command = new RelayCommand(() => _ = OpenRecentAsync(path)) });
            }

            return;
        }

        _recentMenu.Items.Clear();
        if (recent.Count == 0)
        {
            _recentMenu.Items.Add(new MenuItem { Header = "(no recent files)", IsEnabled = false });
            return;
        }

        foreach (string entry in recent)
        {
            string path = entry;
            _recentMenu.Items.Add(new MenuItem { Header = path, Command = new RelayCommand(() => _ = OpenRecentAsync(path)) });
        }
    }

    private async Task OpenRecentAsync(string path)
    {
        if (await EnsureCanDiscardAsync())
        {
            OpenPath(path);
        }
    }

    private bool _closingConfirmed;

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingConfirmed || !EditorView.IsDirty)
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

    // ---- Test seams ------------------------------------------------------

    internal string? CurrentPathForTest => CurrentPath;

    internal bool EncodingUnconfirmedForTest => EncodingUnconfirmed;

    internal string EncodingNameForTest => _format.DisplayName;

    internal void SaveForTest(string path) => WriteToPath(path);
}
