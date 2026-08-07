using Puffinote.Text.Document;
using Xunit;

namespace Puffinote.Text.Tests.Document;

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

    [Fact]
    public void Reads_AcrossManyPieces_ResolveAtBoundaries()
    {
        // Force several non-coalescing pieces, then read characters and ranges that straddle piece boundaries —
        // exercises the binary-search piece index at first/last/interior offsets.
        var doc = TextDocument.FromString("ABCDEFGH");
        doc = doc.Insert(2, "12");  // A B 1 2 C D E F G H
        doc = doc.Insert(6, "xy");  // A B 1 2 C D x y E F G H
        const string expected = "AB12CDxyEFGH";

        Assert.Equal(expected, doc.GetText());
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], doc[i]); // every char, including each piece's first/last
        }

        Assert.Equal("12CDxy", doc.GetText(2, 6)); // a range spanning three pieces
        Assert.Equal("H", doc.GetText(expected.Length - 1, 1)); // last char
        Assert.Equal(expected, doc.GetText(0, expected.Length)); // whole range via the indexed path
    }

    [Fact]
    public void SequentialTyping_CoalescesIntoFewPieces()
    {
        // Typing one character at a time at the end appends contiguously to the add buffer, so the pieces
        // coalesce instead of growing ~1 per keystroke.
        var doc = TextDocument.FromString("start: ");
        for (var i = 0; i < 50; i++)
        {
            doc = doc.Insert(doc.Length, "x");
        }

        Assert.Equal("start: " + new string('x', 50), doc.GetText());
        Assert.True(doc.PieceCount <= 3, $"expected coalescing to keep pieces small, got {doc.PieceCount}");
    }

    [Fact]
    public void DeletingFromTheMiddle_DoesNotGrowPiecesUnbounded()
    {
        // Backspacing the just-typed tail shrinks the trailing add piece in place rather than fragmenting.
        var doc = TextDocument.FromString("abc");
        for (var i = 0; i < 20; i++)
        {
            doc = doc.Insert(doc.Length, "y");
        }

        for (var i = 0; i < 20; i++)
        {
            doc = doc.Delete(doc.Length - 1, 1);
        }

        Assert.Equal("abc", doc.GetText());
        Assert.True(doc.PieceCount <= 2, $"got {doc.PieceCount}");
    }

    [Fact]
    public void ContentAndReads_MatchAReferenceString_AcrossRandomEdits()
    {
        // Fuzz the piece table against a plain string oracle: after every random edit the full text, every
        // random substring (GetText(start,len)), and every random char (indexer) must agree. Guards the
        // piece-coalescing / add-buffer machinery and any piece-lookup index.
        var rng = new System.Random(0x5EED);
        var doc = TextDocument.FromString("the quick brown fox\njumps over\nthe lazy dog");
        var reference = doc.GetText();

        for (var step = 0; step < 4000; step++)
        {
            var offset = rng.Next(0, reference.Length + 1);
            switch (rng.Next(3))
            {
                case 0:
                    var ins = RandomChunk(rng);
                    doc = doc.Insert(offset, ins);
                    reference = reference[..offset] + ins + reference[offset..];
                    break;
                case 1:
                    var del = rng.Next(0, System.Math.Min(7, reference.Length - offset) + 1);
                    doc = doc.Delete(offset, del);
                    reference = reference[..offset] + reference[(offset + del)..];
                    break;
                default:
                    var rl = rng.Next(0, System.Math.Min(7, reference.Length - offset) + 1);
                    var rep = RandomChunk(rng);
                    doc = doc.Replace(offset, rl, rep);
                    reference = reference[..offset] + rep + reference[(offset + rl)..];
                    break;
            }

            Assert.Equal(reference, doc.GetText());
            Assert.Equal(reference.Length, doc.Length);

            // a handful of random range/char reads each step
            for (var r = 0; r < 4 && reference.Length > 0; r++)
            {
                var s = rng.Next(0, reference.Length);
                var len = rng.Next(0, reference.Length - s + 1);
                Assert.Equal(reference.Substring(s, len), doc.GetText(s, len));
                Assert.Equal(reference[s], doc[s]);
            }
        }
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
