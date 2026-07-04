using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Double-click word selection and the sticky goal column for up/down movement. Pointer events
/// can't be driven headlessly, so these call the hit-test/movement the handlers delegate to.
/// </summary>
public class MouseAndCaretTests
{
    [AvaloniaFact]
    public void DoubleClick_SelectsWord()
    {
        var editor = new MarkdownEditor { Text = "the quick brown fox" };
        editor.SelectWordForTest(0, 6); // inside "quick"
        Assert.Equal("quick", editor.SelectedText);
    }

    [AvaloniaFact]
    public void DoubleClick_OnPunctuation_SelectsPunctuationRun()
    {
        var editor = new MarkdownEditor { Text = "a... b" };
        editor.SelectWordForTest(0, 2); // inside the "..."
        Assert.Equal("...", editor.SelectedText);
    }

    [AvaloniaFact]
    public void DoubleClick_OnEmptyLine_CollapsesCaret()
    {
        var editor = new MarkdownEditor { Text = "a\n\nb" };
        editor.SelectWordForTest(1, 2); // the empty middle line
        Assert.Equal(0, editor.Buffer.Selection.Length);
    }

    [AvaloniaFact]
    public void GoalColumn_SurvivesAShortLine()
    {
        // Down through a short line then down again returns to the original column. Headings are used so
        // the three lines stay separate blocks (plain paragraphs would join into one flowing paragraph).
        var editor = new MarkdownEditor { Text = "# abcdefghij\n# xy\n# abcdefghij" };
        editor.Buffer.Selection = Selection.At(8); // column 8 on line 0 (inside the heading text)
        editor.MoveCaretByLineForTest(1); // line 1 ("# xy") — caret clamps inward
        var onShort = editor.Buffer.Document.GetPosition(editor.Buffer.Selection.Caret);
        Assert.Equal(1, onShort.Line);

        editor.MoveCaretByLineForTest(1); // line 2 — goal column restored
        var onLong = editor.Buffer.Document.GetPosition(editor.Buffer.Selection.Caret);
        Assert.Equal(2, onLong.Line);
        Assert.Equal(8, onLong.Column);
    }

    [AvaloniaFact]
    public void UpArrow_OnTheFirstRow_JumpsToTheDocumentStart()
    {
        // Caret mid-way along the top line; Up has nowhere higher to go, so it lands at the very start.
        var editor = new MarkdownEditor { Text = "# abcdefghij\n# xy\n# more text" };
        editor.Buffer.Selection = Selection.At(8); // column 8 on the first line
        editor.MoveCaretByLineForTest(-1);
        Assert.Equal(0, editor.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void DownArrow_OnTheLastRow_JumpsToTheDocumentEnd()
    {
        var editor = new MarkdownEditor { Text = "# abcdefghij\n# xy\n# more text" };
        var len = editor.Buffer.Document.Length;
        editor.Buffer.Selection = Selection.At(len - 6); // partway along the last line
        editor.MoveCaretByLineForTest(1);
        Assert.Equal(len, editor.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void UpArrow_OnAnInteriorRow_StillStepsOneRow_NotToTheStart()
    {
        // The edge jump must only fire at the true top: from line 1, Up goes to line 0, not offset 0.
        var editor = new MarkdownEditor { Text = "# abcdefghij\n# xydef\n# more text" };
        editor.Buffer.Selection = Selection.At(editor.Buffer.Document.GetLineStart(1) + 4);
        editor.MoveCaretByLineForTest(-1);
        Assert.Equal(0, editor.Buffer.Document.GetPosition(editor.Buffer.Selection.Caret).Line);
        Assert.NotEqual(0, editor.Buffer.Selection.Caret); // landed on line 0 at the goal column, not the very start
    }

    [AvaloniaFact]
    public void GoalColumn_ResetsAfterAHorizontalMove()
    {
        var editor = new MarkdownEditor { Text = "# abcdefghij\n# xy\n# abcdefghij" };
        editor.Buffer.Selection = Selection.At(8);
        editor.MoveCaretByLineForTest(1); // to short line, goal = column 8
        editor.ClickAtForTest(editor.Buffer.Document.GetLineStart(1) + 2); // a click resets the goal (at "xy" start)
        editor.MoveCaretByLineForTest(1); // to line 2 — column should track the click (2), not 8
        Assert.Equal(2, editor.Buffer.Document.GetPosition(editor.Buffer.Selection.Caret).Column);
    }
}
