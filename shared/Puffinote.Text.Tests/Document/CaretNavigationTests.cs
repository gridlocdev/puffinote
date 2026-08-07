using Puffinote.Text.Document;
using Puffinote.Text.Editing;
using Xunit;

namespace Puffinote.Text.Tests.Document;

/// <summary>
/// The line-scoped caret navigation (an O(line) optimization over the old whole-document scan)
/// must move the caret exactly as a grapheme walk over the full document text would — including
/// across line boundaries and over <c>\r\n</c>, surrogate pairs, and combining marks.
/// </summary>
public class CaretNavigationTests
{
    [Theory]
    [InlineData("hello world")]
    [InlineData("line one\nline two\nthree")]
    [InlineData("crlf\r\nlines\r\nhere")]
    [InlineData("emoji \U0001F600 mid\nand \U0001F468‍\U0001F469 family")]
    [InlineData("accent é and more\ncombining áb")]
    [InlineData("\n\n\nblank lines\n\n")]
    public void NextAndPrevious_MatchWholeDocumentWalk(string text)
    {
        var buffer = new DocumentBuffer(text);
        var full = text;

        // Forward: stepping from 0 to the end must visit the same boundaries as a whole-text walk.
        var pos = 0;
        while (pos < full.Length)
        {
            var expected = Grapheme.NextBoundary(full, pos);
            var actual = buffer.NextCaret(pos);
            Assert.Equal(expected, actual);
            pos = actual;
        }

        Assert.Equal(full.Length, buffer.NextCaret(full.Length)); // clamps at end

        // Backward.
        pos = full.Length;
        while (pos > 0)
        {
            var expected = Grapheme.PreviousBoundary(full, pos);
            var actual = buffer.PreviousCaret(pos);
            Assert.Equal(expected, actual);
            pos = actual;
        }

        Assert.Equal(0, buffer.PreviousCaret(0)); // clamps at start
    }

    [Fact]
    public void Crlf_CrossesAsOneUnit()
    {
        var buffer = new DocumentBuffer("a\r\nb");
        // From just before the '\r' (offset 1), the next caret skips the whole "\r\n".
        Assert.Equal(3, buffer.NextCaret(1));
        // From the start of "b" (offset 3), the previous caret skips back over "\r\n".
        Assert.Equal(1, buffer.PreviousCaret(3));
    }

    [Fact]
    public void Stats_AreCachedAndInvalidatedOnEdit()
    {
        var buffer = new DocumentBuffer("one two three");
        var first = buffer.Stats;
        Assert.Equal(3, first.Words);
        Assert.Equal(first, buffer.Stats); // same value on repeated reads (cached)

        buffer.Selection = Selection.At(buffer.Document.Length);
        buffer.Insert(" four");
        Assert.Equal(4, buffer.Stats.Words); // recomputed after the edit
    }
}
