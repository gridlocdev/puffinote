using BenchmarkDotNet.Attributes;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;
using Cellestra.Core.Values;

namespace Cellestra.Benchmarks;

/// <summary>
/// File I/O against the PRD §3 target: a 10,000-filled-cell sheet opens in under 500 ms.
/// </summary>
[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private byte[] _odsBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        for (int row = 0; row < 100; row++)
        {
            for (int column = 0; column < 100; column++)
            {
                sheet.SetValue(row, column, CellValue.Number(row * 100 + column));
            }
        }

        _odsBytes = OdsWriter.WriteToBytes(workbook);
    }

    [Benchmark]
    public Workbook Open10000Cells()
    {
        using var stream = new MemoryStream(_odsBytes);
        return OdsReader.Read(stream);
    }
}
