using Pivotask.Core.Model;
using Pivotask.Core.Query;
using Xunit;

namespace Pivotask.Core.Tests;

/// <summary>
/// Filtering and searching. These live in Core so `ptask list --due-soon` and the board's "Due soon"
/// tab cannot disagree about what "soon" means.
/// </summary>
public class BoardQueryTests
{
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
    [InlineData(-14, true)] // three weeks late is the most "due soon" thing there is
    [InlineData(0, true)]   // today
    [InlineData(3, true)]   // the edge of the window
    [InlineData(4, false)]  // past it
    public void Due_soon_reaches_forward_a_few_days_and_backward_without_limit(int days, bool expected) =>
        Assert.Equal(expected, BoardQuery.Matches(Task("x", days), TaskFilter.DueSoon, Today));

    [Fact]
    public void A_completed_task_is_never_due_soon()
    {
        Assert.False(BoardQuery.Matches(Task("x", -1, done: true), TaskFilter.DueSoon, Today));
    }

    [Fact]
    public void A_task_with_no_due_date_is_never_due_soon()
    {
        Assert.False(BoardQuery.Matches(Task("x"), TaskFilter.DueSoon, Today));
    }

    [Fact]
    public void The_other_three_filters_are_what_they_say()
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
    [InlineData(4, "In 4 days")]
    public void A_due_date_reads_the_way_a_person_would_say_it(int days, string expected) =>
        Assert.Equal(expected, BoardQuery.DescribeDue(Today.AddDays(days), Today));

    [Fact]
    public void Past_a_week_a_date_is_a_date_again()
    {
        // "In 23 days" is a number you have to do arithmetic on; a date is not.
        string far = BoardQuery.DescribeDue(Today.AddDays(23), Today);

        Assert.DoesNotContain("days", far, StringComparison.Ordinal);
        Assert.Contains("30", far, StringComparison.Ordinal);
    }
}
