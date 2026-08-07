using Avalonia.Headless.XUnit;
using Puffinote.Text.Document;
using Typage.Control.Controls;
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

    // ----- the caret on a hard-wrapped item's continuation line -----
    // The marker-less line flows into the item above, so the reader sees one item and ⌘L must act on it —
    // not start a rival checkbox item mid-way through, which tore the item in two.

    [AvaloniaFact]
    public void OnABulletsContinuationLine_CheckboxesTheOwningItem()
    {
        var e = At("- apple\nwrapped", line: 1, col: 4);
        e.ToggleTask();
        Assert.Equal("- [ ] apple\nwrapped", e.Text); // was "- apple\n- [ ] wrapped"
        Assert.Equal(16, e.Buffer.Selection.Caret);   // caret rides its own text, past the inserted "[ ] "
    }

    [AvaloniaFact]
    public void OnATasksContinuationLine_FlipsTheOwningItemsBox()
    {
        var e = At("- [ ] apple\nwrapped", line: 1, col: 4);
        e.ToggleTask();
        Assert.Equal("- [x] apple\nwrapped", e.Text);
    }

    [AvaloniaFact]
    public void OnANumberedItemsContinuationLine_CheckboxesTheOwningItem()
    {
        var e = At("1. apple\nwrapped", line: 1, col: 4);
        e.ToggleTask();
        Assert.Equal("1. [ ] apple\nwrapped", e.Text);
    }

    // ----- the selection survives the press (and takes in the new marker when it started at the text) -----

    private static MarkdownEditor Sel(string text, int start, int end)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = new Selection(start, end);
        return e;
    }

    [AvaloniaFact]
    public void WithTextSelected_KeepsItSelected_AndTakesInTheNewCheckbox()
    {
        // The selection reached the line's first non-space character, so the marker inserted there joins it.
        var e = Sel("buy milk", 0, 8);
        e.ToggleTask();
        Assert.Equal("- [ ] buy milk", e.Text);
        Assert.Equal("- [ ] buy milk", Selected(e)); // was collapsed to a bare caret
    }

    [AvaloniaFact]
    public void WithAMidLineSelection_KeepsExactlyThatTextSelected_WithoutTheCheckbox()
    {
        // The highlight didn't start at the text's beginning, so it just rides across the insertion.
        var e = Sel("buy milk", 4, 8);
        e.ToggleTask();
        Assert.Equal("- [ ] buy milk", e.Text);
        Assert.Equal("milk", Selected(e));
    }

    [AvaloniaFact]
    public void OnAListItem_ASelectionOfTheContentDoesNotSwallowTheCheckbox()
    {
        // "item" doesn't reach the line's first non-space character (the bullet), so the box stays outside it.
        var e = Sel("- item", 2, 6);
        e.ToggleTask();
        Assert.Equal("- [ ] item", e.Text);
        Assert.Equal("item", Selected(e));
    }

    [AvaloniaFact]
    public void OnAListItem_ASelectionFromTheBulletEnclosesTheCheckbox()
    {
        var e = Sel("- item", 0, 6);
        e.ToggleTask();
        Assert.Equal("- [ ] item", e.Text);
        Assert.Equal("- [ ] item", Selected(e));
    }

    [AvaloniaFact]
    public void AnIndentedSelection_TakesInTheCheckboxButNotTheIndent()
    {
        var e = Sel("  buy milk", 2, 10);
        e.ToggleTask();
        Assert.Equal("  - [ ] buy milk", e.Text);
        Assert.Equal("- [ ] buy milk", Selected(e));
    }

    [AvaloniaFact]
    public void ABackwardsSelection_KeepsItsDirection()
    {
        var e = Sel("buy milk", 8, 0); // anchor at the end, caret at the start
        e.ToggleTask();
        var sel = e.Buffer.Selection;
        Assert.Equal(14, sel.Anchor);
        Assert.Equal(0, sel.Caret);
    }

    private static string Selected(MarkdownEditor e)
    {
        var sel = e.Buffer.Selection;
        return e.Buffer.Document.GetText(sel.Start, sel.Length);
    }

    [AvaloniaFact]
    public void OnAWrappedPlainParagraph_CheckboxesTheParagraphOnce()
    {
        // Consecutive unmarked lines are one paragraph on screen, so it takes one checkbox on its first line.
        var e = new MarkdownEditor { Text = "a\nb\nc" };
        e.Buffer.Selection = new Selection(0, 5);
        e.ToggleTask();
        Assert.Equal("- [ ] a\nb\nc", e.Text);
    }
}
