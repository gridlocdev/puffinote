using Typage.Engine.Document;
using Xunit;

namespace Typage.Engine.Tests.Document;

public class TextDocumentTests
{
    [Fact]
    public void FromString_RoundTrips()
    {
        var doc = TextDocument.FromString("hello world");
        Assert.Equal(11, doc.Length);
        Assert.Equal("hello world", doc.GetText());
        Assert.Equal("world", doc.GetText(6, 5));
    }

    [Fact]
    public void Empty_HasOneLine()
    {
        Assert.Equal(0, TextDocument.Empty.Length);
        Assert.Equal(1, TextDocument.Empty.LineCount);
        Assert.Equal(string.Empty, TextDocument.Empty.GetLineText(0));
    }

    [Fact]
    public void Insert_ReturnsNewDocument_AndSharesNothingObservable()
    {
        var a = TextDocument.FromString("abcd");
        var b = a.Insert(2, "XY");
        Assert.Equal("abcd", a.GetText());   // original unchanged (immutable)
        Assert.Equal("abXYcd", b.GetText());
    }

    [Fact]
    public void Insert_AtStartAndEnd()
    {
        var a = TextDocument.FromString("mid");
        Assert.Equal("[mid", a.Insert(0, "[").GetText());
        Assert.Equal("mid]", a.Insert(3, "]").GetText());
    }

    [Fact]
    public void Delete_RemovesRange()
    {
        var a = TextDocument.FromString("abcdef");
        Assert.Equal("abef", a.Delete(2, 2).GetText());
        Assert.Equal("", a.Delete(0, 6).GetText());
    }

    [Fact]
    public void Replace_SwapsRange()
    {
        var a = TextDocument.FromString("the quick fox");
        Assert.Equal("the slow fox", a.Replace(4, 5, "slow").GetText());
    }

    [Fact]
    public void Indexer_ReadsAcrossPieces()
    {
        var a = TextDocument.FromString("abc").Insert(1, "XY").Insert(0, "Z");
        // "Z" + "a" + "XY" + "bc" => "ZaXYbc"
        Assert.Equal("ZaXYbc", a.GetText());
        Assert.Equal('X', a[2]);
        Assert.Equal('c', a[5]);
    }

    [Fact]
    public void Lines_AreSplitOnLf()
    {
        var doc = TextDocument.FromString("one\ntwo\nthree");
        Assert.Equal(3, doc.LineCount);
        Assert.Equal("one", doc.GetLineText(0));
        Assert.Equal("two", doc.GetLineText(1));
        Assert.Equal("three", doc.GetLineText(2));
    }

    [Fact]
    public void TrailingNewline_AddsEmptyLastLine()
    {
        var doc = TextDocument.FromString("a\n");
        Assert.Equal(2, doc.LineCount);
        Assert.Equal("", doc.GetLineText(1));
    }

