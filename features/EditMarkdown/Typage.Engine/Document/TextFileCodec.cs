using System.Text;

namespace Typage.Engine.Document;

/// <summary>
/// The encoding a document was read with, so a save can round-trip it byte-for-byte (PRD §7.1,
/// golden rule #5). Carries the configured <see cref="Encoding"/> (its BOM-emission matches
/// <see cref="HasBom"/>) and a short <see cref="DisplayName"/> for the status bar.
/// </summary>
public sealed class TextFileFormat
{
    public TextFileFormat(Encoding encoding, bool hasBom, string displayName)
    {
        Encoding = encoding;
        HasBom = hasBom;
        DisplayName = displayName;
    }

    public Encoding Encoding { get; }

    public bool HasBom { get; }

    public string DisplayName { get; }

    /// <summary>The default for new documents: UTF-8 without a BOM (PRD §7.1).</summary>
    public static TextFileFormat Utf8NoBom { get; } = new(new UTF8Encoding(false, false), false, "UTF-8");

    public static TextFileFormat Utf8Bom { get; } = new(new UTF8Encoding(true, false), true, "UTF-8 BOM");

    public static TextFileFormat Utf16Le { get; } = new(new UnicodeEncoding(false, true), true, "UTF-16 LE");

    public static TextFileFormat Utf16Be { get; } = new(new UnicodeEncoding(true, true), true, "UTF-16 BE");
}

/// <summary>
/// Detects a document's encoding from its byte-order mark and round-trips it. Pure (operates on
/// byte arrays, no filesystem) so the shell owns the actual file I/O while this stays unit-testable.
/// The full PRD §7.1 decision tree (validate UTF-8, open read-only on undecodable input, fallback
/// encodings) is a later phase; this preserves what it can rather than silently rewriting bytes.
/// </summary>
public static class TextFileCodec
{
    /// <summary>Decode <paramref name="bytes"/>, returning the text and the format to save it back with.</summary>
    public static (string Text, TextFileFormat Format) Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
        {
            return (TextFileFormat.Utf8Bom.Encoding.GetString(bytes, 3, bytes.Length - 3), TextFileFormat.Utf8Bom);
        }

        if (StartsWith(bytes, 0xFF, 0xFE))
        {
            return (TextFileFormat.Utf16Le.Encoding.GetString(bytes, 2, bytes.Length - 2), TextFileFormat.Utf16Le);
        }

        if (StartsWith(bytes, 0xFE, 0xFF))
        {
            return (TextFileFormat.Utf16Be.Encoding.GetString(bytes, 2, bytes.Length - 2), TextFileFormat.Utf16Be);
        }

        return (TextFileFormat.Utf8NoBom.Encoding.GetString(bytes), TextFileFormat.Utf8NoBom);
    }

    /// <summary>Encode <paramref name="text"/> with <paramref name="format"/>, emitting its BOM when present.</summary>
    public static byte[] Encode(string text, TextFileFormat format)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(format);

        var body = format.Encoding.GetBytes(text);
        if (!format.HasBom)
        {
            return body;
        }

        var preamble = format.Encoding.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    private static bool StartsWith(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }
}
