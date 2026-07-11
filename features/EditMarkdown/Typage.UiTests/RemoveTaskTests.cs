using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// ⌘⇧L (<see cref="MarkdownEditor.RemoveTask"/>): strip a task checkbox from the caret's line, returning it
/// to plain text — <c>- [ ] text</c> becomes <c>text</c>. It is the inverse of the create half of
/// <see cref="MarkdownEditor.ToggleTask"/> (which inserts a whole <c>- [ ] </c> item), so the unordered bullet
/// comes off with the box. An ordered <c>1. </c> marker is author-written and stays; indentation and blockquote
/// prefixes are always preserved.
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
    public void OnAnUncheckedTask_RemovesTheBoxAndBullet_BackToPlainText()
    {
        var e = At("- [ ] buy milk", col: 8);
        e.RemoveTask();
        Assert.Equal("buy milk", e.Text);
    }

    [AvaloniaFact]
    public void OnACheckedTask_RemovesTheBoxAndBullet_BackToPlainText()
    {
        var e = At("- [x] done", col: 8);
        e.RemoveTask();
        Assert.Equal("done", e.Text);
    }

    [AvaloniaFact]
    public void PreservesIndentationOnANestedTask_DroppingOnlyTheBulletAndBox()
    {
        var e = At("  - [ ] child", line: 0, col: 10);
        e.RemoveTask();
        Assert.Equal("  child", e.Text);
    }

    [AvaloniaFact]
    public void PreservesABlockquotePrefix()
    {
        var e = At("> - [ ] quoted task", col: 12);
        e.RemoveTask();
        Assert.Equal("> quoted task", e.Text);
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
        var e = At("- [ ] task", col: 8); // caret on the 's' of "task", six chars past the "- [ ] " prefix
        e.RemoveTask();
        Assert.Equal("task", e.Text);
        // After removing the 6-char "- [ ] " prefix the caret sits on the same 's'.
        Assert.Equal(2, e.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void RoundTripsToPlainTextWithToggleTask()
    {
        var e = At("plain line", col: 4);
        e.ToggleTask(); // - [ ] plain line
        Assert.Equal("- [ ] plain line", e.Text);
        e.RemoveTask(); // all the way back to plain text
        Assert.Equal("plain line", e.Text);
    }

    [AvaloniaFact]
    public void IsASingleUndo()
    {
        var e = At("- [x] done", col: 8);
        e.RemoveTask();
        e.Undo();
        Assert.Equal("- [x] done", e.Text);
    }

    [AvaloniaFact]
    public void OnAContinuationLine_StripsTheOwningItemsBox()
    {
        // The caret sits on the wrapped item's second line, which flows into the item above: ⌘⇧L must unwind
        // that item back to plain text, leaving the continuation to flow on into the resulting paragraph.
        var e = At("- [ ] apple\nwrapped", line: 1, col: 4);
        e.RemoveTask();
        Assert.Equal("apple\nwrapped", e.Text);
    }
}
