using Puffinote.Components.Core.Documents;
using Puffinote.Components.Overlays;

namespace Pivotask.Desktop.Views;

/// <summary>
/// External-change detection: something outside Pivotask wrote to the open board. This matters more here
/// than for the other editors and proposal 0004 §Phase 4 says why — a board file is small, hand-editable
/// JSON, and the single most likely way it changes is arriving with a <c>git pull</c>.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The file as Pivotask last left it — the baseline somebody else's write is noticed against.</summary>
    private FileStamp? _diskStamp;

    /// <summary>
    /// Somebody else wrote the board while there were unsaved edits here, and whose version wins has not
    /// been decided. Saving asks first; auto-save stands down until it is settled.
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
    /// Take in a filesystem event: nothing, a silent reload, or record the conflict. Returns true when a
    /// conflict was recorded. As in the sibling apps, recording it is all that happens at this moment —
    /// the question is asked at the save that would overwrite, not thrown up mid-edit.
    /// </summary>
    private bool NoteExternalChange()
    {
        // Once a conflict is recorded, a writer that touches the file repeatedly must not record it again:
        // one answer covers everything that has landed since.
        if (_view.Document.Path is not { } path || _conflictPromptOpen || DiskChangedUnderEdits)
        {
            return false;
        }

        if (FileStamp.For(path) is not { } stamp || stamp == _diskStamp)
        {
            return false; // our own atomic save, or a touch that changed nothing
        }

        if (!_view.Document.IsDirty)
        {
            OpenPath(path, reloading: true); // nothing of the user's to lose — show the board that is actually in the file
            return false;
        }

        DiskChangedUnderEdits = true;
        UpdateChrome();
        return true;
    }

    private void HandleExternalChange() => NoteExternalChange();

    private enum ConflictChoice
    {
        Overwrite,
        Reload,
        Cancel,
    }

    /// <summary>
    /// Ask before a save writes over somebody else's version, and answer whether it should go ahead. Both
    /// answers lose something, which is why the choice is the user's: Overwrite loses the change on disk,
    /// Reload loses the edits here. Cancel is the default and the dismiss value — the only one that
    /// discards nothing.
    /// </summary>
    private async Task<bool> ResolveConflictAsync()
    {
        if (!DiskChangedUnderEdits || _view.Document.Path is not { } path)
        {
            return true;
        }

        _conflictPromptOpen = true;
        try
        {
            var choice = await _overlay.ShowModalAsync(new ChoiceDialog<ConflictChoice>(
                heading: "Changed on disk",
                message: $"“{Path.GetFileName(path)}” was changed by something else — most likely a pull — " +
                         "while you were editing it here.\n\nSaving now replaces that change with yours.",
                dismiss: ConflictChoice.Cancel,
                new Choice<ConflictChoice>("Overwrite", ConflictChoice.Overwrite),
                new Choice<ConflictChoice>("Reload and Lose My Edits", ConflictChoice.Reload),
                new Choice<ConflictChoice>("Cancel", ConflictChoice.Cancel, IsDefault: true, IsCancel: true)));

            switch (choice)
            {
                case ConflictChoice.Reload:
                    DiskChangedUnderEdits = false;
                    _conflictPromptOpen = false; // OpenPath may raise a dialog of its own
                    OpenPath(path, reloading: true);
                    return false; // nothing left to save: the board is the file
                case ConflictChoice.Overwrite:
                    // The version on disk becomes the baseline we are knowingly writing over.
                    _diskStamp = FileStamp.For(path);
                    DiskChangedUnderEdits = false;
                    UpdateChrome();
                    return true;
                default:
                    return false;
            }
        }
        finally
        {
            _conflictPromptOpen = false;
        }
    }

    private bool _conflictPromptOpen;

    // ---- Test seams ------------------------------------------------------

    /// <summary>Test seam: deliver a filesystem change, as the watcher would; true when it is a conflict.</summary>
    internal bool NoteExternalChangeForTest() => NoteExternalChange();

    /// <summary>Test seam: whether an unresolved external write stands between the board and the file.</summary>
    internal bool DiskChangedUnderEditsForTest => DiskChangedUnderEdits;

    /// <summary>Test seam: the path the watcher is following (null for a board that was never saved).</summary>
    internal string? WatchedPathForTest => _watcher.Path;
}
