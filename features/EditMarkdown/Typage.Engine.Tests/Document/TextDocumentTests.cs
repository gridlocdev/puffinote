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
}
