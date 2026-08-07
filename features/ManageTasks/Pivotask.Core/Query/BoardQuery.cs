using Pivotask.Core.Model;

namespace Pivotask.Core.Query;

/// <summary>The four filters the prototype settled on. One set, shared by the board, the list, and the CLI.</summary>
public enum TaskFilter
{
    All,

    /// <summary>Starred.</summary>
    Starred,

    /// <summary>Due today or earlier, or within the next few days — see <see cref="BoardQuery.DueSoonWindow"/>.</summary>
    DueSoon,

    /// <summary>Not yet done.</summary>
    Unfinished,
}

/// <summary>
/// Filtering and searching, as pure functions over a board.
///
/// <para>In Core, not in the control, for the reason the CLI exists: <c>ptask list --due-soon</c> and
/// the board's "Due soon" tab have to agree about what "soon" means, and the only way to guarantee that
/// is for there to be one answer. Phase 5's list view consumes the same seam Phase 2's board does, which
/// is why it was an addition rather than a rework.</para>
/// </summary>
public static class BoardQuery
{
    /// <summary>How far ahead "due soon" reaches. Three days: far enough to be a warning, near enough
    /// that it doesn't quietly become "everything".</summary>
    public static readonly int DueSoonWindow = 3;

    /// <summary>Whether <paramref name="task"/> passes <paramref name="filter"/> on <paramref name="today"/>.</summary>
    public static bool Matches(TaskItem task, TaskFilter filter, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(task);
        return filter switch
        {
            TaskFilter.Starred => task.Important,

            // An overdue task is the most "due soon" thing there is, so the window opens backwards
            // without limit — a task three weeks late should not fall out of the list that exists to
            // tell you about it.
            TaskFilter.DueSoon => task.Due is { } due && !task.Done && due <= today.AddDays(DueSoonWindow),
            TaskFilter.Unfinished => !task.Done,
            _ => true,
        };
    }

    /// <summary>
    /// Whether <paramref name="task"/> matches <paramref name="query"/>. Searches the title, the notes,
    /// <b>and the step text</b> — a checklist item is where the specific words usually are, and a search
    /// that couldn't find them would send you opening tasks one at a time to look.
    /// </summary>
    public static bool Matches(TaskItem task, string? query)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return Contains(task.Title, query)
            || Contains(task.Notes, query)
            || task.Steps.Any(step => Contains(step.Text, query));
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every task that passes both the filter and the query, in board order, with its group.</summary>
    public static IEnumerable<(TaskGroup Group, TaskItem Task)> Search(
        Board board, TaskFilter filter, string? query, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(board);
        return board.AllTasks().Where(pair => Matches(pair.Task, filter, today) && Matches(pair.Task, query));
    }

    /// <summary>
    /// A due date as a person would say it: "Today", "Tomorrow", "2 days late", or a short date.
    /// Relative only inside the window where relative is clearer — past a week, "Thu, 30 Jul" is what
    /// you actually want, because "in 23 days" is a number you have to do arithmetic on.
    /// </summary>
    public static string DescribeDue(DateOnly due, DateOnly today)
    {
        int days = due.DayNumber - today.DayNumber;
        return days switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "1 day late",
            < -1 => $"{-days} days late",
            <= 6 => $"In {days} days",
            _ => due.ToString("ddd, d MMM", System.Globalization.CultureInfo.CurrentCulture),
        };
    }
}
