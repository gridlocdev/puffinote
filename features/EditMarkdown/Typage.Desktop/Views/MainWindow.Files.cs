using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Puffinote.Components.Commands;
using Puffinote.Text.Document;
using Typage.Control;

namespace Typage.Desktop.Views;

public partial class MainWindow
{
    public void OpenPath(string path) => OpenPath(path, reloading: false);

    /// <summary>
    /// Open a document the desktop handed us: Finder's "Open With", a double-click on a claimed
    /// <c>.md</c>, a second <c>typage &lt;file&gt;</c> against a process that is already running. Unlike
    /// the launch argument this can arrive over a document someone is in the middle of editing, so it
    /// asks before discarding one — and it brings the window forward, because the click that sent the
    /// file was a request to look at it.
    /// </summary>
    public void OpenFromDesktop(string path) => _ = OpenFromDesktopAsync(path);

    private async Task OpenFromDesktopAsync(string path)
    {
        // Whatever modal is up owns the screen, and at launch that is the crash-recovery prompt — whose
        // answer decides what is even in the buffer this open would replace.
        await _overlayHost.WaitUntilClosedAsync();

        if (await EnsureCanDiscardAsync())
        {
            OpenPath(path);
            Activate();
        }
    }

    /// <summary>Test seam: the "Open With" arrival, awaitable rather than fire-and-forget.</summary>
    internal Task OpenFromDesktopForTest(string path) => OpenFromDesktopAsync(path);

    /// <summary>
    /// Open <paramref name="path"/> into the window. <paramref name="reloading"/> says this is the
    /// <em>same</em> file being taken again after it changed on disk, which differs in one way that matters
    /// to the reader: the new text lands as an undoable step rather than as a fresh document, so ⌘Z gives
    /// back what was in the editor if the version on disk turns out not to be what they wanted (PRD §7.1).
    /// </summary>
    private void OpenPath(string path, bool reloading)
    {
        try
        {
            var read = TextFileCodec.Read(File.ReadAllBytes(path));
            var (text, format) = (read.Text, read.Format);
            _format = format; // remember the encoding/BOM so a save round-trips it

            // Bytes that aren't valid UTF-8 were read through a total single-byte fallback, so the file
            // round-trips — but which characters those bytes stand for is a guess, and saving would write
            // the guess back over the original. Open it, say so, and block the save until someone
            // confirms an encoding (PRD §7.1).
            _encodingUnconfirmed = read.IsReadOnly;
            if (read.IsReadOnly)
            {
                ShowError($"“{Path.GetFileName(path)}” isn't valid UTF-8.",
                    $"It has been opened as {format.DisplayName} so you can read it, and nothing has been changed.\n\n" +
                    "Saving is held back until the encoding is confirmed, because writing this guess back " +
                    "would be the edit that actually damages the file.");
            }

            if (reloading)
            {
                EditorView.ReloadText(text);
            }
            else
            {
                EditorView.LoadText(text);
            }

            SetCurrentPath(path);
            _autoSave.Cancel(); // any countdown was aimed at the document this one just replaced
            _lastKnownDiskText = text; // the on-disk baseline for external-change detection
            _settings.Settings.PushRecent(path);
            _settings.Save();
            RefreshRecentMenu();
            UpdateChrome();
            WatchCurrentFile();
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
        var recent = _settings.Settings.RecentFiles;

        // macOS native menu: refresh the submenu items in place (re-SetMenu while mounted throws on macOS).
        if (_nativeRecentItem?.Menu is { } nativeSub)
        {
            nativeSub.Items.Clear();
            if (recent.Count == 0)
            {
                nativeSub.Items.Add(new NativeMenuItem("(no recent files)") { IsEnabled = false });
                return;
            }

            foreach (var entry in recent)
            {
                var path = entry;
                nativeSub.Items.Add(new NativeMenuItem(path) { Command = new RelayCommand(() => _ = OpenRecentAsync(path)) });
            }

            nativeSub.Items.Add(new NativeMenuItemSeparator());
            nativeSub.Items.Add(new NativeMenuItem("Clear Recent") { Command = new RelayCommand(ClearRecent) });
            return;
        }

        _recentMenu.Items.Clear();
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

        EditorView.LoadText(string.Empty);
        SetCurrentPath(null);
        _autoSave.Cancel(); // ditto: the previous document is gone
        _lastKnownDiskText = null;
        _format = TextFileFormat.Utf8NoBom; // new documents default to UTF-8 (no BOM)
        _encodingUnconfirmed = false;       // nothing was guessed about a document that has no bytes yet
        UpdateChrome();
        WatchCurrentFile(); // nothing to watch now
    }

    /// <summary>
    /// Point the shell — and the surface — at the file now being edited. The editor needs the path of its own
    /// accord: it anchors a relative image path against the document's folder, and names an HTML export after
    /// it. An unsaved document (null) is what puts an inserted image through <see cref="ImageStaging"/>.
    /// </summary>
    private void SetCurrentPath(string? path)
    {
        _currentPath = path;
        EditorView.DocumentPath = path;
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
            FileTypeFilter = [MarkdownType, FilePickerFileTypes.All],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            // A storage-picker await can resume off the UI thread; OpenPath touches the editor and
            // settings, so marshal it back (the same hardening as the clipboard path in the surface).
            await Dispatcher.UIThread.InvokeAsync(() => OpenPath(path));
        }
    }

