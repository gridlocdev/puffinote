using BenchmarkDotNet.Attributes;
using Puffinote.Text.Document;
using Typage.Engine.Markdown;

namespace Typage.Benchmarks;

/// <summary>
/// The P2 incremental tokenizer (PRD §8.2): re-running <see cref="MarkdownModel.Update"/> from the
/// first changed line should cost a fraction of a full <see cref="MarkdownModel.Build"/>. This is
/// what keeps typing in a large document responsive — every keystroke retokenizes from the edit down.
/// </summary>
[MemoryDiagnoser]
public class TokenizerBenchmarks
{
    private TextDocument _doc = null!;
    private MarkdownModel _model = null!;
    private int _midLine;

    [Params(2_000, 20_000)]
    public int Paragraphs;

    [GlobalSetup]
    public void Setup()
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < Paragraphs; i++)
        {
            sb.Append("## Section ").Append(i).Append('\n')
              .Append("Some **bold**, *italic*, `code`, [a link](https://example.com), and plain text.\n\n");
        }

        _doc = TextDocument.FromString(sb.ToString());
        _model = MarkdownModel.Build(_doc);
        _midLine = _doc.LineCount / 2;
    }

    [Benchmark(Baseline = true)]
    public MarkdownModel FullRebuild() => MarkdownModel.Build(_doc);

    [Benchmark]
    public MarkdownModel IncrementalUpdate() => _model.Update(_doc, _midLine, out _);
}
