using System.Text;
using Typage.Engine.Document;
using Xunit;

namespace Typage.Engine.Tests.Document;

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
}
