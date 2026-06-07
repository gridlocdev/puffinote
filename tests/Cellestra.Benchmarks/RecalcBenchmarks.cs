using BenchmarkDotNet.Attributes;
using Cellestra.Core.Dependencies;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Benchmarks;

/// <summary>
/// Recalculation throughput against the PRD §3 target: a full recalculation of 10,000
/// dependent cells in under 100 ms (Apple Silicon).
/// </summary>
[MemoryDiagnoser]
public class RecalcBenchmarks
{
    private const int Count = 10_000;

    private Workbook _chain = null!;
    private RecalculationEngine _engine = null!;

    [GlobalSetup]
    public void Setup()
    {
        _chain = Workbook.CreateNew();
        var sheet = _chain.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(1));
        for (int i = 1; i < Count; i++)
        {
            // Each cell depends on the one above: a worst-case dependency chain.
            sheet.SetFormula(i, 0, $"A{i}+1", CellValue.Empty);
        }

        _engine = new RecalculationEngine(_chain);
        _engine.RebuildGraph();
    }

    [Benchmark]
    public void RecalculateChainOf10000() => _engine.RecalculateAll();
}
