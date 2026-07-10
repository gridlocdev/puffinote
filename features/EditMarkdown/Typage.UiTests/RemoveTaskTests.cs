using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// ⌘⇧L (<see cref="MarkdownEditor.RemoveTask"/>): strip a task checkbox from the caret's line, turning
/// <c>- [ ] text</c> back into <c>- text</c> — the inverse of the create half of <see cref="MarkdownEditor.ToggleTask"/>.
/// The list marker and indentation stay; only the <c>[ ]</c>/<c>[x]</c> box goes.
/// </summary>
public class RemoveTaskTests
{
    private static MarkdownEditor At(string text, int line = 0, int col = 0)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(e.Buffer.Document.GetLineStart(line) + col);
        return e;
    }

    [AvaloniaFact]
    public void OnAnUncheckedTask_RemovesTheBoxAndKeepsTheBullet()
    {
        var e = At("- [ ] buy milk", col: 8);
        e.RemoveTask();
        Assert.Equal("- buy milk", e.Text);
    }

    [AvaloniaFact]
    public void OnACheckedTask_RemovesTheBoxAndKeepsTheBullet()
    {
        var e = At("- [x] done", col: 8);
        e.RemoveTask();
        Assert.Equal("- done", e.Text);
    }

    [AvaloniaFact]
    public void PreservesIndentationOnANestedTask()
    {
        var e = At("  - [ ] child", line: 0, col: 10);
        e.RemoveTask();
        Assert.Equal("  - child", e.Text);
    }

    [AvaloniaFact]
    public void OnAnOrderedTask_RemovesTheBoxAndKeepsTheNumberMarker()
    {
        var e = At("1. [ ] step", col: 9);
        e.RemoveTask();
        Assert.Equal("1. step", e.Text);
    }

    [AvaloniaFact]
    public void OnALineWithoutACheckbox_IsANoOp()
    {
        var e = At("- just a bullet", col: 4);
        e.RemoveTask();
        Assert.Equal("- just a bullet", e.Text);
    }

    [AvaloniaFact]
    public void KeepsTheCaretWithTheText()
    {
        var e = At("- [ ] task", col: 8); // caret inside "task", four chars past the box
        e.RemoveTask();
        Assert.Equal("- task", e.Text);
        // "- [ ] task"[8] is 't' of "task"; after removing the 4-char "[ ] " the caret sits on the same 't'.
        Assert.Equal(4, e.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void RoundTripsWithToggleTaskFromABullet()
    {
        var e = At("- buy milk", col: 4);
        e.ToggleTask(); // - [ ] buy milk
        e.RemoveTask(); // back to the bullet
        Assert.Equal("- buy milk", e.Text);
    }

    [AvaloniaFact]
    public void IsASingleUndo()
    {
        var e = At("- [x] done", col: 8);
        e.RemoveTask();
        e.Undo();
        Assert.Equal("- [x] done", e.Text);
    }
}
