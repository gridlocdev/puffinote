using Avalonia.Headless.XUnit;
using Puffinote.Text.Document;
using Typage.Control.Controls;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Alt+Shift+Up/Down line reordering (item 9): the current line or selected block swaps with the
/// adjacent line as one undoable edit, and the selection rides along with the moved text.
/// </summary>
public class MoveLinesTests
{
    private static MarkdownEditor At(string text, int caret)
    {
        var editor = new MarkdownEditor { Text = text };
        editor.Buffer.Selection = Selection.At(caret);
        return editor;
    }

    private static int CaretLine(MarkdownEditor e) =>
        e.Buffer.Document.GetPosition(e.Buffer.Selection.Caret).Line;

    [AvaloniaFact]
    public void MoveDown_SwapsWithNextLine_AndCarriesCaret()
    {
        var editor = At("a\nb\nc", caret: 0); // on "a"
        editor.MoveLinesForTest(1);
        Assert.Equal("b\na\nc", editor.Text);
        Assert.Equal(1, CaretLine(editor)); // still on "a", now line 1
    }

    [AvaloniaFact]
    public void MoveUp_SwapsWithPreviousLine_AndCarriesCaret()
    {
        var editor = At("a\nb\nc", caret: 4); // on "c"
        editor.MoveLinesForTest(-1);
        Assert.Equal("a\nc\nb", editor.Text);
        Assert.Equal(1, CaretLine(editor)); // still on "c", now line 1
    }

    [AvaloniaFact]
    public void MoveUp_AtTop_IsANoOp()
    {
        var editor = At("a\nb", caret: 0);
        editor.MoveLinesForTest(-1);
        Assert.Equal("a\nb", editor.Text);
    }

    [AvaloniaFact]
    public void MoveDown_AtBottom_IsANoOp()
    {
        var editor = At("a\nb", caret: 2);
        editor.MoveLinesForTest(1);
        Assert.Equal("a\nb", editor.Text);
    }

    [AvaloniaFact]
    public void MovesAMultiLineSelectionTogether()
    {
        var editor = new MarkdownEditor { Text = "a\nb\nc\nd" };
        editor.Buffer.Selection = new Selection(0, 3); // lines 0-1 ("a","b")
        editor.MoveLinesForTest(1);
        Assert.Equal("c\na\nb\nd", editor.Text); // the block moved below "c"
    }

    [AvaloniaFact]
    public void ReordersChecklistItems()
    {
        var editor = At("- [ ] first\n- [x] second", caret: 0);
        editor.MoveLinesForTest(1);
        Assert.Equal("- [x] second\n- [ ] first", editor.Text);
    }

    [AvaloniaFact]
    public void MoveIsASingleUndo()
    {
        var editor = At("a\nb\nc", caret: 0);
        editor.MoveLinesForTest(1);
        editor.Undo();
        Assert.Equal("a\nb\nc", editor.Text);
    }
}
