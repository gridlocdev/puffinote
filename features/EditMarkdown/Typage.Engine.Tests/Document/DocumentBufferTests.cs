using Typage.Engine.Document;
using Typage.Engine.Editing;
using Xunit;

namespace Typage.Engine.Tests.Document;

public class DocumentBufferTests
{
    [Fact]
    public void Insert_AtCaret_MovesCaret()
    {
        var buf = new DocumentBuffer("");
        buf.Insert("hi");
        Assert.Equal("hi", buf.Document.GetText());
        Assert.Equal(2, buf.Selection.Caret);
        Assert.True(buf.IsDirty);
    }

    [Fact]
    public void Insert_ReplacesSelection()
    {
        var buf = new DocumentBuffer("hello")
        {
            Selection = new Selection(0, 5)
        };
        buf.Insert("bye");
        Assert.Equal("bye", buf.Document.GetText());
        Assert.Equal(3, buf.Selection.Caret);
    }

    [Fact]
    public void Backspace_DeletesGraphemeBeforeCaret()
    {
        var buf = new DocumentBuffer("a\U0001F600");
        buf.Selection = Selection.At(buf.Document.Length);
        buf.Backspace();
        Assert.Equal("a", buf.Document.GetText()); // whole emoji removed as one unit
    }

    [Fact]
    public void ForwardDelete_DeletesGraphemeAfterCaret()
    {
        var buf = new DocumentBuffer("\U0001F600z")
        {
            Selection = Selection.At(0)
        };
        buf.Delete();
        Assert.Equal("z", buf.Document.GetText());
    }

    [Fact]
    public void Undo_Redo_RestoresTextAndSelection()
    {
        var buf = new DocumentBuffer("");
        buf.Insert("abc");
        buf.Selection = Selection.At(0);
        buf.Insert("X"); // -> "Xabc"
        Assert.Equal("Xabc", buf.Document.GetText());

        Assert.True(buf.Undo());
        Assert.Equal("abc", buf.Document.GetText());
        Assert.True(buf.Undo());
        Assert.Equal("", buf.Document.GetText());
        Assert.False(buf.Undo());

        Assert.True(buf.Redo());
        Assert.Equal("abc", buf.Document.GetText());
        Assert.True(buf.Redo());
        Assert.Equal("Xabc", buf.Document.GetText());
    }

    [Fact]
    public void TypingRun_CoalescesIntoOneUndoStep()
    {
        var buf = new DocumentBuffer("");
        buf.Insert("h");
        buf.Insert("e");
        buf.Insert("l");
        buf.Insert("l");
        buf.Insert("o");
        Assert.Equal("hello", buf.Document.GetText());

        Assert.True(buf.Undo());
        Assert.Equal("", buf.Document.GetText()); // the whole run undoes at once
    }

    [Fact]
    public void CaretJump_BreaksCoalescing()
    {
        var buf = new DocumentBuffer("");
        buf.Insert("ab");
        buf.Selection = Selection.At(0); // caret jump
        buf.Insert("X");
        Assert.Equal("Xab", buf.Document.GetText());

        Assert.True(buf.Undo());
        Assert.Equal("ab", buf.Document.GetText()); // only the post-jump insert undoes
    }

    [Fact]
    public void Newline_BreaksCoalescing()
    {
        var buf = new DocumentBuffer("");
        buf.Insert("a");
        buf.Insert("\n");
        buf.Insert("b");
        Assert.True(buf.Undo());
        Assert.Equal("a\n", buf.Document.GetText()); // the 'b' run is its own step
    }

    [Fact]
    public void IsDirty_TracksSavedBaseline()
    {
        var buf = new DocumentBuffer("x");
        Assert.False(buf.IsDirty);
        buf.Insert("y");
        Assert.True(buf.IsDirty);
        buf.MarkSaved();
        Assert.False(buf.IsDirty);
    }

    [Fact]
    public void Load_ClearsHistoryAndDirty()
    {
        var buf = new DocumentBuffer("old");
        buf.Insert("!");
        buf.Load("fresh");
        Assert.Equal("fresh", buf.Document.GetText());
        Assert.False(buf.IsDirty);
        Assert.False(buf.CanUndo);
    }
}
