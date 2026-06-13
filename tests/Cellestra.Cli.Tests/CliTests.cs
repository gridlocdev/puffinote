using System;
using System.IO;
using Cellestra.Cli;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Cli.Tests;

/// <summary>
/// Drives the CLI in-process. All cases live in one class so they run sequentially — the helper
/// redirects the process-global Console streams to capture output.
/// </summary>
public sealed class CliTests : IDisposable
{
    private readonly string _dir;

    public CliTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cellestra-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Path2(string name) => System.IO.Path.Combine(_dir, name);

    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var previousOut = Console.Out;
        var previousErr = Console.Error;
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try
        {
            int exit = CliApplication.Run(args);
            return (exit, outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousErr);
        }
    }

    [Fact]
    public void Eval_computes_a_formula()
    {
        var (exit, output, _) = Run("eval", "=ROUND(355/113, 4)", "--locale", "en-US");
        Assert.Equal(0, exit);
        Assert.Equal("3.1416", output.Trim());
    }

    [Fact]
    public void Convert_csv_to_ods_round_trips_a_value()
    {
        string csv = Path2("in.csv");
        string ods = Path2("out.ods");
        File.WriteAllText(csv, "Name,Score\nAda,90\nBev,75\n");

        Assert.Equal(0, Run("convert", csv, ods, "--sheet", "Imported").Exit);

        var (exit, output, _) = Run("get", ods, "Imported.B2");
        Assert.Equal(0, exit);
        Assert.Equal("90", output.Trim());
    }

    [Fact]
    public void Set_writes_a_formula_and_get_reads_back_value_and_source()
    {
        string ods = Path2("book.ods");
        Run("new", ods);
        Run("set", ods, "Sheet1.A1", "10", "--in-place");
        Run("set", ods, "Sheet1.A2", "5", "--in-place");
        Assert.Equal(0, Run("set", ods, "Sheet1.A3", "=A1+A2", "--in-place").Exit);

        Assert.Equal("15", Run("get", ods, "Sheet1.A3").Out.Trim());
        Assert.Equal("=A1+A2", Run("get", ods, "Sheet1.A3", "--formula").Out.Trim());
    }

    [Fact]
    public void Set_refuses_to_write_without_in_place_or_output()
    {
        string ods = Path2("book.ods");
        Run("new", ods);
        var (exit, _, err) = Run("set", ods, "Sheet1.A1", "1");
        Assert.Equal(1, exit);
        Assert.Contains("--in-place", err, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_passes_clean_and_fails_on_errors()
    {
        string ods = Path2("book.ods");
        Run("new", ods);
        Run("set", ods, "Sheet1.A1", "1", "--in-place");
        Assert.Equal(0, Run("validate", ods).Exit);

        Run("set", ods, "Sheet1.A2", "=1/0", "--in-place");
        var (exit, _, err) = Run("validate", ods);
        Assert.Equal(1, exit);
        Assert.Contains("#DIV/0!", err, StringComparison.Ordinal);
    }

    [Fact]
    public void Functions_lists_and_describes()
    {
        Assert.Contains("SUM", Run("functions").Out, StringComparison.Ordinal);

        var (exit, output, _) = Run("functions", "VLOOKUP");
        Assert.Equal(0, exit);
        Assert.Contains("VLOOKUP(", output, StringComparison.Ordinal);
        Assert.Contains("Example:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_exits_nonzero_when_workbooks_differ()
    {
        string a = Path2("a.ods");
        string b = Path2("b.ods");
        Run("new", a);
        Run("set", a, "Sheet1.A1", "1", "--in-place");
        Run("convert", a, b); // identical copy
        Assert.Equal(0, Run("diff", a, b).Exit);

        Run("set", b, "Sheet1.A1", "2", "--in-place");
        var (exit, output, _) = Run("diff", a, b);
        Assert.Equal(1, exit);
        Assert.Contains("Sheet1.A1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_runs_a_batch_of_edits()
    {
        string ods = Path2("book.ods");
        string edits = Path2("edits.json");
        Run("new", ods);
        File.WriteAllText(edits, "[{\"cell\":\"Sheet1.A1\",\"value\":\"=2+3\"},{\"cell\":\"Sheet1.A2\",\"value\":\"hi\"}]");

        Assert.Equal(0, Run("apply", ods, edits).Exit);
        Assert.Equal("5", Run("get", ods, "Sheet1.A1").Out.Trim());
        Assert.Equal("hi", Run("get", ods, "Sheet1.A2").Out.Trim());
    }

    [Fact]
    public void Edits_preserve_unmodeled_content()
    {
        string ods = Path2("withchart.ods");

        // Build a valid .ods, then inject an unmodeled part (as a chart/image would be).
        var workbook = Workbook.CreateNew();
        workbook.ActiveSheet.SetValue(0, 0, CellValue.Number(1));
        var package = OdsPackage.Read(new MemoryStream(OdsWriter.WriteToBytes(workbook)));
        package.SetBytes("Pictures/chart.bin", new byte[] { 1, 2, 3, 4 });
        using (var stream = File.Create(ods))
        {
            package.Write(stream);
        }

        // A CLI edit must keep the unmodeled part verbatim (PRD §8.1).
        Assert.Equal(0, Run("set", ods, "Sheet1.A1", "42", "--in-place").Exit);

        using var reloaded = File.OpenRead(ods);
        var after = OdsPackage.Read(reloaded);
        Assert.True(after.Contains("Pictures/chart.bin"));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, after.GetBytes("Pictures/chart.bin"));
    }

    [Fact]
    public void Stats_aggregates_a_range()
    {
        string ods = Path2("book.ods");
        Run("new", ods);
        Run("set", ods, "Sheet1.A1", "10", "--in-place");
        Run("set", ods, "Sheet1.A2", "20", "--in-place");
        Run("set", ods, "Sheet1.A3", "30", "--in-place");

        var (exit, output, _) = Run("stats", ods, "Sheet1.A1:A3");
        Assert.Equal(0, exit);
        Assert.Contains("sum\t60", output, StringComparison.Ordinal);
        Assert.Contains("average\t20", output, StringComparison.Ordinal);
        Assert.Contains("max\t30", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("get", "/no/such/file.ods", "A1")]                 // missing file (IOException)
    [InlineData("add-sheet", null, "Sheet1")]                       // duplicate sheet (InvalidOperationException)
    [InlineData("rename-sheet", null, "Sheet1", "bad/name")]        // invalid name (ArgumentException)
    public void User_errors_report_cleanly_and_exit_nonzero(params string?[] template)
    {
        // For cases that need a real workbook, "null" in the template is replaced by a fresh file.
        string ods = Path2("book.ods");
        Run("new", ods);
        var args = template.Select(a => a ?? ods).ToArray();

        var (exit, _, err) = Run(args!);

        Assert.Equal(1, exit);
        Assert.StartsWith("cellestra:", err.Trim(), StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", err, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", err, StringComparison.Ordinal); // no stack trace
    }

    [Fact]
    public void Invalid_regex_reports_cleanly()
    {
        string ods = Path2("book.ods");
        Run("new", ods);
        var (exit, _, err) = Run("find", ods, "[", "--regex");
        Assert.Equal(1, exit);
        Assert.Contains("Invalid regex", err, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", err, StringComparison.Ordinal);
    }

    [Fact]
    public void Stats_ignores_empty_cells_in_a_large_range()
    {
        string ods = Path2("book.ods");
        Run("new", ods);
        Run("set", ods, "Sheet1.A1", "10", "--in-place");
        Run("set", ods, "Sheet1.A500", "40", "--in-place");

        // A wide range with only two populated cells — correctness must not change with the
        // sparse-iteration optimization.
        var (exit, output, _) = Run("stats", ods, "Sheet1.A1:A1000");
        Assert.Equal(0, exit);
        Assert.Contains("count\t2", output, StringComparison.Ordinal);
        Assert.Contains("sum\t50", output, StringComparison.Ordinal);
        Assert.Contains("min\t10", output, StringComparison.Ordinal);
        Assert.Contains("max\t40", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Cat_csv_format_is_pipe_friendly()
    {
        string ods = Path2("book.ods");
        Run("new", ods);
        Run("set", ods, "Sheet1.A1", "x", "--in-place");
        Run("set", ods, "Sheet1.B1", "y", "--in-place");

        var (exit, output, _) = Run("cat", ods, "--range", "A1:B1", "--format", "csv");
        Assert.Equal(0, exit);
        Assert.Equal("x,y", output.Trim());
    }

    // ---- Data, structural, and layout edits (desktop parity) --------------

    private string ThreeRowBook()
    {
        string ods = Path2("data.ods");
        Run("new", ods);
        Run("set", ods, "A1", "Name", "--in-place");
        Run("set", ods, "B1", "Score", "--in-place");
        Run("set", ods, "A2", "Beta", "--in-place");
        Run("set", ods, "B2", "2", "--in-place");
        Run("set", ods, "A3", "Alpha", "--in-place");
        Run("set", ods, "B3", "1", "--in-place");
        return ods;
    }

    [Fact]
    public void Sort_orders_rows_by_a_column_keeping_the_header()
    {
        string ods = ThreeRowBook();
        Assert.Equal(0, Run("sort", ods, "A1:B3", "--by", "A", "--in-place").Exit);

        Assert.Equal("Name", Run("get", ods, "A1").Out.Trim());   // header stays put
        Assert.Equal("Alpha", Run("get", ods, "A2").Out.Trim());  // sorted ascending
        Assert.Equal("Beta", Run("get", ods, "A3").Out.Trim());
    }

    [Fact]
    public void Sort_descending_reverses_a_numeric_column()
    {
        string ods = ThreeRowBook();
        Run("sort", ods, "A1:B3", "--by", "B:desc", "--in-place");
        Assert.Equal("2", Run("get", ods, "B2").Out.Trim());
        Assert.Equal("1", Run("get", ods, "B3").Out.Trim());
    }

    [Fact]
    public void Remove_duplicates_drops_repeated_rows_and_reports_count()
    {
        string ods = ThreeRowBook();
        Run("set", ods, "A4", "Beta", "--in-place");
        Run("set", ods, "B4", "2", "--in-place"); // duplicate of row 2

        var (exit, _, err) = Run("remove-duplicates", ods, "A1:B4", "--in-place");
        Assert.Equal(0, exit);
        Assert.Contains("Removed 1", err, StringComparison.Ordinal);
        Assert.Equal("", Run("get", ods, "A4").Out.Trim()); // the duplicate row was cleared
    }

    [Fact]
    public void Split_separates_a_delimited_column()
    {
        string ods = Path2("split.ods");
        Run("new", ods);
        Run("set", ods, "A1", "a;b;c", "--in-place");

        Assert.Equal(0, Run("split", ods, "A1:A1", "-d", ";", "--in-place").Exit);
        Assert.Equal("a", Run("get", ods, "A1").Out.Trim());
        Assert.Equal("b", Run("get", ods, "B1").Out.Trim());
        Assert.Equal("c", Run("get", ods, "C1").Out.Trim());
    }

    [Fact]
    public void Table_creation_labels_empty_headers_and_round_trips()
    {
        string ods = Path2("table.ods");
        Run("new", ods);
        Run("set", ods, "A2", "1", "--in-place");

        Assert.Equal(0, Run("table", ods, "A1:B2", "--in-place").Exit);
        var reopened = OdsReader.Read(File.OpenRead(ods)).FindSheet("Sheet1")!;
        Assert.Single(reopened.Tables);
        Assert.Equal("Column 1", reopened.GetValue(0, 0).AsText); // placeholder header filled in
    }

    [Fact]
    public void Data_validation_persists_a_list_rule()
    {
        string ods = Path2("dv.ods");
        Run("new", ods);
        Assert.Equal(0, Run("data-validation", ods, "A1:A5", "--list", "Red,Green,Blue", "--in-place").Exit);

        var reopened = OdsReader.Read(File.OpenRead(ods)).FindSheet("Sheet1")!;
        var rule = Assert.Single(reopened.Validations);
        Assert.Equal(new[] { "Red", "Green", "Blue" }, rule.ListValues!);
    }

    [Fact]
    public void Data_validation_persists_a_range_source_rule()
    {
        string ods = Path2("dvr.ods");
        Run("new", ods);
        Assert.Equal(0, Run("data-validation", ods, "A1:A5", "--source", "E1:E8", "--in-place").Exit);

        var reopened = OdsReader.Read(File.OpenRead(ods)).FindSheet("Sheet1")!;
        var rule = Assert.Single(reopened.Validations);
        Assert.Null(rule.ListValues);
        Assert.NotNull(rule.ListSource);
    }

    [Fact]
    public void Named_range_add_list_and_remove()
    {
        string ods = Path2("names.ods");
        Run("new", ods);
        Assert.Equal(0, Run("name", "add", ods, "TaxRate", "B1:B10", "--in-place").Exit);

        Assert.Contains("TaxRate", Run("name", "list", ods).Out, StringComparison.Ordinal);
        Assert.Equal(0, Run("name", "remove", ods, "TaxRate", "--in-place").Exit);
        Assert.DoesNotContain("TaxRate", Run("name", "list", ods).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Hyperlink_sets_and_removes_a_link_without_mangling_the_value()
    {
        string ods = Path2("link.ods");
        Run("new", ods);
        Run("set", ods, "A1", "docs", "--in-place");
        Assert.Equal(0, Run("hyperlink", ods, "A1", "https://example.com", "--in-place").Exit);

        // The visible value is untouched (regression: a hyperlinked cell used to reopen wrapped in
        // pretty-print whitespace).
        Assert.Equal("docs", Run("get", ods, "A1").Out.Trim());
        var reopened = OdsReader.Read(File.OpenRead(ods)).FindSheet("Sheet1")!;
        Assert.Equal("https://example.com", reopened.Find(0, 0)!.Hyperlink!.Target);

        Run("hyperlink", ods, "A1", "--remove", "--in-place");
        var cleared = OdsReader.Read(File.OpenRead(ods)).FindSheet("Sheet1")!;
        Assert.Null(cleared.Find(0, 0)!.Hyperlink);
    }

    [Fact]
    public void Insert_and_delete_rows_shift_data()
    {
        string ods = ThreeRowBook();
        Run("insert-rows", ods, "2", "--in-place"); // a blank row 2 pushes Beta down to row 3
        Assert.Equal("", Run("get", ods, "A2").Out.Trim());
        Assert.Equal("Beta", Run("get", ods, "A3").Out.Trim());

        Run("delete-rows", ods, "2", "--in-place"); // remove it again
        Assert.Equal("Beta", Run("get", ods, "A2").Out.Trim());
    }

    [Fact]
    public void Insert_columns_accepts_a_letter()
    {
        string ods = ThreeRowBook();
        Run("insert-cols", ods, "A", "--in-place"); // a blank column A shifts Name to B
        Assert.Equal("Name", Run("get", ods, "B1").Out.Trim());
    }

    [Fact]
    public void Hide_and_show_columns_round_trip_visibility()
    {
        string ods = ThreeRowBook();
        Assert.Equal(0, Run("hide-cols", ods, "B", "--in-place").Exit);
        Assert.True(OdsReader.Read(File.OpenRead(ods)).FindSheet("Sheet1")!.IsColumnHidden(1));

        Assert.Equal(0, Run("show-cols", ods, "B", "--in-place").Exit);
        Assert.False(OdsReader.Read(File.OpenRead(ods)).FindSheet("Sheet1")!.IsColumnHidden(1));
    }

    [Fact]
    public void Merge_and_unmerge_a_region()
    {
        string ods = Path2("merge.ods");
        Run("new", ods);
        Run("set", ods, "A1", "wide", "--in-place");
        Assert.Equal(0, Run("merge", ods, "A1:B2", "--in-place").Exit);
        Assert.NotNull(OdsReader.Read(File.OpenRead(ods)).FindSheet("Sheet1")!.FindMerge(0, 0));

        Assert.Equal(0, Run("unmerge", ods, "A1", "--in-place").Exit);
        Assert.Null(OdsReader.Read(File.OpenRead(ods)).FindSheet("Sheet1")!.FindMerge(0, 0));
    }

    [Fact]
    public void Data_edits_refuse_to_overwrite_without_in_place_or_output()
    {
        string ods = ThreeRowBook();
        var (exit, _, err) = Run("sort", ods, "A1:B3", "--by", "A");
        Assert.Equal(1, exit);
        Assert.Contains("--in-place", err, StringComparison.Ordinal);
    }
}
