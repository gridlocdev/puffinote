using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Puffinote.Components.Commands;
using Typage.Desktop.Services;
using Typage.Engine.Document;
using Typage.Engine.Html;

namespace Typage.Desktop.Views;

public partial class MainWindow
{
    public void OpenPath(string path)
    {
        try
        {
            var (text, format) = TextFileCodec.Decode(File.ReadAllBytes(path));
            _format = format; // remember the encoding/BOM so a save round-trips it
            EditorView.LoadText(text);
            _currentPath = path;
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
        _currentPath = null;
        _lastKnownDiskText = null;
        _format = TextFileFormat.Utf8NoBom; // new documents default to UTF-8 (no BOM)
        UpdateChrome();
        WatchCurrentFile(); // nothing to watch now
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

    // ----- image insertion + staging (PRD §8.3 images) -----

    /// <summary>
    /// Pick an image file and insert a reference to it. A <b>saved</b> document references the picked image
    /// by a relative path (it has a folder to anchor against). An <b>unsaved</b> document has no such anchor,
    /// so the image is copied into a scratch directory and referenced by that absolute staged path; the next
    /// save offers to move it into the document's permanent home (see <see cref="ReconcileStagedImagesAsync"/>).
    /// </summary>
    private async Task InsertImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Insert Image",
            AllowMultiple = false,
            FileTypeFilter = [ImageType, FilePickerFileTypes.All],
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } source)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var ed = EditorView.Editor;
            var alt = Path.GetFileNameWithoutExtension(source);
            try
            {
                var path = _currentPath is { } docPath
                    ? ImageStaging.RelativeUrl(docPath, source)              // saved → relative path in place
                    : ImageStaging.Stage(source, System.Guid.NewGuid().ToString("N")); // unsaved → stage a copy
                ed.InsertImageReference(ImageStaging.EncodeUrl(path), alt); // encode so spaces don't break the link
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowError("Couldn't insert the image.",
                    $"{ex.Message}\n\nThe image could not be staged for the unsaved document.");
            }
        });
    }

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

        await ReconcileStagedImagesAsync(_currentPath);
        return WriteToPath(_currentPath);
    }

    private async Task<bool> SaveAsAsync()
    {
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
                _currentPath = path;
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

    private bool WriteToPath(string path)
    {
        try
        {
            AtomicWrite(path, TextFileCodec.Encode(EditorView.Text, _format));
            _lastKnownDiskText = EditorView.Text; // so the watcher recognises this as our own save
            EditorView.MarkSaved();
            _recovery.Clear(); // the save is durable now; drop the crash-recovery snapshot
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

    private async Task ExportHtmlAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export HTML",
            DefaultExtension = "html",
            SuggestedFileName = (Path.GetFileNameWithoutExtension(_currentPath) ?? "Untitled") + ".html",
            FileTypeChoices = [new FilePickerFileType("HTML") { Patterns = ["*.html", "*.htm"] }],
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            // Read the editor text and report errors on the UI thread (the picker await may resume off it).
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var title = Path.GetFileNameWithoutExtension(path);
                var html = HtmlRenderer.RenderDocument(Engine.Document.TextDocument.FromString(EditorView.Text), title);
                try
                {
                    AtomicWrite(path, new UTF8Encoding(false).GetBytes(html)); // HTML export is always UTF-8
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ShowError("Couldn't export HTML.",
                        $"{ex.Message}\n\nCheck that the destination folder is writable.");
                }
            });
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
}
