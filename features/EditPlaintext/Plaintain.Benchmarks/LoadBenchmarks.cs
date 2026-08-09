using System.Text;
using BenchmarkDotNet.Attributes;
using Puffinote.Text.Document;
using Puffinote.Text.Editing;

namespace Plaintain.Benchmarks;

/// <summary>
/// What it costs to open a file, up to and including 50 MB — the number proposal 0003 §9.5 says to
/// measure before optimising anything, and the number Plaintain's whole geometry was designed around.
///
/// <para>The stages are separated on purpose, because they fail differently. Decoding is a full pass over
/// the bytes with a <em>throwing</em> UTF-8 decoder (the validation that makes "assume UTF-8" a check
/// rather than an assumption, PRD §4) and it allocates the whole string. Building the buffer computes the
/// line-start index, which is the second full pass and the one everything after it is O(1) against. Only
/// then is there a document to draw.</para>
///
/// <para>Nothing here is asserted. A benchmark that fails a threshold is a test in the wrong project;
/// this exists so the number is known, which is what "measure before optimising" needs and what nobody
/// had.</para>
/// </summary>
[MemoryDiagnoser]
public class LoadBenchmarks
{
    /// <summary>1 MB is a large log someone opens without thinking; 50 MB is the design target.</summary>
    [Params(1_000_000, 50_000_000)]
    public int Bytes;

    private byte[] _utf8 = [];
    private byte[] _latin1Early = [];
    private byte[] _latin1Late = [];
    private string _text = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        // Log-shaped: many short-to-middling lines, some non-ASCII, which is what actually gets opened in
        // a plaintext editor. A file of one enormous line is a different benchmark and a different bug.
        var line = "2026-08-09T12:34:56Z  worker[42]  request finished in 12ms — café ☃\n";
        var builder = new StringBuilder(Bytes + line.Length);
        while (builder.Length < Bytes)
        {
            builder.Append(line);
        }

        _text = builder.ToString();
        _utf8 = Encoding.UTF8.GetBytes(_text);

        // The fallback path: bytes that are not valid UTF-8, so the strict decoder throws and the file is
        // decoded again through Latin-1. *Where* it throws is the whole story, so both ends are measured.
        // Early — a real Latin-1 log, whose first accented character is in the first line — costs almost
        // nothing for the failed pass. Late is the actual worst case: a full validating pass over the
        // whole file that then has to be thrown away and done again.
        _latin1Early = Encoding.Latin1.GetBytes(_text);
        _latin1Late = [.. Encoding.ASCII.GetBytes(_text.Replace("café ☃", "cafe", StringComparison.Ordinal)), 0xE9];
    }

    /// <summary>Decode alone: the validating UTF-8 pass, and the string it allocates.</summary>
    [Benchmark(Baseline = true)]
    public int DecodeUtf8() => TextFileCodec.Read(_utf8).Text.Length;

    /// <summary>A real Latin-1 file: strict UTF-8 fails in the first line, so almost nothing is wasted.</summary>
    [Benchmark]
    public int DecodeLatin1ThatFailsEarly() => TextFileCodec.Read(_latin1Early).Text.Length;

    /// <summary>
    /// The other end: ASCII all the way to the last byte, so the validating pass reads the whole file
    /// before finding out it has to be read again. Expected to be the expensive one and measurably is
    /// not — a Latin-1 decode is a byte-widening where a UTF-8 decode is a transcode, so even two passes
    /// of it come in under one successful UTF-8 read. Which is the sort of thing measuring is for.
    /// </summary>
    [Benchmark]
    public int DecodeLatin1ThatFailsAtTheVeryEnd() => TextFileCodec.Read(_latin1Late).Text.Length;

    /// <summary>Building the buffer over already-decoded text — where the line-start index is computed.</summary>
    [Benchmark]
    public int BuildBuffer() => new DocumentBuffer(_text).Document.LineCount;

    /// <summary>Bytes to a document ready to draw: what pressing Open actually costs.</summary>
    [Benchmark]
    public int OpenEndToEnd() => new DocumentBuffer(TextFileCodec.Read(_utf8).Text).Document.LineCount;
}