    [Fact]
    public void Crlf_KeptAsLineContent()
    {
        var doc = TextDocument.FromString("a\r\nb");
        Assert.Equal(2, doc.LineCount);
        Assert.Equal("a\r", doc.GetLineText(0)); // '\r' preserved verbatim for byte-for-byte round trip
        Assert.Equal("b", doc.GetLineText(1));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 0, 3)]
    [InlineData(4, 1, 0)]
    [InlineData(6, 1, 2)]
    public void GetPosition_MapsOffsetToLineColumn(int offset, int line, int col)
    {
        var doc = TextDocument.FromString("abc\ndef"); // offsets: a0 b1 c2 \n3 d4 e5 f6
        var pos = doc.GetPosition(offset);
        Assert.Equal(line, pos.Line);
        Assert.Equal(col, pos.Column);
    }

    [Fact]
    public void GetOffset_IsInverseOfGetPosition()
    {
        var doc = TextDocument.FromString("hello\nworld\n!");
        for (var off = 0; off <= doc.Length; off++)
        {
            var pos = doc.GetPosition(off);
            Assert.Equal(off, doc.GetOffset(pos.Line, pos.Column));
        }
    }

    [Fact]
    public void ManyEdits_StayConsistent()
    {
        var doc = TextDocument.FromString("");
        doc = doc.Insert(0, "Hello");
        doc = doc.Insert(5, " World");
        doc = doc.Replace(0, 5, "Goodbye");
        doc = doc.Delete(7, 6); // remove " World"
        Assert.Equal("Goodbye", doc.GetText());
    }

    // The line-start index is patched incrementally on each edit (no whole-document rescan). These pin the
    // tricky boundary cases; the fuzz test below proves equivalence to a from-scratch scan in general.

    [Fact]
    public void IncrementalLineStarts_InsertingANewline_AddsALine()
    {
        var doc = TextDocument.FromString("abcd");
        _ = doc.LineCount; // force the pre-edit starts to be cached → the patched path runs
        doc = doc.Insert(2, "X\nY");
        Assert.Equal(2, doc.LineCount);
        AssertLineStartsMatchFromScratch(doc);
    }

    [Fact]
    public void IncrementalLineStarts_DeletingTheNewline_JoinsLines()
    {
        var doc = TextDocument.FromString("ab\ncd");
        _ = doc.LineCount;
        doc = doc.Delete(2, 1); // remove the '\n'
        Assert.Equal(1, doc.LineCount);
        Assert.Equal("abcd", doc.GetText());
        AssertLineStartsMatchFromScratch(doc);
    }

    [Fact]
    public void IncrementalLineStarts_ReplacingAcrossANewline()
    {
        var doc = TextDocument.FromString("one\ntwo\nthree");
        _ = doc.LineCount;
        doc = doc.Replace(2, 4, "X\nY\nZ"); // spans the first '\n'
        AssertLineStartsMatchFromScratch(doc);
    }

    [Fact]
    public void IncrementalLineStarts_DeletingExactlyUpToALineStart()
    {
        var doc = TextDocument.FromString("a\nb\nc");
        _ = doc.LineCount;
        doc = doc.Delete(2, 2); // remove "b\n" — deleteEnd lands on the next line start
        Assert.Equal("a\nc", doc.GetText());
        AssertLineStartsMatchFromScratch(doc);
    }

    [Fact]
    public void IncrementalLineStarts_MatchFromScratch_AcrossRandomEdits()
    {
        var rng = new System.Random(0xC0FFEE);
        var doc = TextDocument.FromString("line one\nline two\r\nthree\n\nlast");

        for (var step = 0; step < 3000; step++)
        {
            _ = doc.LineCount; // ensure the pre-edit document has its starts cached (patched path, not lazy)
            var len = doc.Length;
            var offset = rng.Next(0, len + 1);

            doc = rng.Next(3) switch
            {
                0 => doc.Insert(offset, RandomChunk(rng)),
                1 => doc.Delete(offset, rng.Next(0, System.Math.Min(6, len - offset) + 1)),
                _ => doc.Replace(offset, rng.Next(0, System.Math.Min(6, len - offset) + 1), RandomChunk(rng)),
            };

            AssertLineStartsMatchFromScratch(doc);
        }
    }

    private static string RandomChunk(System.Random rng)
    {
        var n = rng.Next(0, 5);
        var chars = new char[n];
        for (var i = 0; i < n; i++)
        {
            chars[i] = rng.Next(6) switch { 0 => '\n', 1 => '\r', 2 => '\n', _ => (char)('a' + rng.Next(3)) };
        }

        return new string(chars);
    }

    /// <summary>Assert the document's (incrementally patched) line index matches a fresh from-scratch scan.</summary>
    private static void AssertLineStartsMatchFromScratch(TextDocument doc)
    {
        var fresh = TextDocument.FromString(doc.GetText()); // lazy null → trusted ComputeLineStarts
        Assert.Equal(fresh.LineCount, doc.LineCount);
        for (var line = 0; line < fresh.LineCount; line++)
        {
            Assert.Equal(fresh.GetLineStart(line), doc.GetLineStart(line));
        }
    }
}
