using BenchmarkDotNet.Attributes;
using Puffinote.Text.Document;
using Puffinote.Text.Editing;

namespace Typage.Benchmarks;

/// <summary>
/// Caret navigation and offset→position mapping — the O(line) paths that a click, an arrow key, and
/// every redraw/scroll go through (PRD §3, §9.2). They must stay flat as the document grows rather
/// than scanning the whole buffer.
/// </summary>
[MemoryDiagnoser]
public class CaretBenchmarks
{
    private DocumentBuffer _buffer = null!;
    private int _length;

    [Params(50_000, 500_000)]
    public int Chars;

    [GlobalSetup]
    public void Setup()
    {
        var lines = Enumerable.Range(0, Math.Max(1, Chars / 48))
            .Select(i => $"Line {i} with words, unicode é and ☃, and a tail.");
        _buffer = new DocumentBuffer(string.Join('\n', lines));
        _length = _buffer.Document.Length;
    }

    [Benchmark]
    public int WalkForwardByGrapheme()
    {
        var pos = 0;
        var n = 0;
        while (pos < _length)
        {
            pos = _buffer.NextCaret(pos);
            n++;
        }

        return n;
    }

    [Benchmark]
    public LinePosition ScatteredGetPosition()
    {
        LinePosition p = default;
        for (var i = 0; i < 1_000; i++)
        {
            p = _buffer.Document.GetPosition((i * 9_973) % _length); // many lookups, as a redraw does
        }

        return p;
    }
}
