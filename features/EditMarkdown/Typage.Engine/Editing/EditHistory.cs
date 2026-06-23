using Typage.Engine.Document;

namespace Typage.Engine.Editing;

/// <summary>One recorded step: the edit plus the selection before and after it.</summary>
public readonly record struct HistoryEntry(TextEdit Edit, Selection Before, Selection After);

/// <summary>
/// The undo/redo stacks (PRD §7.2): session-only, capped at <see cref="MaxDepth"/> steps with
/// the oldest dropped, cleared when the document closes or reloads, never written to the file.
/// Recording a new edit clears the redo stack. A run of single-character typing coalesces into
/// one step (bounded by caret jumps and <see cref="BreakCoalescing"/>); structural edits and
/// command operations are discrete.
/// </summary>
public sealed class EditHistory
{
    public const int MaxDepth = 1000;

    private readonly List<HistoryEntry> _undo = new();
    private readonly List<HistoryEntry> _redo = new();
    private bool _coalesceBreak = true;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Force the next recorded edit to start a fresh undo step (e.g. after a caret jump or idle timeout).</summary>
    public void BreakCoalescing() => _coalesceBreak = true;

    /// <summary>Record an edit that has already been applied to the buffer.</summary>
    public void Record(TextEdit edit, Selection before, Selection after)
    {
        _redo.Clear();

        if (!_coalesceBreak && _undo.Count > 0 && _undo[^1].Edit.CanCoalesceWith(edit))
        {
            var top = _undo[^1];
            _undo[^1] = top with { Edit = top.Edit.CoalesceWith(edit), After = after };
        }
        else
        {
            _undo.Add(new HistoryEntry(edit, before, after));
            if (_undo.Count > MaxDepth)
            {
                _undo.RemoveAt(0);
            }
        }

        _coalesceBreak = false;
    }

    /// <summary>Pop the most recent step for undo, or null if there is nothing to undo.</summary>
    public HistoryEntry? PopUndo()
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(entry);
        _coalesceBreak = true;
        return entry;
    }

    /// <summary>Pop the most recently undone step for redo, or null if there is nothing to redo.</summary>
    public HistoryEntry? PopRedo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(entry);
        _coalesceBreak = true;
        return entry;
    }

    /// <summary>Clear both stacks (document close, revert, or external reload — PRD §7.2).</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _coalesceBreak = true;
    }
}
