using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Xunit;

namespace Pivotask.Core.Tests;

/// <summary>
/// Every mutation goes through a command, and every command undoes itself. These check the pairs, not
/// just the applications — a command that applies correctly and reverts wrongly is worse than one that
/// doesn't work at all, because you only find out after you've lost something.
/// </summary>
public class BoardCommandTests
{
    private static BoardDocument Open()
    {
        var board = new Board();
        var inbox = new TaskGroup { Id = "g1", Name = "Inbox" };
        var week = new TaskGroup { Id = "g2", Name = "This week" };
        inbox.Tasks.Add(new TaskItem { Id = "t1", Title = "first" });
        inbox.Tasks.Add(new TaskItem { Id = "t2", Title = "second" });
        week.Tasks.Add(new TaskItem { Id = "t3", Title = "third" });
        board.Groups.Add(inbox);
        board.Groups.Add(week);
        return new BoardDocument(board);
    }

    [Fact]
    public void Adding_a_task_and_undoing_leaves_the_board_as_it_was()
    {
        var doc = Open();
        var inbox = doc.Board.Groups[0];

        doc.Execute(BoardCommands.AddTask(inbox, new TaskItem { Id = "new", Title = "third" }));
        Assert.Equal(3, inbox.Tasks.Count);

        doc.Undo();
        Assert.Equal(2, inbox.Tasks.Count);
        Assert.DoesNotContain(inbox.Tasks, t => t.Id == "new");
    }

    [Fact]
    public void Deleting_a_task_puts_it_back_where_it_was_not_at_the_end()
    {
        var doc = Open();
        var inbox = doc.Board.Groups[0];

        doc.Execute(BoardCommands.DeleteTask(inbox, inbox.Tasks[0]));
        doc.Undo();

        Assert.Equal("t1", inbox.Tasks[0].Id); // position restored, not appended
    }

    [Fact]
    public void Moving_a_task_across_groups_is_one_undo_not_two()
    {
        // A person who dropped a card in the wrong column presses undo once.
        var doc = Open();
        var (inbox, week) = (doc.Board.Groups[0], doc.Board.Groups[1]);
        var task = inbox.Tasks[1];

        doc.Execute(BoardCommands.MoveTask(doc.Board, task, week));
        Assert.Contains(task, week.Tasks);
        Assert.DoesNotContain(task, inbox.Tasks);

        doc.Undo();
        Assert.Equal(1, inbox.Tasks.IndexOf(task)); // back in its old group *and* its old slot
        Assert.DoesNotContain(task, week.Tasks);
    }

    [Fact]
    public void Moving_before_a_task_puts_it_in_front_of_that_one()
    {
        var doc = Open();
        var (inbox, week) = (doc.Board.Groups[0], doc.Board.Groups[1]);

        doc.Execute(BoardCommands.MoveTask(doc.Board, inbox.Tasks[0], week, before: week.Tasks[0]));

        Assert.Equal("t1", week.Tasks[0].Id);
        Assert.Equal("t3", week.Tasks[1].Id);
    }

    [Fact]
    public void Reordering_inside_a_group_works_through_the_same_command()
    {
        var doc = Open();
        var inbox = doc.Board.Groups[0];

        doc.Execute(BoardCommands.MoveTask(doc.Board, inbox.Tasks[1], inbox, before: inbox.Tasks[0]));

        Assert.Equal(["t2", "t1"], inbox.Tasks.Select(t => t.Id));
    }

    [Fact]
    public void Clearing_a_checklist_is_one_step_to_undo()
    {
        // Per-step deletion would need N undos to put back one mistake.
        var doc = Open();
        var task = doc.Board.Groups[0].Tasks[0];
        doc.Execute(BoardCommands.AddStep(task, "one"));
        doc.Execute(BoardCommands.AddStep(task, "two"));

        doc.Execute(BoardCommands.ClearSteps(task));
        Assert.Empty(task.Steps);

        doc.Undo();
        Assert.Equal(["one", "two"], task.Steps.Select(s => s.Text));
    }

    [Fact]
    public void Toggling_a_label_off_and_undoing_puts_it_back()
    {
        var doc = Open();
        var task = doc.Board.Groups[0].Tasks[0];
        doc.Execute(BoardCommands.ToggleLabel(task, "design"));
        Assert.Contains("design", task.Labels);

        doc.Execute(BoardCommands.ToggleLabel(task, "design"));
        Assert.DoesNotContain("design", task.Labels);

        doc.Undo();
        Assert.Contains("design", task.Labels);
    }

    [Fact]
    public void Redo_replays_what_undo_reversed()
    {
        var doc = Open();
        var task = doc.Board.Groups[0].Tasks[0];

        doc.Execute(BoardCommands.SetDone(task, true));
        doc.Undo();
        Assert.False(task.Done);

        doc.Redo();
        Assert.True(task.Done);
    }

    [Fact]
    public void A_new_edit_forks_the_timeline_and_drops_the_old_future()
    {
        var doc = Open();
        var task = doc.Board.Groups[0].Tasks[0];

        doc.Execute(BoardCommands.SetDone(task, true));
        doc.Undo();
        doc.Execute(BoardCommands.SetImportant(task, true));

        Assert.False(doc.CanRedo);
    }

    [Fact]
    public void An_edit_and_its_undo_leave_the_document_clean()
    {
        // Dirty is measured against the serialized form, not an edit counter, because "I haven't
        // changed anything" is a claim about the file rather than about how many keys were pressed.
        var doc = Open();
        doc.Serialize(); // establish the saved baseline
        Assert.False(doc.IsDirty);

        doc.Execute(BoardCommands.SetTitle(doc.Board.Groups[0].Tasks[0], "changed"));
        Assert.True(doc.IsDirty);

        doc.Undo();
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void The_undo_label_says_what_undo_would_do()
    {
        var doc = Open();
        doc.Execute(BoardCommands.SetDone(doc.Board.Groups[0].Tasks[0], true));

        Assert.Equal("Complete Task", doc.UndoLabel);
    }

    [Fact]
    public void Deleting_a_group_restores_it_at_its_own_index()
    {
        var doc = Open();
        doc.Execute(BoardCommands.DeleteGroup(doc.Board, doc.Board.Groups[0]));
        Assert.Single(doc.Board.Groups);

        doc.Undo();
        Assert.Equal("Inbox", doc.Board.Groups[0].Name);
    }

    [Fact]
    public void Moving_a_task_that_is_not_on_this_board_is_refused()
    {
        var doc = Open();
        var stranger = new TaskItem { Id = "nope", Title = "x" };

        Assert.Throws<ArgumentException>(
            () => BoardCommands.MoveTask(doc.Board, stranger, doc.Board.Groups[0]));
    }
}
