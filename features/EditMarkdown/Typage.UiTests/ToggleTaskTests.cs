using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// ⌘L / Ctrl+L (<see cref="MarkdownEditor.ToggleTask"/>): one shortcut that adds a task checkbox to the
/// caret's line or toggles an existing one — plain line → task item, list item → task item, task → flipped.
/// </summary>
public class ToggleTaskTests
{
    private static MarkdownEditor At(string text, int line = 0, int col = 0)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(e.Buffer.Document.GetLineStart(line) + col);
        return e;
    }

    [AvaloniaFact]
    public void OnAPlainLine_AddsAnUncheckedTaskItem()
    {
        var e = At("buy milk", col: 3);
        e.ToggleTask();
        Assert.Equal("- [ ] buy milk", e.Text);
    }

    [AvaloniaFact]
    public void OnAnEmptyLine_AddsAnEmptyTaskItem()
    {
        var e = At("");
        e.ToggleTask();
        Assert.Equal("- [ ] ", e.Text);
    }

    [AvaloniaFact]
    public void OnAListItem_TurnsItIntoATask()
    {
        var e = At("- buy milk", col: 4);
        e.ToggleTask();
        Assert.Equal("- [ ] buy milk", e.Text);
    }

    [AvaloniaFact]
    public void OnAnExistingUncheckedTask_ChecksIt()
    {
        var e = At("- [ ] task", col: 8);
        e.ToggleTask();
        Assert.Equal("- [x] task", e.Text);
    }

    [AvaloniaFact]
    public void OnAnExistingCheckedTask_UnchecksIt()
    {
        var e = At("- [x] task", col: 8);
        e.ToggleTask();
        Assert.Equal("- [ ] task", e.Text);
    }

    [AvaloniaFact]
    public void TogglingATask_LeavesTheCaretPut()
    {
        var e = At("- [ ] task", col: 8); // caret inside "task"
        var before = e.Buffer.Selection.Caret;
        e.ToggleTask();
        Assert.Equal(before, e.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void PreservesIndentationWhenAddingToANestedLine()
    {
        var e = At("  - buy milk", line: 0, col: 6); // a nested bullet
        e.ToggleTask();
        Assert.Equal("  - [ ] buy milk", e.Text);
    }

    [AvaloniaFact]
    public void IsASingleUndo()
    {
        var e = At("buy milk", col: 0);
        e.ToggleTask();
        e.Undo();
        Assert.Equal("buy milk", e.Text);
    }

    [AvaloniaFact]
    public void CompletedTasks_CanMoveToBottom()
    {
        var e = At("- [ ] a\n- [ ] b\n- [ ] c", line: 0, col: 6);
        e.CompletedTaskSorting = MarkdownEditor.CompletedTaskSortMode.Bottom;

        e.ToggleTask();

        Assert.Equal("- [ ] b\n- [ ] c\n- [x] a", e.Text);
        Assert.Equal(2, e.Buffer.Document.GetPosition(e.Buffer.Selection.Caret).Line);
    }

    [AvaloniaFact]
    public void CompletedTasks_CanMoveToTop()
    {
        var e = At("- [ ] a\n- [ ] b\n- [x] c", line: 1, col: 6);
        e.CompletedTaskSorting = MarkdownEditor.CompletedTaskSortMode.Top;

        e.ToggleTask();

        Assert.Equal("- [x] b\n- [x] c\n- [ ] a", e.Text);
        Assert.Equal(0, e.Buffer.Document.GetPosition(e.Buffer.Selection.Caret).Line);
    }

    [AvaloniaFact]
    public void NestedIncompleteTasks_BlockParentFromMoving()
    {
        var e = At("- [ ] a\n  - [ ] child\n- [ ] b", line: 0, col: 6);
        e.CompletedTaskSorting = MarkdownEditor.CompletedTaskSortMode.Bottom;

        e.ToggleTask();

        Assert.Equal("- [x] a\n  - [ ] child\n- [ ] b", e.Text);
    }

    [AvaloniaFact]
    public void CompletedNestedSubtree_MovesAsAUnit()
    {
        var e = At("- [ ] a\n  - [x] child\n- [ ] b", line: 0, col: 6);
        e.CompletedTaskSorting = MarkdownEditor.CompletedTaskSortMode.Bottom;

        e.ToggleTask();

        Assert.Equal("- [ ] b\n- [x] a\n  - [x] child", e.Text);
    }
}
