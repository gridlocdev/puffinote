using Pivotask.Core.Model;

namespace Pivotask.Core.Editing;

/// <summary>
/// One undoable change to a board. Command-based, and Pivotask's own: proposal 0001 §3 already ruled
/// that Typage's <c>EditHistory</c> is engine-coupled and must not be shared, and a board's edits are
/// structural ("move this task there") rather than textual, so Cellestra's <c>IEditCommand</c> shape is
/// the closer model. Session-only, never written to the file, like the rest of the family.
/// </summary>
public interface IBoardCommand
{
    /// <summary>What the undo menu calls this.</summary>
    string Label { get; }

    void Apply(Board board);

    void Revert(Board board);
}

/// <summary>
/// The undo/redo stack. Session-only and capped, because an unbounded history on a long-lived tab is a
/// leak with a friendly name.
/// </summary>
public sealed class BoardHistory
{
    /// <summary>Matches the family's cap (Cellestra PRD §8.5).</summary>
    public const int MaxDepth = 1000;

    private readonly List<IBoardCommand> _undo = [];
    private readonly List<IBoardCommand> _redo = [];

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>The label of the change undo would reverse, for a menu item that says what it will do.</summary>
    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    public void Execute(Board board, IBoardCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Apply(board);
        _undo.Add(command);
        _redo.Clear(); // a new edit forks the timeline; the old future is gone
        if (_undo.Count > MaxDepth)
        {
            _undo.RemoveAt(0);
        }
    }

    public bool Undo(Board board)
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Revert(board);
        _redo.Add(command);
        return true;
    }

    public bool Redo(Board board)
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Apply(board);
        _undo.Add(command);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

/// <summary>
/// The board's mutations, each as a command that knows how to undo itself.
///
/// <para>Every one is a factory rather than a method that mutates directly, so there is exactly one way
/// to change a board and it is always undoable — the GUI, the CLI, and a test all go through here.</para>
/// </summary>
public static class BoardCommands
{
    // ---- Groups -----------------------------------------------------------

    public static IBoardCommand AddGroup(string name, string? id = null)
    {
        var group = new TaskGroup { Id = id ?? Ids.New(), Name = name };
        return new Action("Add Group",
            board => board.Groups.Add(group),
            board => board.Groups.Remove(group));
    }

    public static IBoardCommand RenameGroup(TaskGroup group, string name)
    {
        ArgumentNullException.ThrowIfNull(group);
        string before = group.Name;
        return new Action("Rename Group", _ => group.Name = name, _ => group.Name = before);
    }

    public static IBoardCommand DeleteGroup(Board board, TaskGroup group)
    {
        ArgumentNullException.ThrowIfNull(board);
        int index = board.Groups.IndexOf(group);
        return new Action("Delete Group",
            b => b.Groups.Remove(group),
            b => b.Groups.Insert(Math.Clamp(index, 0, b.Groups.Count), group));
    }

    public static IBoardCommand SetCollapsed(TaskGroup group, bool collapsed)
    {
        ArgumentNullException.ThrowIfNull(group);
        bool before = group.Collapsed;
        return new Action(collapsed ? "Collapse Group" : "Expand Group",
            _ => group.Collapsed = collapsed, _ => group.Collapsed = before);
    }

    public static IBoardCommand SetFoldDone(TaskGroup group, bool fold)
    {
        ArgumentNullException.ThrowIfNull(group);
        bool before = group.FoldDone;
        return new Action(fold ? "Hide Completed" : "Show Completed",
            _ => group.FoldDone = fold, _ => group.FoldDone = before);
    }

    // ---- Tasks ------------------------------------------------------------

    public static IBoardCommand AddTask(TaskGroup group, TaskItem task, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(task);
        int at = Math.Clamp(index ?? group.Tasks.Count, 0, group.Tasks.Count);
        return new Action("Add Task",
            _ => group.Tasks.Insert(Math.Clamp(at, 0, group.Tasks.Count), task),
            _ => group.Tasks.Remove(task));
    }

    public static IBoardCommand DeleteTask(TaskGroup group, TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(group);
        int index = group.Tasks.IndexOf(task);
        return new Action("Delete Task",
            _ => group.Tasks.Remove(task),
            _ => group.Tasks.Insert(Math.Clamp(index, 0, group.Tasks.Count), task));
    }

    /// <summary>
    /// Tick or untick the task. <b>Completing it ticks every step with it</b> — the prototype does this,
    /// and it is the honest reading: a task you have called finished cannot still have half its checklist
    /// outstanding, and leaving them would show "1/3 steps" on a card with a line through its title.
    /// <para>
    /// Reopening does <em>not</em> untick them, because that would throw away work: the steps were
    /// genuinely done. Undo restores exactly which steps were ticked before, so the cascade is never a
    /// one-way door.
    /// </para>
    /// </summary>
    public static IBoardCommand SetDone(TaskItem task, bool done)
    {
        ArgumentNullException.ThrowIfNull(task);
        bool before = task.Done;
        var stepsBefore = task.Steps.Select(step => step.Done).ToArray();

        return new Action(
            done ? "Complete Task" : "Reopen Task",
            _ =>
            {
                task.Done = done;
                if (done)
                {
                    foreach (var step in task.Steps)
                    {
                        step.Done = true;
                    }
                }
            },
            _ =>
            {
                task.Done = before;
                for (int i = 0; i < task.Steps.Count && i < stepsBefore.Length; i++)
                {
                    task.Steps[i].Done = stepsBefore[i];
                }
            });
    }

