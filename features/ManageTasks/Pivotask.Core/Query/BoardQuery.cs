using Pivotask.Core.Model;

namespace Pivotask.Core.Query;

/// <summary>The slices a board can be read in. One set, shared by the board, the list, and the CLI.</summary>
public enum TaskFilter
{
    All,

    /// <summary>Starred.</summary>
    Starred,

    /// <summary>Due today or tomorrow, or already late.</summary>
    DueTomorrow,

    /// <summary>Due on or before the last day of this calendar week, or already late.</summary>
    DueThisWeek,

    /// <summary>Not yet done.</summary>
    Unfinished,
}

/// <summary>
/// Filtering and searching, as pure functions over a board.
///
/// <para>In Core, not in the control, for the reason the CLI exists: <c>ptask list --due-tomorrow</c>
/// and the board's "Due tomorrow" slice have to agree about which tasks that is, and the only way to
/// guarantee it is for there to be one answer. Phase 5's list view consumes the same seam Phase 2's
/// board does, which is why it was an addition rather than a rework.</para>
/// </summary>
public static class BoardQuery
{
    /// <summary>
    /// How many days ahead <see cref="TaskFilter.DueTomorrow"/> reaches: <b>one</b>. This is the
    /// prototype's window (<c>dayDiff(t.due) &lt;= 1</c>), and the number matters: a filter that answers
    /// "what do I have to deal with now" is only useful while its answer is short enough to act on.
    /// </summary>
    public static readonly int DueTomorrowWindow = 1;

    /// <summary>
    /// The last day of the calendar week containing <paramref name="today"/> — where
    /// <see cref="TaskFilter.DueThisWeek"/> stops.
    ///
    /// <para>A calendar week, not a rolling seven days, because that is what the name says: a filter
    /// called "this week" that on a Friday offered you something due next Wednesday would be the sort of
    /// vagueness this pair of filters exists to end. The week starts where the <em>reader's</em> culture
    /// starts it (Monday in most of the world, Sunday in the US), because "this week" is a fact about
    /// their calendar and not about ours.</para>
    ///
    /// <para>It does shrink as the week goes on, and that is honest: by Saturday there genuinely is not
    /// much week left, and the filter that says so is more useful than one that quietly means "the next
    /// seven days" while calling itself something else.</para>
    /// </summary>
    public static DateOnly EndOfWeek(DateOnly today)
    {
        var starts = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        int elapsed = (((int)today.DayOfWeek - (int)starts) + 7) % 7;
        return today.AddDays(6 - elapsed);
    }

    /// <summary>Whether <paramref name="task"/> passes <paramref name="filter"/> on <paramref name="today"/>.</summary>
    public static bool Matches(TaskItem task, TaskFilter filter, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(task);
        return filter switch
        {
            TaskFilter.Starred => task.Important,

            // Both due filters open backwards without limit: an overdue task is the most urgent thing
            // either of them could name, and a task three weeks late must not fall out of the list that
            // exists to tell you about it. They differ only in how far forward they reach.
            TaskFilter.DueTomorrow => DueBy(task, today.AddDays(DueTomorrowWindow)),
            TaskFilter.DueThisWeek => DueBy(task, EndOfWeek(today)),
            TaskFilter.Unfinished => !task.Done,
            _ => true,
        };
    }

    private static bool DueBy(TaskItem task, DateOnly last) =>
        task.Due is { } due && !task.Done && due <= last;

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
