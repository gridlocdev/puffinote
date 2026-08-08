using Avalonia.Input;
using Pivotask.Control.Views;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Pivotask.Core.Query;
using Puffinote.Components.Commands;

namespace Pivotask.Control.Commands;

/// <summary>
/// Pivotask's one command catalog: every operation the embeddable <see cref="TaskBoardView"/> offers,
/// published as shared <see cref="AppCommand"/>s. A host composes these into its menu, palette, and key
/// bindings, so the standalone app and the Puffinote pane offer the same commands under the same chords
/// by construction rather than by two lists happening to agree.
///
/// <para>What stays with a host is only what needs a host: opening and saving <i>files</i>, recent
/// boards, the window. Everything that acts on the board is here.</para>
/// </summary>
public static class TaskEditingCommands
{
    /// <summary>Build the catalog bound to <paramref name="view"/>. Call once per view and cache it
    /// (<see cref="TaskBoardView.EditingCommands"/> does).</summary>
    public static IReadOnlyList<AppCommand> Build(TaskBoardView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return
        [
            new("edit.undo", "Undo", "Step backward through your changes.", Shortcut.Cmd(Key.Z),
                () => view.Document.Undo(), () => view.Document.CanUndo, icon: "undo"),
            new("edit.redo", "Redo", "Re-apply a change you undid.", Shortcut.Cmd(Key.Z, KeyModifiers.Shift),
                () => view.Document.Redo(), () => view.Document.CanRedo, icon: "redo"),

            new("task.add", "Add Task", "Add a task to the first group.", Shortcut.Cmd(Key.N, KeyModifiers.Shift),
                () => AddTask(view), () => view.Groups.Count > 0, icon: "plus"),
            new("task.addGroup", "Add Group", "Add a column to the board.", null,
                () => view.Document.Execute(BoardCommands.AddGroup("New group")), icon: "columns-plus"),

            new("view.board", "Board View", "Show the tasks as columns.", null,
                () => view.Layout = BoardLayout.Board,
                isActive: () => view.Layout == BoardLayout.Board, icon: "layout-grid"),
            new("view.list", "List View", "Show the tasks as grouped rows.", null,
                () => view.Layout = BoardLayout.List,
                isActive: () => view.Layout == BoardLayout.List, icon: "list"),

            new("filter.all", "All Tasks", "Clear the filter.", null,
                () => view.Filter = TaskFilter.All,
                isActive: () => view.Filter == TaskFilter.All, icon: "layers"),
            new("filter.starred", "Starred", "Only starred tasks.", null,
                () => view.Filter = TaskFilter.Starred,
                isActive: () => view.Filter == TaskFilter.Starred, icon: "star"),
            new("filter.dueSoon", "Due Soon", "Only tasks due within a few days, or overdue.", null,
                () => view.Filter = TaskFilter.DueSoon,
                isActive: () => view.Filter == TaskFilter.DueSoon, icon: "calendar"),
            new("filter.unfinished", "Unfinished", "Only tasks that aren't done.", null,
                () => view.Filter = TaskFilter.Unfinished,
                isActive: () => view.Filter == TaskFilter.Unfinished, icon: "square-check"),

            new("edit.find", "Search…", "Search titles, notes, and steps.", Shortcut.Cmd(Key.F),
                () => SearchRequested?.Invoke(view), icon: "search"),

            .. ZoomCommands.Build(() => view.Zoom, zoom => view.Zoom = zoom),
        ];
    }

    /// <summary>
    /// Raised by "Search…" so a host can put the caret in whichever search box it is showing. The
    /// command belongs to the editor (it acts on what the board displays); only the focus is the host's.
    /// </summary>
    public static event Action<TaskBoardView>? SearchRequested;

    private static void AddTask(TaskBoardView view)
    {
        if (view.Groups.Count == 0)
        {
            return;
        }

        var task = new TaskItem { Id = Ids.New(), Title = "New task" };
        view.Document.Execute(BoardCommands.AddTask(view.Groups[0], task));

        // Open it straight away: a task called "New task" is a placeholder, and the useful next action
        // is always to say what it actually is.
        _ = view.OpenTaskAsync(task);
    }
}
