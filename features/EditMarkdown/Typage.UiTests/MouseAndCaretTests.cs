using Typage.Control.Controls;
using Typage.Engine.Document;
using Avalonia.Headless.XUnit;
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
        // Down through a short line then down again returns to the original column.
        var editor = new MarkdownEditor { Text = "abcdefghij\nxy\nabcdefghij" };
        editor.Buffer.Selection = Selection.At(6); // column 6 on line 0
        editor.MoveCaretByLineForTest(1); // line 1 ("xy") — caret clamps inward
        var onShort = editor.Buffer.Document.GetPosition(editor.Buffer.Selection.Caret);
        Assert.Equal(1, onShort.Line);

        editor.MoveCaretByLineForTest(1); // line 2 — goal column restored
        var onLong = editor.Buffer.Document.GetPosition(editor.Buffer.Selection.Caret);
        Assert.Equal(2, onLong.Line);
        Assert.Equal(6, onLong.Column);
    }

    [AvaloniaFact]
    public void GoalColumn_ResetsAfterAHorizontalMove()
    {
        var editor = new MarkdownEditor { Text = "abcdefghij\nxy\nabcdefghij" };
        editor.Buffer.Selection = Selection.At(6);
        editor.MoveCaretByLineForTest(1); // to short line, goal = column 6
        editor.ClickAtForTest(editor.Buffer.Document.GetLineStart(1)); // a click resets the goal
        editor.MoveCaretByLineForTest(1); // to line 2 — column should track the click (0), not 6
        Assert.Equal(0, editor.Buffer.Document.GetPosition(editor.Buffer.Selection.Caret).Column);
    }
}
