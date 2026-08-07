using System.Text;

namespace Puffinote.Text.Document;

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

    /// <summary>
    /// Latin-1, the fallback for bytes that are not UTF-8. Chosen because it is the one single-byte
    /// encoding that is <b>total</b>: every byte 0x00–0xFF maps to a code point and back, so a file
    /// opened this way round-trips byte-for-byte even where the guess about what those bytes *mean*
    /// is wrong. A guess that is merely wrong shows mojibake; a guess that is lossy destroys the file.
    /// </summary>
    public static TextFileFormat Latin1 { get; } = new(Encoding.Latin1, false, "Latin-1");
}

/// <summary>
/// How much of a file survived being read (PRD §7.1's encoding decision tree).
/// </summary>
public enum TextDecodeConfidence
{
    /// <summary>A byte-order mark said what the encoding was. Nothing was guessed.</summary>
    Declared,

    /// <summary>No BOM, and the bytes are valid UTF-8 — which almost nothing else is by accident.</summary>
    ValidUtf8,

    /// <summary>
    /// Not UTF-8. The text was recovered through a total single-byte fallback, so it round-trips —
    /// but which characters those bytes stand for is a guess. A document opened at this confidence
    /// is <b>read-only</b> until a person confirms an encoding: saving it would write the guess back.
    /// </summary>
    Guessed,
}

/// <summary>A decoded file: its text, the format to write it back with, and how much was guessed.</summary>
/// <param name="Text">The decoded text.</param>
/// <param name="Format">The format a save must use to round-trip the file.</param>
/// <param name="Confidence">How the format was arrived at.</param>
public readonly record struct DecodedText(string Text, TextFileFormat Format, TextDecodeConfidence Confidence)
{
    /// <summary>
    /// True when the encoding was guessed rather than established, so a save would write the guess
    /// back over the original bytes. A shell opens these read-only and offers an encoding to confirm.
    /// </summary>
    public bool IsReadOnly => Confidence == TextDecodeConfidence.Guessed;
}

/// <summary>
/// Reads a document's bytes into text and remembers how to write them back — PRD §7.1's decision
/// tree, in full: a BOM is believed; without one the bytes are <i>validated</i> as UTF-8 rather than
/// assumed to be; and bytes that are not UTF-8 fall back to a total single-byte encoding and open
/// read-only rather than being silently rewritten.
///
/// <para>The read-only part is the point. .NET's default UTF-8 decoder replaces every byte it cannot
/// make sense of with U+FFFD, so a Latin-1 file opened as UTF-8 loses its accented characters the
/// instant it is saved — quietly, and irreversibly. Refusing to save until someone confirms the
/// encoding is the difference between a file that looks wrong and a file that <i>is</i> wrong.</para>
///
/// <para>Pure (operates on byte arrays, no filesystem) so the shell owns the actual file I/O while
/// this stays unit-testable.</para>
/// </summary>
public static class TextFileCodec
{
    /// <summary>Decode <paramref name="bytes"/>, returning the text and the format to save it back with.</summary>
    public static (string Text, TextFileFormat Format) Decode(byte[] bytes)
    {
        var result = Read(bytes);
        return (result.Text, result.Format);
    }

    /// <summary>
    /// Decode <paramref name="bytes"/> with the full decision tree, reporting how much was guessed.
    /// </summary>
    public static DecodedText Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        // A byte-order mark is a declaration, not a hint: believe it.
        if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
        {
            return Declared(TextFileFormat.Utf8Bom, bytes, 3);
        }

        if (StartsWith(bytes, 0xFF, 0xFE))
        {
            return Declared(TextFileFormat.Utf16Le, bytes, 2);
        }

        if (StartsWith(bytes, 0xFE, 0xFF))
        {
            return Declared(TextFileFormat.Utf16Be, bytes, 2);
        }

        // No BOM: assume UTF-8, but *check*. A throwing decoder is what makes this a validation
        // rather than an assumption — the default one substitutes U+FFFD and reports nothing.
        if (TryStrictDecode(bytes, out string utf8))
        {
            return new DecodedText(utf8, TextFileFormat.Utf8NoBom, TextDecodeConfidence.ValidUtf8);
        }

        // Not UTF-8. Latin-1 reads any byte sequence at all, so the text is recoverable and the file
        // round-trips — but what those bytes mean is a guess, and the document opens read-only.
        return new DecodedText(TextFileFormat.Latin1.Encoding.GetString(bytes), TextFileFormat.Latin1, TextDecodeConfidence.Guessed);
    }

    /// <summary>Re-read <paramref name="bytes"/> as <paramref name="format"/> — the user having chosen it,
    /// which is what turns a guess into a declaration and unlocks saving.</summary>
    public static DecodedText ReadAs(byte[] bytes, TextFileFormat format)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(format);

        int skip = format.HasBom && StartsWith(bytes, format.Encoding.GetPreamble()) ? format.Encoding.GetPreamble().Length : 0;
        return Declared(format, bytes, skip);
    }

    private static DecodedText Declared(TextFileFormat format, byte[] bytes, int skip) =>
        new(format.Encoding.GetString(bytes, skip, bytes.Length - skip), format, TextDecodeConfidence.Declared);

    /// <summary>Decode as UTF-8 with a decoder that <b>throws</b> rather than substituting U+FFFD.</summary>
    private static bool TryStrictDecode(byte[] bytes, out string text)
    {
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
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
