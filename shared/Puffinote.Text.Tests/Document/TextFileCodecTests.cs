using System.Text;
using Puffinote.Text.Document;
using Xunit;

namespace Puffinote.Text.Tests.Document;

/// <summary>
/// Encoding round-trips byte-for-byte (PRD §7.1, golden rule #5): a document opened with a given
/// BOM/encoding is saved back with the same one, never silently rewritten to UTF-8.
/// </summary>
public class TextFileCodecTests
{
    [Fact]
    public void Utf8NoBom_RoundTrips()
    {
        var bytes = Encoding.UTF8.GetBytes("héllo");
        var (text, format) = TextFileCodec.Decode(bytes);
        Assert.Equal("héllo", text);
        Assert.False(format.HasBom);
        Assert.Equal(bytes, TextFileCodec.Encode(text, format));
    }

    [Fact]
    public void Utf8Bom_IsDetectedAndPreserved()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("hi")).ToArray();
        var (text, format) = TextFileCodec.Decode(bytes);
        Assert.Equal("hi", text);
        Assert.True(format.HasBom);
        Assert.Equal("UTF-8 BOM", format.DisplayName);
        Assert.Equal(bytes, TextFileCodec.Encode(text, format)); // BOM preserved
    }

    [Fact]
    public void Utf16Le_IsDetectedAndPreserved()
    {
        var bytes = new UnicodeEncoding(false, true).GetPreamble()
            .Concat(new UnicodeEncoding(false, true).GetBytes("ok")).ToArray();
        var (text, format) = TextFileCodec.Decode(bytes);
        Assert.Equal("ok", text);
        Assert.Equal("UTF-16 LE", format.DisplayName);
        Assert.Equal(bytes, TextFileCodec.Encode(text, format));
    }

    [Fact]
    public void Utf16Be_IsDetected()
    {
        var bytes = new UnicodeEncoding(true, true).GetPreamble()
            .Concat(new UnicodeEncoding(true, true).GetBytes("ok")).ToArray();
        var (text, format) = TextFileCodec.Decode(bytes);
        Assert.Equal("ok", text);
        Assert.Equal("UTF-16 BE", format.DisplayName);
    }

    [Fact]
    public void NewDocument_DefaultIsUtf8NoBom()
    {
        Assert.False(TextFileFormat.Utf8NoBom.HasBom);
        Assert.Empty(TextFileCodec.Encode(string.Empty, TextFileFormat.Utf8NoBom));
    }

    // ---- The rest of PRD §7.1's decision tree: validate, fall back, open read-only ----

    [Fact]
    public void A_bom_is_a_declaration_and_nothing_is_guessed()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("hi")).ToArray();
        var read = TextFileCodec.Read(bytes);

        Assert.Equal(TextDecodeConfidence.Declared, read.Confidence);
        Assert.False(read.IsReadOnly);
    }

    [Fact]
    public void No_bom_but_valid_utf8_is_checked_not_assumed()
    {
        var read = TextFileCodec.Read(Encoding.UTF8.GetBytes("héllo — ok"));

        Assert.Equal("héllo — ok", read.Text);
        Assert.Equal(TextDecodeConfidence.ValidUtf8, read.Confidence);
        Assert.False(read.IsReadOnly);
    }

    [Fact]
    public void Bytes_that_are_not_utf8_open_read_only_rather_than_being_mangled()
    {
        // "café" in Latin-1: the 0xE9 is not a legal UTF-8 lead byte in this position.
        var bytes = Encoding.Latin1.GetBytes("café");
        var read = TextFileCodec.Read(bytes);

        Assert.Equal(TextDecodeConfidence.Guessed, read.Confidence);
        Assert.True(read.IsReadOnly);
        Assert.DoesNotContain('�', read.Text); // no replacement characters: nothing was substituted
    }

    [Fact]
    public void An_undecodable_file_still_round_trips_byte_for_byte()
    {
        // The whole reason the fallback is Latin-1: it is total, so even a wrong *reading* of the bytes
        // writes the same bytes back. The guess can be corrected later; a lossy read cannot be.
        var bytes = Enumerable.Range(0, 256).Select(b => (byte)b).ToArray();
        var read = TextFileCodec.Read(bytes);

        Assert.Equal(TextDecodeConfidence.Guessed, read.Confidence);
        Assert.Equal(bytes, TextFileCodec.Encode(read.Text, read.Format));
    }

    [Fact]
    public void Choosing_an_encoding_turns_the_guess_into_a_declaration()
    {
        var bytes = Encoding.Latin1.GetBytes("café");
        var chosen = TextFileCodec.ReadAs(bytes, TextFileFormat.Latin1);

        Assert.Equal("café", chosen.Text);
        Assert.Equal(TextDecodeConfidence.Declared, chosen.Confidence);
        Assert.False(chosen.IsReadOnly); // and so saving is unblocked
    }

    [Fact]
    public void An_empty_file_is_ordinary_utf8()
    {
        var read = TextFileCodec.Read([]);

        Assert.Equal(string.Empty, read.Text);
        Assert.False(read.IsReadOnly);
    }
}
