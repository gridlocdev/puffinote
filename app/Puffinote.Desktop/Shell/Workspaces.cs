namespace Puffinote.Desktop.Shell;

/// <summary>
/// The four things Puffinote is for, as the rail down the left edge of the window offers them (PRD §7.1).
/// Only <see cref="Notes"/> is built; the other three are named here rather than in the rail so that the
/// order, the words, and the ids have one home, and so that adding the real thing later is a change of
/// content, not of vocabulary.
/// </summary>
internal enum Workspace
{
    /// <summary>Every <c>.pivotask</c> board in the notebook, gathered. Not built.</summary>
    Todos,

    /// <summary>A calendar over the notebook's dated work. Not built.</summary>
    Calendar,

    /// <summary>The notebook: the file tree, the tabs, the editors. The one that exists.</summary>
    Notes,

    /// <summary>Every <c>.ods</c> workbook in the notebook, gathered. Not built.</summary>
    Data,
}

/// <summary>What the rail draws for one workspace, and what a placeholder says about it.</summary>
/// <param name="Id">The stable id — a config key and a command id, so it follows the shared-vocabulary rule.</param>
/// <param name="Title">The word on the rail, and the heading of a placeholder.</param>
/// <param name="Icon">Its Lucide glyph.</param>
/// <param name="Promise">What will live here, said in one sentence. Empty for a workspace that is built.</param>
internal readonly record struct WorkspaceInfo(Workspace Id, string Name, string Title, string Icon, string Promise);

internal static class Workspaces
{
    /// <summary>
    /// Top to bottom, in the order the rail shows them: the two lists of documents that are <em>about</em>
    /// time or tasks bracket the notebook itself, with Notes third because that is where the mockup put it
    /// and because a rail whose one working destination is at the top would read as a list with three dead
    /// entries under it.
    /// </summary>
    public static readonly IReadOnlyList<WorkspaceInfo> All =
    [
        new(Workspace.Todos, "todos", "Todos", "square-check",
            "Every task board in this notebook, in one list — the .pivotask files, found however deep they are filed."),
        new(Workspace.Calendar, "calendar", "Calendar", "calendar",
            "The notebook's dated work on a calendar: what is due, and when."),
        new(Workspace.Notes, "notes", "Notes", "notebook-pen", string.Empty),
        new(Workspace.Data, "data", "Data", "table-2",
            "Every spreadsheet in this notebook, in one list — the .ods files, found however deep they are filed."),
    ];

    /// <summary>The rail's entry for <paramref name="workspace"/>.</summary>
    public static WorkspaceInfo Info(Workspace workspace) => All.First(w => w.Id == workspace);

    /// <summary>The command id that switches to <paramref name="workspace"/> (Command Menu, menu bar, shortcuts).</summary>
    public static string CommandId(Workspace workspace) => "view.workspace." + Info(workspace).Name;
}
