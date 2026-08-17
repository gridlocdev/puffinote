using Pivotask.Core.Model;
using Pivotask.Core.Query;
using Xunit;

namespace Pivotask.Core.Tests;

/// <summary>
/// Filtering and searching. These live in Core so `pivotask list --due-tomorrow` and the board's "Due
/// tomorrow" slice cannot disagree about which tasks that is.
/// </summary>
public class BoardQueryTests
{
    /// <summary>A Friday, so "this week" has a couple of days left in it and an end to test against.</summary>
    private static readonly DateOnly Today = new(2026, 8, 7);

    private static TaskItem Task(string title, int? dueInDays = null, bool done = false, bool important = false)
    {
        var task = new TaskItem { Id = title, Title = title, Done = done, Important = important };
        if (dueInDays is { } days)
        {
            task.Due = Today.AddDays(days);
        }

        return task;
    }

    [Theory]
    [InlineData(-14, true)] // three weeks late is the most urgent thing this filter could name
    [InlineData(0, true)]   // today
    [InlineData(1, true)]   // tomorrow — the edge of the window (the prototype's dayDiff <= 1)
    [InlineData(2, false)]  // past it: that is the other due filter's job now
    public void Due_tomorrow_reaches_to_tomorrow_and_backward_without_limit(int days, bool expected) =>
        Assert.Equal(expected, BoardQuery.Matches(Task("x", days), TaskFilter.DueTomorrow, Today));

    /// <summary>
    /// "This week" is the calendar week, not a rolling seven days — a filter that on a Friday offered
    /// something due next Wednesday would be the vagueness this pair of filters exists to end. Today is a
    /// Friday, so the week ends two days out where the week starts on Monday, and tomorrow where it
    /// starts on Sunday; both are asserted against <see cref="BoardQuery.EndOfWeek"/> rather than against
    /// a number, because which day a week starts on is the reader's culture's business and not ours.
    /// </summary>
    [Fact]
    public void Due_this_week_reaches_to_the_end_of_the_calendar_week()
    {
        int last = BoardQuery.EndOfWeek(Today).DayNumber - Today.DayNumber;

        Assert.True(BoardQuery.Matches(Task("x", -14), TaskFilter.DueThisWeek, Today)); // still overdue-inclusive
        Assert.True(BoardQuery.Matches(Task("x", 0), TaskFilter.DueThisWeek, Today));
        Assert.True(BoardQuery.Matches(Task("x", last), TaskFilter.DueThisWeek, Today));
        Assert.False(BoardQuery.Matches(Task("x", last + 1), TaskFilter.DueThisWeek, Today));

        // …and it is the wider of the two: everything due tomorrow is also due this week.
        Assert.True(last >= BoardQuery.DueTomorrowWindow);
    }

    /// <summary>The week runs seven days and contains today, wherever the reader's culture starts it.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void The_week_ends_within_the_next_seven_days_whatever_day_it_is(int offset)
    {
        var day = Today.AddDays(offset);
        int left = BoardQuery.EndOfWeek(day).DayNumber - day.DayNumber;

        Assert.InRange(left, 0, 6);
        Assert.Equal(BoardQuery.EndOfWeek(day), BoardQuery.EndOfWeek(BoardQuery.EndOfWeek(day)));
    }

    [Theory]
    [InlineData(TaskFilter.DueTomorrow)]
    [InlineData(TaskFilter.DueThisWeek)]
    public void A_completed_task_and_a_dateless_one_are_never_due(TaskFilter filter)
    {
        Assert.False(BoardQuery.Matches(Task("x", -1, done: true), filter, Today));
        Assert.False(BoardQuery.Matches(Task("x"), filter, Today));
    }

    [Fact]
    public void The_other_filters_are_what_they_say()
    {
        Assert.True(BoardQuery.Matches(Task("x", done: true), TaskFilter.All, Today));
        Assert.True(BoardQuery.Matches(Task("x", important: true), TaskFilter.Starred, Today));
        Assert.False(BoardQuery.Matches(Task("x"), TaskFilter.Starred, Today));
        Assert.False(BoardQuery.Matches(Task("x", done: true), TaskFilter.Unfinished, Today));
    }

    [Fact]
    public void Search_looks_inside_the_steps_as_well_as_the_title_and_notes()
    {
        // A checklist item is where the specific words usually are; a search that couldn't reach them
        // would send you opening tasks one at a time to look.
        var task = Task("Pick a typeface");
        task.Notes = "for the docs site";
        task.Steps.Add(new TaskStep { Id = "s", Text = "Test them at 13px" });

        Assert.True(BoardQuery.Matches(task, "typeface"));
        Assert.True(BoardQuery.Matches(task, "docs"));
        Assert.True(BoardQuery.Matches(task, "13px"));
        Assert.False(BoardQuery.Matches(task, "invoice"));
    }

    [Fact]
    public void Search_ignores_case_and_an_empty_query_matches_everything()
    {
        var task = Task("Halden Co.");

        Assert.True(BoardQuery.Matches(task, "halden"));
        Assert.True(BoardQuery.Matches(task, "  "));
        Assert.True(BoardQuery.Matches(task, null));
    }

    [Fact]
    public void Search_returns_matches_in_board_order_with_their_group()
    {
        var board = new Board();
        var a = new TaskGroup { Id = "a", Name = "A" };
        var b = new TaskGroup { Id = "b", Name = "B" };
        a.Tasks.Add(Task("alpha one"));
        b.Tasks.Add(Task("alpha two"));
        b.Tasks.Add(Task("beta"));
        board.Groups.Add(a);
        board.Groups.Add(b);

        var found = BoardQuery.Search(board, TaskFilter.All, "alpha", Today).ToList();

        Assert.Equal(["alpha one", "alpha two"], found.Select(f => f.Task.Title));
        Assert.Equal("A", found[0].Group.Name);
    }

    [Theory]
    [InlineData(0, "Today")]
    [InlineData(1, "Tomorrow")]
    [InlineData(-1, "1 day late")]
    [InlineData(-5, "5 days late")]
    public void A_due_date_reads_the_way_a_person_would_say_it(int days, string expected) =>
        Assert.Equal(expected, BoardQuery.DescribeDue(Today.AddDays(days), Today));

    /// <summary>
    /// Relative wording stops at tomorrow, where the prototype stops it. "In 4 days" is a sum the reader
    /// has to do to find out which day that is, and the card has room to just say.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(23)]
    public void Past_tomorrow_a_date_is_a_date_again(int days)
    {
        var due = Today.AddDays(days);
        string text = BoardQuery.DescribeDue(due, Today);

        Assert.DoesNotContain("days", text, StringComparison.Ordinal);
        Assert.StartsWith(due.ToString("ddd", System.Globalization.CultureInfo.CurrentCulture), text, StringComparison.Ordinal);
        Assert.Contains(due.Day.ToString(System.Globalization.CultureInfo.CurrentCulture), text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The weekday and the day-month, ordered the way the reader's own locale orders them — the browser
    /// behaviour the prototype gets for free from toLocaleDateString.
    /// </summary>
    [Fact]
    public void A_short_date_follows_the_cultures_own_day_month_order()
    {
        var due = new DateOnly(2025, 7, 28);
        var prior = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Assert.Equal("Mon, Jul 28", BoardQuery.DescribeDue(due, due.AddDays(-30)));

            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-GB");
            Assert.Equal("Mon, 28 Jul", BoardQuery.DescribeDue(due, due.AddDays(-30)));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = prior;
        }
    }
}
