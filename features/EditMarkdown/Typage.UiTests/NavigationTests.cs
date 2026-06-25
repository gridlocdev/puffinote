using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Control.Views;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Standard caret-navigation and editing affordances expected of any editor (Nielsen #4 consistency,
/// #7 efficiency): word-wise movement and deletion, smart Home, visual-row Home/End under word wrap,
/// and Page Up/Down.
/// </summary>
public class NavigationTests
{
    private static MarkdownEditor At(string text, int caret)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(caret);
        return e;
    }

    private static int Caret(MarkdownEditor e) => e.Buffer.Selection.Caret;

    [AvaloniaFact]
    public void WordRight_StopsAtTheEndOfEachWord()
    {
        var e = At("hello world", 0);
        e.WordMoveForTest(1);
        Assert.Equal(5, Caret(e)); // end of "hello"
        e.WordMoveForTest(1);
        Assert.Equal(11, Caret(e)); // end of "world"
    }

    [AvaloniaFact]
    public void WordLeft_StopsAtTheStartOfTheWord()
    {
        var e = At("hello world", 11);
        e.WordMoveForTest(-1);
        Assert.Equal(6, Caret(e)); // start of "world"
    }

    [AvaloniaFact]
    public void DeleteWordBackward_RemovesThePrecedingWord()
    {
        var e = At("hello world", 11);
        e.DeleteWordForTest(-1);
        Assert.Equal("hello ", e.Text);
    }

    [AvaloniaFact]
    public void DeleteWordForward_RemovesTheFollowingWord()
    {
        var e = At("hello world", 0);
        e.DeleteWordForTest(1);
        Assert.Equal(" world", e.Text);
    }

    [AvaloniaFact]
    public void SmartHome_TogglesBetweenContentStartAndRowStart()
    {
        var e = At("- [ ] task", 10); // caret at end; "task" begins at offset 6 (past the hidden marker)
        e.HomeForTest();
        Assert.Equal(6, Caret(e)); // first Home → start of the visible content
        e.HomeForTest();
        Assert.Equal(0, Caret(e)); // second Home → true start of the row
    }

    [AvaloniaFact]
    public void End_GoesToTheEndOfTheLine()
    {
        var e = At("hello", 0);
        e.EndForTest();
        Assert.Equal(5, Caret(e));
    }

    [AvaloniaFact]
    public void TripleClick_SelectsTheWholeParagraph()
    {
        var e = new MarkdownEditor { Text = "first line\nsecond line" }; // joins into one paragraph block
        e.SelectBlockForTest(0);
        Assert.Equal("first line\nsecond line", e.SelectedText);
    }

    [AvaloniaFact]
    public void HomeAndEnd_AreVisualRowRelative_WhenWrapped()
    {
        var view = new MarkdownEditorView();
        var window = new Window { Width = 200, Height = 300, Content = view };
        window.Show();
        view.Text = string.Join(' ', System.Linq.Enumerable.Repeat("word", 60));
        window.CaptureRenderedFrame();

        var editor = view.Editor;
        editor.Buffer.Selection = Selection.At(editor.Buffer.Document.Length); // caret on the last wrapped row
        editor.HomeForTest();
        var rowStart = editor.Buffer.Selection.Caret;
        Assert.True(rowStart > 0, "Home on a wrapped line should land on the row's start, not the document line start");

        editor.EndForTest();
        Assert.True(editor.Buffer.Selection.Caret > rowStart, "End should move to the end of the same visual row");
    }
}
