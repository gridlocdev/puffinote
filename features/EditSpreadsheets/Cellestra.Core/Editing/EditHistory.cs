using Cellestra.Core.Model;

namespace Cellestra.Core.Editing;

/// <summary>
/// The undo/redo stacks (PRD §8.5): session-only, capped at 1,000 steps with the oldest
/// dropped, cleared when the document closes, never written to the file. Executing a new
/// command clears the redo stack.
/// </summary>
public sealed class EditHistory
{
    public const int MaxDepth = 1000;

    private readonly List<IEditCommand> _undo = [];
    private readonly List<IEditCommand> _redo = [];
    private readonly Workbook _workbook;

    public EditHistory(Workbook workbook) => _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoLabel => CanUndo ? _undo[^1].Label : null;

    public string? RedoLabel => CanRedo ? _redo[^1].Label : null;

    /// <summary>Apply a command and record it for undo. Clears the redo stack.</summary>
    public EditResult Execute(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = command.Apply(_workbook);
        _undo.Add(command);
        _redo.Clear();
        if (_undo.Count > MaxDepth)
        {
            _undo.RemoveAt(0); // drop the oldest step
        }

        return result;
    }

    public EditResult Undo()
    {
        if (!CanUndo)
        {
            return EditResult.None;
        }

        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        var result = command.Undo(_workbook);
        _redo.Add(command);
        return result;
    }

    public EditResult Redo()
    {
        if (!CanRedo)
        {
            return EditResult.None;
        }

        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        var result = command.Apply(_workbook);
        _undo.Add(command);
        return result;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
