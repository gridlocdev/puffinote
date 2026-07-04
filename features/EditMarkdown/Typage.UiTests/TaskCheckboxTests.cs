using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Clicking a task-list checkbox toggles it in place (PRD §7.2). The pointer path can't be driven
/// headlessly, so these exercise the hit-test/toggle the click delegates to.
/// </summary>
public class TaskCheckboxTests
{
    [AvaloniaFact]
    public void Toggle_UncheckedToChecked()
    {
        var editor = new MarkdownEditor { Text = "- [ ] todo" };
        Assert.True(editor.ToggleTaskForTest(0, 3)); // inside the "[ ]"
        Assert.Equal("- [x] todo", editor.Text);
    }

    [AvaloniaFact]
    public void Toggle_CheckedToUnchecked()
    {
        var editor = new MarkdownEditor { Text = "- [x] done" };
        Assert.True(editor.ToggleTaskForTest(0, 3));
        Assert.Equal("- [ ] done", editor.Text);
    }

    [AvaloniaFact]
    public void Toggle_LeavesCaretAndOtherLinesAlone()
    {
        var editor = new MarkdownEditor { Text = "- [ ] a\n- [ ] b" };
        var line2Start = editor.Text.IndexOf('b') - 4; // start of "[ ] b" marker box on line 2
        editor.ToggleTaskForTest(1, line2Start + 1);
        Assert.Equal("- [ ] a\n- [x] b", editor.Text);
    }

    [AvaloniaFact]
    public void Toggle_ReturnsFalseOffTheCheckbox()
    {
        var editor = new MarkdownEditor { Text = "- [ ] todo" };
        Assert.False(editor.ToggleTaskForTest(0, 9)); // on the word "todo", not the box
        Assert.Equal("- [ ] todo", editor.Text);
    }

    [AvaloniaFact]
    public void Toggle_ReturnsFalseOnAPlainListItem()
    {
        var editor = new MarkdownEditor { Text = "- not a task" };
        Assert.False(editor.ToggleTaskForTest(0, 2));
        Assert.Equal("- not a task", editor.Text);
    }
}
