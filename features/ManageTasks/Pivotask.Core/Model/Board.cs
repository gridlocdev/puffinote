namespace Pivotask.Core.Model;

/// <summary>
/// One board — and the root of one file. There is no wrapping array, no board id, and no in-app board
/// switcher (proposal 0004 §10.9): more boards means more files, which a notebook app already handles.
///
/// <para>The model is a mutable object graph rather than an immutable snapshot, unlike the text editors'
/// documents. A board is small and its edits are structural ("move this task there"), so the undo story
/// is a stack of commands that know how to reverse themselves — the shape Cellestra uses — rather than
/// a piece table's cheap snapshots.</para>
/// </summary>
public sealed class Board
{
    /// <summary>The schema version this file was written at. A major version ahead of ours is rejected
    /// rather than mangled — see <c>BoardCodec</c>.</summary>
    public const int CurrentVersion = 1;

    public List<TaskGroup> Groups { get; init; } = [];

    /// <summary>
    /// The board's label vocabulary. In the document, not a constant: the prototype hard-coded five, and
    /// a fixed palette does not survive contact with anyone's actual work (proposal 0004 §4).
    /// </summary>
    public List<Label> Labels { get; init; } = [];

    /// <summary>
    /// Properties a newer Pivotask wrote that this one doesn't model, carried through untouched so an
    /// older build opening and saving a file never silently deletes what it didn't understand.
    /// </summary>
    public Dictionary<string, System.Text.Json.JsonElement> Unknown { get; init; } = [];

    public TaskGroup? FindGroup(string id) => Groups.Find(g => g.Id == id);

    /// <summary>The group containing <paramref name="taskId"/>, and the task, or null when nothing has that id.</summary>
    public (TaskGroup Group, TaskItem Task)? Locate(string taskId)
    {
        foreach (var group in Groups)
        {
            if (group.Tasks.Find(t => t.Id == taskId) is { } task)
            {
                return (group, task);
            }
        }

        return null;
    }

    public Label? FindLabel(string id) => Labels.Find(l => l.Id == id);

    /// <summary>Every task on the board, in board order.</summary>
    public IEnumerable<(TaskGroup Group, TaskItem Task)> AllTasks() =>
        Groups.SelectMany(group => group.Tasks.Select(task => (group, task)));
}

/// <summary>A column on the board, and the unit the outline and the CLI's <c>groups</c> verb both speak.</summary>
public sealed class TaskGroup
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Whether the column is folded shut. A view state, but a persisted one — it is how a board
    /// with twelve columns stays usable, so losing it on reopen would lose the arrangement.</summary>
    public bool Collapsed { get; set; }

    /// <summary>Whether completed tasks are hidden in this group.</summary>
    public bool FoldDone { get; set; }

    public List<TaskItem> Tasks { get; init; } = [];

    public Dictionary<string, System.Text.Json.JsonElement> Unknown { get; init; } = [];
}

/// <summary>One task. Notes are plain text, never Markdown (proposal 0004 §10.4).</summary>
public sealed class TaskItem
{
    public required string Id { get; set; }

    public required string Title { get; set; }

    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// A date, not a time: <c>YYYY-MM-DD</c>, with no timezone anywhere near it. "Thursday" is what
    /// people mean by a due date, and the moment a time is stored the question of *whose* Thursday
    /// arrives with it.
    /// </summary>
    public DateOnly? Due { get; set; }

    public bool Important { get; set; }

    public bool Done { get; set; }

    public List<TaskStep> Steps { get; init; } = [];

    /// <summary>Label ids, referencing <see cref="Board.Labels"/>.</summary>
    public List<string> Labels { get; init; } = [];

    public Dictionary<string, System.Text.Json.JsonElement> Unknown { get; init; } = [];

    /// <summary>How far through this task's steps you are, for the card's progress bar.</summary>
    public (int Done, int Total) StepProgress => (Steps.Count(s => s.Done), Steps.Count);
}

/// <summary>One checklist item inside a task. Exactly one level deep — subtasks do not nest (§8).</summary>
public sealed class TaskStep
{
    public required string Id { get; set; }

    public required string Text { get; set; }

    public bool Done { get; set; }

    public Dictionary<string, System.Text.Json.JsonElement> Unknown { get; init; } = [];
}

/// <summary>A label in the board's vocabulary.</summary>
public sealed class Label
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    /// <summary>An <c>#RRGGBB</c> colour.</summary>
    public required string Color { get; set; }

    public Dictionary<string, System.Text.Json.JsonElement> Unknown { get; init; } = [];
}