    // ----- image staging (PRD §8.3 images) -----
    // Inserting an image is the editor's (MarkdownEditorView.InsertImageAsync) — every host does it the same
    // way. What remains here is the half that genuinely needs a shell: at save time, offering to move the
    // images staged for a then-unsaved document into their permanent home.

    /// <summary>
    /// When the document being saved to <paramref name="targetPath"/> still references staged (scratch)
    /// images, offer to copy them into an <c>images/</c> folder beside the document (created if missing) and
    /// rewrite the Markdown to point there relatively. The files are physically copied out of the scratch
    /// directory, not just re-pathed. Declining leaves the staged paths intact (the document still opens).
    /// Runs before the bytes are written so the saved file already carries the permanent paths.
    /// </summary>
    private async Task ReconcileStagedImagesAsync(string targetPath)
    {
        var staged = ImageStaging.StagedReferences(EditorView.Text);
        if (staged.Count == 0)
        {
            return;
        }

        var noun = staged.Count == 1 ? "image" : "images";
        if (!await ConfirmAsync(
                $"Copy {staged.Count} staged {noun} into an \"images\" folder next to the document, and update the links?",
                confirmLabel: "Copy", denyLabel: "Keep As-Is"))
        {
            return;
        }

        var (newText, error) = ImageStaging.Reconcile(EditorView.Text, targetPath, staged);
        if (error is not null)
        {
            ShowError("Couldn't copy some staged images.",
                $"{error}\n\nThe document will be saved with its current image links.");
        }

        if (newText != EditorView.Text)
        {
            var buffer = EditorView.Editor.Buffer;
            buffer.ReplaceRange(0, buffer.Document.Length, newText);
        }
    }

    private async Task<bool> SaveAsync()
    {
        if (_currentPath is null)
        {
            return await SaveAsAsync();
        }

        if (!await ConfirmEncodingAsync())
        {
            return false;
        }

        await ReconcileStagedImagesAsync(_currentPath);
        return WriteToPath(_currentPath);
    }

    private async Task<bool> SaveAsAsync()
    {
        if (!await ConfirmEncodingAsync())
        {
            return false;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Markdown",
            DefaultExtension = "md",
            SuggestedFileName = Path.GetFileName(_currentPath) ?? "Untitled.md",
            FileTypeChoices = [MarkdownType],
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            // Hop to the UI thread (the picker await may resume off it) before the modal + editor edits.
            await Dispatcher.UIThread.InvokeAsync(() => { });

            // Offer to move any staged images into their permanent home before the bytes hit disk.
            await ReconcileStagedImagesAsync(path);

            if (WriteToPath(path))
            {
                SetCurrentPath(path);
                _settings.Settings.PushRecent(path);
                _settings.Save();
                RefreshRecentMenu();
                UpdateChrome();
                WatchCurrentFile(); // follow the file we just saved as
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True while the open document's encoding was <i>guessed</i> (its bytes were not valid UTF-8).
    /// A save would write that guess back, so it is held until a person confirms — which is the one
    /// point in the flow where "are these really Latin-1?" is a question anyone can answer.
    /// </summary>
    private bool _encodingUnconfirmed;

    private async Task<bool> ConfirmEncodingAsync()
    {
        if (!_encodingUnconfirmed)
        {
            return true;
        }

        bool confirmed = await ConfirmAsync(
            $"This file wasn't valid UTF-8 and was read as {_format.DisplayName}. Saving now writes it back that way — " +
            "if that guess is wrong, the characters it got wrong become wrong on disk.",
            confirmLabel: $"Save as {_format.DisplayName}",
            denyLabel: "Cancel");

        _encodingUnconfirmed = !confirmed; // confirmed once is confirmed for the rest of the session
        return confirmed;
    }

    private bool WriteToPath(string path)
    {
        try
        {
            // The editor library owns the one atomic (temp-then-rename) write; don't keep a second copy here.
            MarkdownDocumentSession.AtomicWrite(path, TextFileCodec.Encode(EditorView.Text, _format));
            _lastKnownDiskText = EditorView.Text; // so the watcher recognises this as our own save
            EditorView.MarkSaved();
            _recovery.Clear(); // the save is durable now; drop the crash-recovery snapshot

            // The document is on disk, so a countdown aimed at it has nothing left to write. And a manual
            // save that succeeded is the answer to whatever made auto-save stand itself down.
            _autoSave.Cancel();
            _autoSave.Suspended = false;
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
        if (_currentPath is null || !EditorView.IsDirty)
        {
            return;
        }

        if (await ConfirmAsync("Revert to the last saved version? Unsaved changes will be lost."))
        {
            OpenPath(_currentPath);
        }
    }
}
