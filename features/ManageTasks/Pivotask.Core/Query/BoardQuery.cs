using Pivotask.Core.Model;

namespace Pivotask.Core.Query;

/// <summary>The four filters the prototype settled on. One set, shared by the board, the list, and the CLI.</summary>
public enum TaskFilter
{
    All,

    /// <summary>Starred.</summary>
    Starred,

    /// <summary>Due today or tomorrow, or already late — see <see cref="BoardQuery.DueSoonWindow"/>.</summary>
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
    /// <summary>
    /// How many days ahead "due soon" reaches: <b>one</b> — today and tomorrow, plus everything already
    /// late. This is the prototype's window (<c>dayDiff(t.due) &lt;= 1</c>), and the number matters: a
    /// filter that answers "what do I have to deal with now" is only useful while its answer is short
    /// enough to act on. Three days quietly turned it into "this week", which the board's own "This week"
    /// column already is.
    /// </summary>
    public static readonly int DueSoonWindow = 1;

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
    /// A due date as a person would say it: "Today", "Tomorrow", "2 days late", or else a short weekday
    /// date — "Tue, Jul 28".
    /// <para>
    /// Relative wording stops at tomorrow, which is where the prototype stops it. Anything further out is
    /// a date, because "In 4 days" is a sum the reader has to do to find out which day that is, and the
    /// card has room to just say. The three relative forms earn their place by being the ones nobody has
    /// to convert.
    /// </para>
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
            _ => ShortDate(due),
        };
    }

    /// <summary>
    /// A weekday and a day-month, ordered the way the reader's own locale orders them — "Tue, Jul 28" in
    /// the US, "Tue, 28 Jul" where the day comes first. Built from the culture's own month/day pattern
    /// rather than a hardcoded one, because the prototype leaves the ordering to the browser's locale and
    /// a fixed <c>"ddd, d MMM"</c> would be quietly wrong for half its readers.
    /// </summary>
    private static string ShortDate(DateOnly due)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;

        // MonthDayPattern spells the full month ("MMMM d"); the card wants the abbreviation.
        string monthDay = culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal);
        return due.ToString($"ddd, {monthDay}", culture);
    }
}
