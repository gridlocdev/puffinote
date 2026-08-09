using Puffinote.Components.Core.Documents;
using Puffinote.Components.Overlays;

namespace Plaintain.Desktop.Views;

/// <summary>
/// External-change detection: what happens when something outside Plaintain writes to the open file — a
/// <c>git pull</c>, another editor, a log rotating under a tail. A plaintext editor is the one most likely
/// to have a file open that something else is actively writing, and it was the one app in the family that
/// never looked.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// The file as Plaintain last left it. A stamp rather than the text, because the whole reason this
    /// editor exists is files big enough that re-reading one to find out whether it changed is the
    /// expensive part (a 50 MB log, PRD §3).
    /// </summary>
    private FileStamp? _diskStamp;

    private bool _conflictPromptOpen;

    /// <summary>
    /// Somebody else wrote the file while there were unsaved edits here, and the question of whose version
    /// wins has not been answered yet. Saving asks before it writes, and auto-save stands down entirely —
    /// resolving this is a choice between two losses and not one to make on a timer.
    /// </summary>
    private bool DiskChangedUnderEdits { get; set; }

    /// <summary>Adopt <paramref name="path"/> as the file being watched, and take its stamp as the baseline.</summary>
    private void TrackOnDisk(string? path)
    {
        _diskStamp = FileStamp.For(path);
        DiskChangedUnderEdits = false;
        _watcher.Watch(path);
    }

    /// <summary>
    /// Take in a filesystem event: nothing, a silent reload, or record the conflict. Returns true when a
    /// conflict was recorded.
    /// <para>
    /// Notice that recording it is <em>all</em> that happens — no modal appears the instant a file moves.
    /// Plaintain is the app most likely to have something else writing to its open file (a log being
    /// appended to, a config a tool regenerates), and a dialog that interrupts typing because a background
    /// process touched a file on disk is worse than the problem it reports. The status bar says so, and
    /// the question is asked at the one moment it has to be answered: the save that would overwrite.
    /// </para>
    /// </summary>
    private bool NoteExternalChange()
    {
        // Already asked, or already asking. A writer that touches the file repeatedly — a log being
        // appended to, a formatter that writes twice — must not stack a prompt per event: the conflict is
        // recorded, and the answer to it covers everything that has landed since.
        if (CurrentPath is not { } path || _conflictPromptOpen || DiskChangedUnderEdits)
        {
            return false;
        }

        // Our own atomic save moved the stamp when it wrote; anything that still matches it is a touch,
        // a permissions change, or the second half of a write we already picked up.
        if (FileStamp.For(path) is not { } stamp || stamp == _diskStamp)
        {
            return false;
        }

        if (!EditorView.IsDirty)
        {
            OpenPath(path); // nothing of the user's to lose — show what is actually in the file
            return false;
        }

        DiskChangedUnderEdits = true;
        UpdateChrome();
        return true;
    }

    private void HandleExternalChange() => NoteExternalChange();

    private enum ConflictChoice
    {
        Reload,
        KeepMine,
        SaveAs,
    }

    /// <summary>
    /// The three-way choice, in the words of what each one costs: reload and lose the edits here, keep them
    /// and overwrite the file, or fork the two versions apart with Save As. Esc and the backdrop resolve to
    /// Keep Mine, because a stray keystroke must never be the thing that discards someone's typing.
    /// Returns true when the caller may go on to write.
    /// </summary>
    private async Task<bool> ResolveConflictAsync()
    {
        if (!DiskChangedUnderEdits)
        {
            return true;
        }

        _conflictPromptOpen = true;
        try
        {
            string name = CurrentPath is null ? "This file" : Path.GetFileName(CurrentPath);
            var choice = await _overlay.ShowModalAsync(new ChoiceDialog<ConflictChoice>(
                heading: "Changed on disk",
                message: $"{name} was changed by something else while you were editing it here.\n\n" +
                         "Reload to take the version on disk, keep your changes (they overwrite the file when you " +
                         "save), or save a separate copy.",
                dismiss: ConflictChoice.KeepMine,
                new Choice<ConflictChoice>("Reload", ConflictChoice.Reload),
                new Choice<ConflictChoice>("Save As…", ConflictChoice.SaveAs),
                new Choice<ConflictChoice>("Keep Mine", ConflictChoice.KeepMine, IsDefault: true)));

            switch (choice)
            {
                case ConflictChoice.Reload when CurrentPath is { } path:
                    _conflictPromptOpen = false; // OpenPath may raise a dialog of its own
                    OpenPath(path);
                    return false; // there is nothing left to save: the buffer is the file
                case ConflictChoice.SaveAs:
                    DiskChangedUnderEdits = false;
                    _conflictPromptOpen = false;
                    await SaveAsAsync();
                    return false; // written elsewhere; the original is left as the other tool wrote it
                default:
                    // Keep Mine: the version on disk becomes the baseline we are knowingly overwriting.
                    _diskStamp = FileStamp.For(CurrentPath);
                    DiskChangedUnderEdits = false;
                    UpdateChrome();
                    return true;
            }
        }
        finally
        {
            _conflictPromptOpen = false;
        }
    }

    // ---- Test seams ------------------------------------------------------

    /// <summary>
    /// Test seam: deliver a filesystem change, as the watcher would, and answer whether it wants the
    /// conflict prompt — which a headless suite stops short of, having nobody to answer it.
    /// </summary>
    internal bool NoteExternalChangeForTest() => NoteExternalChange();

    /// <summary>Test seam: whether an unresolved external write is standing between the buffer and the file.</summary>
    internal bool DiskChangedUnderEditsForTest => DiskChangedUnderEdits;

    /// <summary>Test seam: the path the watcher is following (null for an untitled buffer).</summary>
    internal string? WatchedPathForTest => _watcher.Path;
}
