using Puffinote.Components.Core.Documents;
using Puffinote.Components.Overlays;

namespace Cellestra.Desktop.Views;

/// <summary>
/// External-change detection: something outside Cellestra wrote to the open workbook. The document lock
/// (`.~lock.<name>#`) already warns about another <em>editor</em> holding the file, but a lock is advisory
/// and most of what rewrites an `.ods` never takes one — a `git pull`, a sync client, a script. Without
/// this, the next save wrote the workbook as it was when it was opened over whatever had appeared.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// The file as Cellestra last left it. A stamp and not the bytes: an `.ods` is a zip, so "did this
    /// change?" answered by re-reading means deserializing a whole workbook to find out.
    /// </summary>
    private FileStamp? _diskStamp;

    /// <summary>
    /// Somebody else wrote the open file while there were unsaved edits here, and whose version wins has
    /// not been decided. Saving asks first; auto-save stands down until it is settled.
    /// </summary>
    private bool DiskChangedUnderEdits { get; set; }

    /// <summary>Adopt <paramref name="path"/> as the file being watched, and its stamp as the baseline.</summary>
    private void TrackOnDisk(string? path)
    {
        _diskStamp = FileStamp.For(path);
        DiskChangedUnderEdits = false;
        _watcher.Watch(path);
    }

    /// <summary>
    /// Take the file's current stamp as the baseline, after a write of our own. Without this the watcher
    /// reports Cellestra's save back to Cellestra as somebody else's change, and a clean workbook gets
    /// reloaded — a full deserialize and recalc — every time it is saved.
    /// </summary>
    private void RestampFromDisk(string path)
    {
        _diskStamp = FileStamp.For(path);
        DiskChangedUnderEdits = false;
    }

    /// <summary>
    /// Take in a filesystem event: nothing, a silent reload, or record the conflict. Returns true when a
    /// conflict was recorded. No dialog appears here — a workbook is opened and recalculated off the UI
    /// thread and a modal thrown up mid-edit because a sync client touched the file would interrupt
    /// typing to report something the save is about to ask about anyway.
    /// </summary>
    private bool NoteExternalChange()
    {
        // A file operation of our own in flight is writing (or reading) this very file; and once a
        // conflict is recorded, a writer that touches the file repeatedly must not record it again.
        if (Editor.Session.FilePath is not { } path || _fileBusy || DiskChangedUnderEdits)
        {
            return false;
        }

        if (FileStamp.For(path) is not { } stamp || stamp == _diskStamp)
        {
            return false; // our own atomic save, or a touch that changed nothing
        }

        if (!Editor.Session.IsModified)
        {
            _ = ReloadFromDiskAsync(path); // nothing of the user's to lose — show what is in the file
            return false;
        }

        DiskChangedUnderEdits = true;
        Editor.Narrate($"{Path.GetFileName(path)} changed on disk — Save will ask before overwriting it.");
        return true;
    }

    private void HandleExternalChange() => NoteExternalChange();

    /// <summary>Re-open the workbook through the ordinary open path, so the load, the lock, and the
    /// baseline are all established the one way they are established anywhere else.</summary>
    private async Task ReloadFromDiskAsync(string path)
    {
        await OpenPathAsync(path, reloading: true);
        Editor.Narrate($"{Path.GetFileName(path)} was changed on disk and has been reloaded.");
    }

    /// <summary>
    /// Ask before a save writes over somebody else's version, and answer whether it should go ahead. Both
    /// outcomes lose something, which is why the choice is the user's: Overwrite loses the change on disk,
    /// Reload loses the edits here. Cancel is the default and the dismiss value — the one answer that
    /// discards nothing.
    /// </summary>
    private async Task<bool> ResolveConflictAsync()
    {
        if (!DiskChangedUnderEdits || Editor.Session.FilePath is not { } path)
        {
            return true;
        }

        var choice = await Editor.ShowModalAsync(new ChoiceDialog<ConflictChoice>(
            "Changed on disk",
            $"“{Path.GetFileName(path)}” was changed by something else while you were editing it.\n\n" +
            "Saving now replaces that change with yours.",
            dismiss: ConflictChoice.Cancel,
            new Choice<ConflictChoice>("Overwrite", ConflictChoice.Overwrite),
            new Choice<ConflictChoice>("Reload and Lose My Edits", ConflictChoice.Reload),
            new Choice<ConflictChoice>("Cancel", ConflictChoice.Cancel, IsDefault: true, IsCancel: true)));

        switch (choice)
        {
            case ConflictChoice.Reload:
                DiskChangedUnderEdits = false;
                await ReloadFromDiskAsync(path);
                return false;
            case ConflictChoice.Overwrite:
                // The version on disk becomes the baseline we are knowingly writing over.
                _diskStamp = FileStamp.For(path);
                DiskChangedUnderEdits = false;
                return true;
            default:
                return false;
        }
    }

    /// <summary>How the user resolved a file that changed on disk under unsaved edits.</summary>
    private enum ConflictChoice
    {
        Overwrite,
        Reload,
        Cancel,
    }

    // ---- Test seams ------------------------------------------------------

    /// <summary>Test seam: deliver a filesystem change, as the watcher would; true when it is a conflict.</summary>
    internal bool NoteExternalChangeForTest() => NoteExternalChange();

    /// <summary>Test seam: whether an unresolved external write stands between the workbook and the file.</summary>
    internal bool DiskChangedUnderEditsForTest => DiskChangedUnderEdits;

    /// <summary>Test seam: the path the watcher is following (null when the workbook has never been saved).</summary>
    internal string? WatchedPathForTest => _watcher.Path;
}
