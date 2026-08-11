using Puffinote.Text.Editing;
using Xunit;

namespace Puffinote.Text.Tests.Editing;

/// <summary>
/// Taking the version on disk is an <b>edit</b>, not a new document: the history survives it, so the reader
/// who reloads and immediately regrets it can press ⌘Z. This is the piece every app in the family reloads
/// text through.
/// </summary>
public class ReloadFromTests
{
    [Fact]
    public void ReloadKeepsTheHistory_SoUndoGivesBackWhatWasInTheEditor()
    {
        var buffer = new DocumentBuffer("mine") { Selection = Puffinote.Text.Document.Selection.At(4) };
        buffer.Insert(" and mine");
        Assert.Equal("mine and mine", buffer.Document.GetText());

        buffer.ReloadFrom("theirs");
        Assert.Equal("theirs", buffer.Document.GetText());
        Assert.False(buffer.IsDirty); // the buffer is the file now

        Assert.True(buffer.Undo());
        Assert.Equal("mine and mine", buffer.Document.GetText());
        Assert.True(buffer.IsDirty); // and it differs from the file again, which it does

        Assert.True(buffer.Redo());
        Assert.Equal("theirs", buffer.Document.GetText());

        // The edits from before the reload are still there to undo through.
        Assert.True(buffer.Undo());
        Assert.True(buffer.Undo());
        Assert.Equal("mine", buffer.Document.GetText());
    }

    [Fact]
    public void ReloadingIdenticalTextIsNotAStepToUndo()
    {
        // A save of our own comes back through the watcher; nothing changed, so nothing should be undoable.
        var buffer = new DocumentBuffer("same");
        buffer.ReloadFrom("same");

        Assert.False(buffer.CanUndo);
        Assert.False(buffer.IsDirty);
    }

    [Fact]
    public void TheCaretKeepsItsPlaceWhereTheNewTextIsLongEnough()
    {
        var buffer = new DocumentBuffer("one two three") { Selection = Puffinote.Text.Document.Selection.At(8) };

        buffer.ReloadFrom("one two THREE");
        Assert.Equal(8, buffer.Selection.Caret);

        buffer.ReloadFrom("no"); // shorter than the caret's offset: clamped, not thrown
        Assert.Equal(2, buffer.Selection.Caret);
    }

    [Fact]
    public void LoadStillClearsTheHistory_BecauseItIsADifferentDocument()
    {
        var buffer = new DocumentBuffer("first");
        buffer.Insert("!");
        buffer.Load("second");

        Assert.False(buffer.CanUndo);
        Assert.Equal("second", buffer.Document.GetText());
    }
}
