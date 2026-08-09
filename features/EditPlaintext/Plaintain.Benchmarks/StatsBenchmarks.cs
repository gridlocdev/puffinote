using System.Text;
using BenchmarkDotNet.Attributes;
using Puffinote.Text.Editing;

namespace Plaintain.Benchmarks;

/// <summary>
/// What a keystroke costs, which is not the edit — it is the status bar.
///
/// <para>The edit itself is a piece-table splice and is O(1)-ish. But the status bar shows line and
/// character counts and the line-ending style, and <see cref="DocumentBuffer.Stats"/> caches its answer
/// only until the next change: so every keystroke recomputes it, and computing it materialises the whole
/// document as one string and walks it several times. On a 50 MB log that is the difference between
/// typing and waiting, and it is invisible in every test because tests do not type twice.</para>
///
/// <para>Measured here because it sits on the same path the load benchmarks measure the front of, and
/// because "the geometry is arithmetic" (ARCHITECTURE §3.3) is true of the drawing and says nothing about
/// what the chrome asks for per frame.</para>
/// </summary>
[MemoryDiagnoser]
public class StatsBenchmarks
{
    [Params(1_000_000, 50_000_000)]
    public int Bytes;

    private string _text = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        var line = "2026-08-09T12:34:56Z  worker[42]  request finished in 12ms — café ☃\n";
        var builder = new StringBuilder(Bytes + line.Length);
        while (builder.Length < Bytes)
        {
            builder.Append(line);
        }

        _text = builder.ToString();
    }

    /// <summary>
    /// One keystroke, as the app performs it: insert a character, then read the stats the status bar
    /// needs. A fresh buffer per iteration so the stats cache is cold exactly as it is after an edit.
    /// </summary>
    [Benchmark]
    public int TypeOneCharacterThenReadTheStatusBar()
    {
        var buffer = new DocumentBuffer(_text);
        buffer.Insert("x");
        return buffer.Stats.Lines;
    }

    /// <summary>The same keystroke without asking for the stats — what the edit alone costs.</summary>
    [Benchmark(Baseline = true)]
    public int TypeOneCharacter()
    {
        var buffer = new DocumentBuffer(_text);
        buffer.Insert("x");
        return buffer.Document.LineCount;
    }
}
