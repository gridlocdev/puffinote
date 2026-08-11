using Avalonia.Threading;
using Puffinote.Components.Overlays;
using Puffinote.Text.Document;
using Typage.Control;

namespace Typage.Desktop.Views;

public partial class MainWindow
{
    // ----- crash recovery (PRD §9.4) -----

    private void SetupRecoverySnapshots()
    {
        _snapshotTimer?.Stop();
        var seconds = _settings.Settings.RecoverySnapshotSeconds;
        if (seconds <= 0)
        {
            _snapshotTimer = null; // snapshots disabled
            return;
        }

        _snapshotTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _snapshotTimer.Tick += (_, _) => RecoverySnapshotTick();
        _snapshotTimer.Start();
    }

    /// <summary>Snapshot the buffer if it has unsaved changes, else drop any stale snapshot.</summary>
    private void RecoverySnapshotTick()
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

    /// <summary>Run one snapshot cycle; for tests (the timer can't tick headlessly).</summary>
    internal void RecoverySnapshotTickForTest() => RecoverySnapshotTick();

    // ----- document auto-save (write the open file after the typing stops) -----

    /// <summary>
    /// Write the open document back to its own file once editing has paused, if it can be written without
    /// asking anyone anything. Every early return here is a case where a save would have to open a dialog,
    /// and an auto-save that opens dialogs is a worse feature than no auto-save:
    /// <list type="bullet">
    /// <item>an untitled buffer has no path — Save As is a question, and the crash-recovery snapshot is
    /// already covering it;</item>
    /// <item>a file whose encoding was only guessed must not have that guess written back until someone
    /// confirms it (the whole point of <see cref="_encodingUnconfirmed"/>);</item>
    /// <item>a modal is up — including the external-change conflict prompt, where the question on screen is
    /// literally "whose version wins?" and answering it by writing ours would be the wrong answer.</item>
    /// </list>
    /// Staged images stay staged: the manual save offers to move them into an <c>images/</c> folder, and
    /// that offer is a dialog too. The document saves with the links it has, which still open.
    /// </summary>
    private void AutoSaveTick()
    {
        if (_currentPath is null
            || !EditorView.IsDirty
            || _encodingUnconfirmed
            || _conflictPromptOpen
            || _overlayHost.IsOpen)
        {
            return;
        }

        try
        {
            MarkdownDocumentSession.AtomicWrite(_currentPath, TextFileCodec.Encode(EditorView.Text, _format));
            _lastKnownDiskText = EditorView.Text; // so the watcher recognises this as our own save
            EditorView.MarkSaved();
            _recovery.Clear(); // the save is durable now; drop the crash-recovery snapshot
            UpdateChrome();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Stand auto-save down and say so once. The disk that just refused this write will refuse the
            // next one too, and a modal every thirty seconds for the rest of the afternoon is not a report,
            // it's a siege. The document stays dirty, ⌘S still works, and a manual save that succeeds
            // switches auto-save back on.
            _autoSave.Suspended = true;
            ShowError("Auto-save couldn't write the document.",
                $"{ex.Message}\n\nAuto-save is paused until you save successfully yourself (⌘S).");
        }
    }

    /// <summary>Run one auto-save cycle now; for tests (a DispatcherTimer can't tick headlessly).</summary>
    internal Task AutoSaveNowForTest() => _autoSave.RunAsync();

    /// <summary>Whether an auto-save is armed and waiting out the pause (test seam).</summary>
    internal bool AutoSavePendingForTest => _autoSave.IsPending;

    /// <summary>Whether auto-save has stood itself down after a failed write (test seam).</summary>
    internal bool AutoSaveSuspendedForTest => _autoSave.Suspended;

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

    /// <summary>Take the version on disk of the open file, as the watcher's "Reload" does; for tests (a
    /// headless run has no file-system watcher ticking and no prompt to answer).</summary>
    internal void ReloadForTest()
    {
        if (_currentPath is { } path)
        {
            OpenPath(path, reloading: true);
        }
    }

    private void WatchCurrentFile() => _watcher.Watch(_currentPath);

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
                OpenPath(_currentPath, reloading: true); // no local edits — pick up the new version silently
                return;
            case DiskChange.Conflict:
                _conflictPromptOpen = true;
                try
                {
                    switch (await PromptConflictAsync())
                    {
                        case ConflictChoice.Reload:
                            OpenPath(_currentPath, reloading: true);
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