    public static IBoardCommand SetImportant(TaskItem task, bool important)
    {
        ArgumentNullException.ThrowIfNull(task);
        bool before = task.Important;
        return new Action(important ? "Star Task" : "Unstar Task",
            _ => task.Important = important, _ => task.Important = before);
    }

    public static IBoardCommand SetTitle(TaskItem task, string title)
    {
        ArgumentNullException.ThrowIfNull(task);
        string before = task.Title;
        return new Action("Rename Task", _ => task.Title = title, _ => task.Title = before);
    }

    public static IBoardCommand SetNotes(TaskItem task, string notes)
    {
        ArgumentNullException.ThrowIfNull(task);
        string before = task.Notes;
        return new Action("Edit Notes", _ => task.Notes = notes, _ => task.Notes = before);
    }

    public static IBoardCommand SetDue(TaskItem task, DateOnly? due)
    {
        ArgumentNullException.ThrowIfNull(task);
        var before = task.Due;
        return new Action(due is null ? "Clear Due Date" : "Set Due Date",
            _ => task.Due = due, _ => task.Due = before);
    }

    /// <summary>
    /// Move a task to <paramref name="target"/>, optionally before <paramref name="before"/>. One
    /// command rather than a delete plus an add, so undo puts it back where it was in one press —
    /// which is what a person who dropped a card in the wrong column expects.
    /// </summary>
    public static IBoardCommand MoveTask(Board board, TaskItem task, TaskGroup target, TaskItem? before = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(target);

        var origin = board.Locate(task?.Id ?? string.Empty)?.Group
            ?? throw new ArgumentException("That task isn't on this board.", nameof(task));
        int originIndex = origin.Tasks.IndexOf(task!);

        return new Action("Move Task",
            _ =>
            {
                origin.Tasks.Remove(task!);
                int at = before is null ? target.Tasks.Count : Math.Max(0, target.Tasks.IndexOf(before));
                target.Tasks.Insert(Math.Clamp(at, 0, target.Tasks.Count), task!);
            },
            _ =>
            {
                target.Tasks.Remove(task!);
                origin.Tasks.Insert(Math.Clamp(originIndex, 0, origin.Tasks.Count), task!);
            });
    }

    // ---- Steps and labels -------------------------------------------------

    public static IBoardCommand AddStep(TaskItem task, string text, string? id = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        var step = new TaskStep { Id = id ?? Ids.New(), Text = text };
        return new Action("Add Step", _ => task.Steps.Add(step), _ => task.Steps.Remove(step));
    }

    /// <summary>Reword a step. A checklist item is written in a hurry and read later, so it has to be
    /// fixable in place — and, like every other edit here, fixable back.</summary>
    public static IBoardCommand SetStepText(TaskStep step, string text)
    {
        ArgumentNullException.ThrowIfNull(step);
        string before = step.Text;
        return new Action("Edit Step", _ => step.Text = text, _ => step.Text = before);
    }

    public static IBoardCommand SetStepDone(TaskStep step, bool done)
    {
        ArgumentNullException.ThrowIfNull(step);
        bool before = step.Done;
        return new Action("Check Step", _ => step.Done = done, _ => step.Done = before);
    }

    public static IBoardCommand DeleteStep(TaskItem task, TaskStep step)
    {
        ArgumentNullException.ThrowIfNull(task);
        int index = task.Steps.IndexOf(step);
        return new Action("Delete Step",
            _ => task.Steps.Remove(step),
            _ => task.Steps.Insert(Math.Clamp(index, 0, task.Steps.Count), step));
    }

    /// <summary>Clear a task's whole checklist in one step, so undo restores all of it (the prototype's
    /// "delete all steps"). A per-step loop would need N undos to put back one mistake.</summary>
    public static IBoardCommand ClearSteps(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var before = task.Steps.ToList();
        return new Action("Clear Steps",
            _ => task.Steps.Clear(),
            _ =>
            {
                task.Steps.Clear();
                task.Steps.AddRange(before);
            });
    }

    public static IBoardCommand ToggleLabel(TaskItem task, string labelId)
    {
        ArgumentNullException.ThrowIfNull(task);
        bool had = task.Labels.Contains(labelId, StringComparer.Ordinal);
        return new Action(had ? "Remove Label" : "Add Label",
            _ =>
            {
                if (had)
                {
                    task.Labels.Remove(labelId);
                }
                else
                {
                    task.Labels.Add(labelId);
                }
            },
            _ =>
            {
                if (had)
                {
                    task.Labels.Add(labelId);
                }
                else
                {
                    task.Labels.Remove(labelId);
                }
            });
    }

    public static IBoardCommand AddLabel(Label label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return new Action("Add Label", board => board.Labels.Add(label), board => board.Labels.Remove(label));
    }

    /// <summary>A command built from a pair of closures. Every factory above is one of these.</summary>
    private sealed class Action(string label, Action<Board> apply, Action<Board> revert) : IBoardCommand
    {
        public string Label => label;

        public void Apply(Board board) => apply(board);

        public void Revert(Board board) => revert(board);
    }
}
