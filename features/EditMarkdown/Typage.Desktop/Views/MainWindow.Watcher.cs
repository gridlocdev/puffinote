using Avalonia.Threading;
using Puffinote.Components.Overlays;
using Typage.Engine.Document;

namespace Typage.Desktop.Views;

public partial class MainWindow
{
    // ----- crash recovery (PRD §9.4) -----

    private void SetupAutosave()
    {
        _autosaveTimer?.Stop();
        var seconds = _settings.Settings.AutosaveSeconds;
        if (seconds <= 0)
        {
            _autosaveTimer = null; // autosave disabled
            return;
        }

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _autosaveTimer.Tick += (_, _) => AutosaveTick();
        _autosaveTimer.Start();
    }

    /// <summary>Snapshot the buffer if it has unsaved changes, else drop any stale snapshot.</summary>
    private void AutosaveTick()
    {
        if (EditorView.IsDirty)
        {
            _recovery.Save(_currentPath, EditorView.Text);
        }
        else
        {
            _recovery.Clear();
        }
    }

    /// <summary>Run one autosave cycle; for tests (the timer can't tick headlessly).</summary>
    internal void AutosaveTickForTest() => AutosaveTick();

    /// <summary>On startup, if a snapshot from an unclean shutdown exists, offer to recover it (PRD §9.4).</summary>
    private async Task CheckForRecoveryAsync()
    {
        if (_recoveryChecked)
        {
            return;
        }

        _recoveryChecked = true;
        if (_recovery.TryLoad() is not { } snapshot)
        {
            return;
        }

        if (await ConfirmAsync($"Recover unsaved changes from {snapshot.SavedAtUtc.ToLocalTime():g}?", confirmLabel: "Restore", denyLabel: "Discard"))
        {
            // Re-adopt the original file (encoding + disk baseline), then overlay the recovered text as dirty.
            if (snapshot.Path is not null && File.Exists(snapshot.Path))
            {
                try
                {
                    var (diskText, format) = TextFileCodec.Decode(File.ReadAllBytes(snapshot.Path));
                    _format = format;
                    _lastKnownDiskText = diskText;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The original is gone or unreadable — recover the text as an Untitled buffer.
                }

                _currentPath = snapshot.Path;
            }

            EditorView.LoadRecovered(snapshot.Text);
            UpdateChrome();
            WatchCurrentFile();
        }
        else
        {
            _recovery.Clear();
        }
    }

    // ----- external-change detection (PRD §7.1) -----

    /// <summary>What an on-disk change to the open file means relative to the editor's state.</summary>
    internal enum DiskChange
    {
        /// <summary>The disk matches what we last read/wrote (our own save or a no-op touch).</summary>
        None,

        /// <summary>The disk changed and the buffer is clean — safe to reload silently.</summary>
        ReloadClean,

        /// <summary>The disk changed and the buffer has unsaved edits — a real conflict.</summary>
        Conflict,
    }

    /// <summary>Classify an on-disk text against the last-known disk baseline and the dirty flag.</summary>
    internal DiskChange ClassifyDiskChange(string diskText)
    {
        if (diskText == _lastKnownDiskText)
        {
            return DiskChange.None;
        }

        return EditorView.IsDirty ? DiskChange.Conflict : DiskChange.ReloadClean;
    }

    private void WatchCurrentFile()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (_currentPath is null)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_currentPath));
            if (dir is null)
            {
                return;
            }

            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_currentPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            // Events arrive on a thread-pool thread; marshal the handling to the UI thread.
            _watcher.Changed += (_, _) => Dispatcher.UIThread.Post(HandleExternalChange);
            _watcher.Created += (_, _) => Dispatcher.UIThread.Post(HandleExternalChange);
            _watcher.Renamed += (_, _) => Dispatcher.UIThread.Post(HandleExternalChange);
        }
        catch (Exception)
        {
            _watcher = null; // watching is best-effort; never let it break editing
        }
    }

    private async void HandleExternalChange()
    {
        if (_currentPath is null || _conflictPromptOpen)
        {
            return;
        }

        string diskText;
        try
        {
            (diskText, _) = TextFileCodec.Decode(File.ReadAllBytes(_currentPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // the writer may still hold the file mid-save; a later event will retry
        }

        switch (ClassifyDiskChange(diskText))
        {
            case DiskChange.None:
                return;
            case DiskChange.ReloadClean:
                OpenPath(_currentPath); // no local edits — pick up the new version silently
                return;
            case DiskChange.Conflict:
                _conflictPromptOpen = true;
                try
                {
                    switch (await PromptConflictAsync())
                    {
                        case ConflictChoice.Reload:
                            OpenPath(_currentPath);
                            break;
                        case ConflictChoice.SaveAs:
                            await SaveAsAsync();
                            break;
                        case ConflictChoice.KeepMine:
                            _lastKnownDiskText = diskText; // accept the new disk text as the baseline; overwrite on save
                            break;
                    }
                }
                finally
                {
                    _conflictPromptOpen = false;
                }

                return;
        }
    }

    private enum ConflictChoice
    {
        Reload,
        KeepMine,
        SaveAs,
    }

    /// <summary>
    /// Offer the three-way choice when the file changed on disk while we have unsaved edits (PRD §7.1):
    /// reload (lose local edits), keep mine (overwrite on save), or fork via Save As.
    /// </summary>
    private async Task<ConflictChoice> PromptConflictAsync()
    {
        var name = _currentPath is null ? "This file" : Path.GetFileName(_currentPath);
        // Esc / backdrop keep the user's work (the safe default), same as closing the old window did.
        return await _overlayHost.ShowModalAsync(new ChoiceDialog<ConflictChoice>(
            heading: string.Empty,
            message: $"{name} changed on disk, but you have unsaved changes here. " +
                     "Reload to use the version on disk, keep your changes (they’ll overwrite the file when you save), " +
                     "or save a separate copy.",
            dismiss: ConflictChoice.KeepMine,
            new Choice<ConflictChoice>("Reload", ConflictChoice.Reload),
            new Choice<ConflictChoice>("Save As…", ConflictChoice.SaveAs),
            new Choice<ConflictChoice>("Keep Mine", ConflictChoice.KeepMine, IsDefault: true)));
    }
}
