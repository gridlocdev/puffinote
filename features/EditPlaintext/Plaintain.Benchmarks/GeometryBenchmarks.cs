using System.Text;
using BenchmarkDotNet.Attributes;
using Puffinote.Text.Editing;

namespace Plaintain.Benchmarks;

/// <summary>
/// The claim Plaintain's layout rests on (PRD §6, golden rule 2): every row is the same height and every
/// character the same width, so where a line is on screen is arithmetic rather than a measurement — and
/// therefore the same cost on line 700,000 as on line 3.
///
/// <para>That is the claim the 50 MB target was designed against, and it had never been run at 50 MB.
/// What these measure is the small set of document operations the surface performs per frame: the offset
/// the caret is at into a line and column, and the text of the line about to be drawn. If any of them
/// scales with document size, the geometry is arithmetic over a lookup that isn't, and scrolling to the
/// end of a large log is where it shows.</para>
/// </summary>
[MemoryDiagnoser]
public class GeometryBenchmarks
{
    [Params(1_000_000, 50_000_000)]
    public int Bytes;

    private DocumentBuffer _buffer = null!;
    private int _lastLine;
    private int _lastOffset;

    [GlobalSetup]
    public void Setup()
    {
        var line = "2026-08-09T12:34:56Z  worker[42]  request finished in 12ms — café ☃\n";
        var builder = new StringBuilder(Bytes + line.Length);
        while (builder.Length < Bytes)
        {
            builder.Append(line);
        }

        _buffer = new DocumentBuffer(builder.ToString());
        _lastLine = _buffer.Document.LineCount - 1;
        _lastOffset = _buffer.Document.Length - 1;
    }

    /// <summary>Where the caret is, at the far end of the document — a per-frame call while scrolled there.</summary>
    [Benchmark]
    public int PositionAtTheEnd() => _buffer.Document.GetPosition(_lastOffset).Line;

    /// <summary>Where the caret is at the start, for comparison. These two should not differ.</summary>
    [Benchmark(Baseline = true)]
    public int PositionAtTheStart() => _buffer.Document.GetPosition(0).Line;

    /// <summary>Reading a line to draw it, from the end of the file.</summary>
    [Benchmark]
    public int LineTextAtTheEnd() => _buffer.Document.GetLineText(_lastLine).Length;

    /// <summary>One screenful of lines from the middle — what a redraw after a scroll actually does.</summary>
    [Benchmark]
    public int OneScreenfulFromTheMiddle()
    {
        int total = 0;
        int from = _lastLine / 2;
        for (int line = from; line < from + 60 && line <= _lastLine; line++)
        {
            total += _buffer.Document.GetLineText(line).Length;
        }

        return total;
    }
}
