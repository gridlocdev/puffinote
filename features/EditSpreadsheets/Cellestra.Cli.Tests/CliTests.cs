using System.Text.Json;
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

    // ---- --json: one report shape for reads, edits, and checks ------------

    [Fact]
    public void Get_json_reports_a_typed_value_and_the_formula()
    {
        string ods = Path2("book.ods");
        Run("new", ods);
        Run("set", ods, "Sheet1.A1", "10", "--in-place");
        Run("set", ods, "Sheet1.A2", "=A1*2", "--in-place");

        var (exit, output, _) = Run("get", ods, "Sheet1.A2", "--json");
        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal("Sheet1.A2", doc.RootElement.GetProperty("cell").GetString());
        Assert.Equal(20, doc.RootElement.GetProperty("value").GetDouble());
        Assert.Equal("20", doc.RootElement.GetProperty("display").GetString());
        Assert.Equal("=A1*2", doc.RootElement.GetProperty("formula").GetString());
        Assert.False(doc.RootElement.TryGetProperty("error", out _)); // nulls are omitted on the wire
    }

    [Fact]
    public void Set_json_reports_the_changed_cell_and_any_error_it_introduced()
    {
        string ods = Path2("book.ods");
        Run("new", ods);
        var (exit, output, err) = Run("set", ods, "Sheet1.B1", "=SUMM(A1)", "--in-place", "--json");
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, err); // the report is the whole reply, on stdout

        using var doc = JsonDocument.Parse(output);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var changed = doc.RootElement.GetProperty("changed");
        Assert.Equal(1, changed.GetArrayLength());
        Assert.Equal("Sheet1.B1", changed[0].GetProperty("cell").GetString());
        Assert.Equal("#NAME?", changed[0].GetProperty("error").GetString());
        Assert.Equal("Sheet1.B1", doc.RootElement.GetProperty("errors")[0].GetProperty("cell").GetString());
    }

    [Fact]
    public void Validate_json_lists_errors_and_keeps_the_exit_code()
    {
        string ods = Path2("book.ods");
        Run("new", ods);
        Run("set", ods, "Sheet1.A1", "=1/0", "--in-place");

        var (exit, output, _) = Run("validate", ods, "--json");
        Assert.Equal(1, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("#DIV/0!", doc.RootElement.GetProperty("errors")[0].GetProperty("error").GetString());
    }

    [Fact]
    public void Cat_json_carries_the_grid_and_every_populated_cell()
    {
        string ods = Path2("book.ods");
        Run("new", ods);
        Run("set", ods, "Sheet1.A1", "Name", "--in-place");
        Run("set", ods, "Sheet1.B2", "3", "--in-place");

        var (exit, output, _) = Run("cat", ods, "--json");
        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal("A1:B2", doc.RootElement.GetProperty("range").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("rows").GetArrayLength());
        var cells = doc.RootElement.GetProperty("cells");
        Assert.Equal(2, cells.GetArrayLength()); // only populated cells, in row-major order
        Assert.Equal("Name", cells[0].GetProperty("value").GetString());
        Assert.Equal(3, cells[1].GetProperty("value").GetDouble());
    }

    [Fact]
    public void Diff_json_is_structured_and_exits_nonzero_on_a_difference()
    {
        string a = Path2("a.ods");
        string b = Path2("b.ods");
        Run("new", a);
        Run("set", a, "Sheet1.A1", "1", "--in-place");
        Run("convert", a, b);
        Run("set", b, "Sheet1.A1", "2", "--in-place");

        var (exit, output, _) = Run("diff", a, b, "--json");
        Assert.Equal(1, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.False(doc.RootElement.GetProperty("same").GetBoolean());
        var cell = doc.RootElement.GetProperty("cells")[0];
        Assert.Equal("Sheet1.A1", cell.GetProperty("cell").GetString());
        Assert.Equal("1", cell.GetProperty("before").GetString());
        Assert.Equal("2", cell.GetProperty("after").GetString());
    }

    [Fact]
    public void Eval_and_functions_speak_json()
    {
        var (exit, output, _) = Run("eval", "=1+1", "--json");
        Assert.Equal(0, exit);
        using (var doc = JsonDocument.Parse(output))
        {
            Assert.Equal(2, doc.RootElement.GetProperty("value").GetDouble());
        }

        (exit, output, _) = Run("functions", "ABS", "--json");
        Assert.Equal(0, exit);
        using (var doc = JsonDocument.Parse(output))
        {
            Assert.Equal("ABS", doc.RootElement.GetProperty("name").GetString());
            Assert.Equal("number", doc.RootElement.GetProperty("arguments")[0].GetProperty("name").GetString());
        }
    }

    [Fact]
    public void A_user_error_under_json_is_a_json_document_too()
    {
        string ods = Path2("book.ods");
        Run("new", ods);
        var (exit, output, err) = Run("set", ods, "Nope.A1", "1", "--in-place", "--json");
        Assert.Equal(1, exit);
        Assert.StartsWith("cellestra: ", err, StringComparison.Ordinal); // the human line still goes to stderr
        using var doc = JsonDocument.Parse(output);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("Nope", doc.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_runs_a_batch_of_edits()
    {
        string ods = Path2("book.ods");
        string edits = Path2("edits.json");
        Run("new", ods);
        File.WriteAllText(edits, "[{\"cell\":\"Sheet1.A1\",\"value\":\"=2+3\"},{\"cell\":\"Sheet1.A2\",\"value\":\"hi\"}]");

        Assert.Equal(0, Run("apply", ods, edits, "--in-place").Exit);
        Assert.Equal("5", Run("get", ods, "Sheet1.A1").Out.Trim());
        Assert.Equal("hi", Run("get", ods, "Sheet1.A2").Out.Trim());
    }

    [Fact]
    public void Apply_builds_a_sheet_from_typed_ops_in_one_pass()
    {
        string ods = Path2("book.ods");
        string ops = Path2("ops.json");
        Run("new", ods);
        File.WriteAllText(ops, """
            {"ops": [
              {"op":"set", "at":"A1", "values":[["Item","Qty","Price","Total"],["Pen",3,1.5,"=B2*C2"],["Ink",2,4,"=B3*C3"]]},
              {"op":"set", "cell":"D4", "value":"=SUM(D2:D3)"},
              {"op":"style", "range":"A1:D1", "bold":true, "fill":"#EEEEEE", "align":"center"},
              {"op":"style", "range":"C2:D4", "format":"0.00"},
              {"op":"width", "columns":"A", "width":120},
              {"op":"freeze", "rows":1},
              {"op":"add-sheet", "name":"Notes"},
              {"op":"name", "name":"GrandTotal", "range":"Sheet1.D4"},
              {"op":"table", "range":"A1:D3", "name":"Orders"}
            ]}
            """);

        var (exit, output, _) = Run("apply", ods, ops, "--in-place", "--json");
        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(9, doc.RootElement.GetProperty("applied").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("errors").GetArrayLength());
        Assert.Contains(doc.RootElement.GetProperty("changed").EnumerateArray(),
            c => c.GetProperty("cell").GetString() == "Sheet1.D4" && c.GetProperty("value").GetDouble() == 12.5);

        var workbook = OdsReader.Read(File.OpenRead(ods));
        var sheet = workbook.FindSheet("Sheet1")!;
        Assert.Equal(12.5, sheet.GetValue(3, 3).AsNumber);
        Assert.True(workbook.Styles.Get(sheet.Find(0, 0)!.StyleId).Font.Bold);
        Assert.Equal("0.00", workbook.Styles.Get(sheet.Find(1, 2)!.StyleId).NumberFormat.Code);
        Assert.Equal(120, sheet.GetColumnWidth(0));
        Assert.Equal(1, sheet.FrozenRows);
        Assert.NotNull(workbook.FindSheet("Notes"));
        Assert.NotNull(workbook.FindNamedRange("GrandTotal"));
        Assert.Equal("Orders", sheet.Tables[0].Name);
        Assert.Equal("12.50", Run("get", ods, "Sheet1.D4").Out.Trim());
    }

    [Fact]
    public void Apply_accepts_a_bare_array_and_the_original_cell_value_form()
    {
        string ods = Path2("book.ods");
        string ops = Path2("ops.json");
        Run("new", ods);
        File.WriteAllText(ops, "[{\"cell\":\"A1\",\"value\":\"7\"},{\"cell\":\"A2\",\"formula\":\"A1*2\"},{\"op\":\"set\",\"cell\":\"A3\",\"value\":true}]");

        Assert.Equal(0, Run("apply", ods, ops, "--in-place").Exit);
        Assert.Equal("14", Run("get", ods, "A2").Out.Trim());
        Assert.Equal("=A1*2", Run("get", ods, "A2", "--formula").Out.Trim());
        Assert.Equal("TRUE", Run("get", ods, "A3").Out.Trim());
    }

    [Fact]
    public void Apply_dry_run_reports_the_outcome_and_writes_nothing()
    {
        string ods = Path2("book.ods");
        string ops = Path2("ops.json");
        Run("new", ods);
        File.WriteAllText(ops, "[{\"op\":\"set\",\"cell\":\"A1\",\"value\":\"=NOPE(1)\"}]");
        var before = File.ReadAllBytes(ods);

        var (exit, output, _) = Run("apply", ods, ops, "--dry-run", "--json"); // no --in-place needed
        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.True(doc.RootElement.GetProperty("dryRun").GetBoolean());
        Assert.Equal("#NAME?", doc.RootElement.GetProperty("errors")[0].GetProperty("error").GetString());
        Assert.Equal(before, File.ReadAllBytes(ods));
    }

    [Fact]
    public void Apply_aborts_the_whole_batch_on_a_bad_op_and_says_which()
    {
        string ods = Path2("book.ods");
        string ops = Path2("ops.json");
        Run("new", ods);
        File.WriteAllText(ops, "[{\"op\":\"set\",\"cell\":\"A1\",\"value\":1},{\"op\":\"style\",\"range\":\"A1\",\"fill\":\"red\"}]");

        var (exit, _, err) = Run("apply", ods, ops, "--in-place");
        Assert.Equal(1, exit);
        Assert.Contains("op 2 (style)", err, StringComparison.Ordinal);
        Assert.Equal(string.Empty, Run("get", ods, "A1").Out.Trim()); // op 1 was not written either
    }

    [Fact]
    public void Apply_names_the_op_when_core_refuses()
    {
        string ods = Path2("book.ods");
        string ops = Path2("ops.json");
        Run("new", ods);
        File.WriteAllText(ops, "[{\"op\":\"add-sheet\",\"name\":\"Sheet1\"}]"); // duplicate: Core throws InvalidOperationException
        var (exit, _, err) = Run("apply", ods, ops, "--in-place");
        Assert.Equal(1, exit);
        Assert.Contains("op 1 (add-sheet)", err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_parse_error_under_json_is_a_json_document_too()
    {
        var (exit, output, _) = Run("set", "book.ods", "--json"); // missing <cell> and <value>
        Assert.Equal(1, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.NotEmpty(doc.RootElement.GetProperty("error").GetString()!);
    }

    [Fact]
    public void Apply_and_sheet_verbs_refuse_to_overwrite_without_being_told_to()
    {
        string ods = Path2("book.ods");
        string ops = Path2("ops.json");
        Run("new", ods);
        File.WriteAllText(ops, "[]");
        Assert.Contains("--in-place", Run("apply", ods, ops).Err, StringComparison.Ordinal);
        Assert.Contains("--in-place", Run("add-sheet", ods, "Two").Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Man_is_one_page_generated_from_the_live_command_tree()
    {
        var (exit, output, _) = Run("man");
        Assert.Equal(0, exit);
        var root = CliApplication.BuildRoot();
        foreach (var command in root.Subcommands.Where(c => c.Subcommands.Count == 0))
        {
            Assert.Contains($"### cellestra {command.Name}", output, StringComparison.Ordinal);
        }

        Assert.Contains("### cellestra name add", output, StringComparison.Ordinal); // nested verbs are flattened
        Assert.Contains("--in-place", output, StringComparison.Ordinal);
        Assert.Contains("[default: table]", output, StringComparison.Ordinal);
        Assert.Contains("## Exit codes", output, StringComparison.Ordinal);
        Assert.Contains("{\"op\":\"freeze\"", output, StringComparison.Ordinal); // the apply contract is on the page

        (exit, output, _) = Run("man", "--json");
        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.Contains(doc.RootElement.GetProperty("commands").EnumerateArray(), c => c.GetProperty("name").GetString() == "cellestra apply");
    }

    [Fact]
    public void Skill_prints_the_embedded_skill_and_installs_it_into_a_skills_directory()
    {
        var (exit, output, _) = Run("skill");
        Assert.Equal(0, exit);
        Assert.StartsWith("---\nname: cellestra\n", output, StringComparison.Ordinal);
        Assert.Contains("cellestra apply", output, StringComparison.Ordinal);
        Assert.DoesNotContain("AGENTS.md", output, StringComparison.Ordinal); // portable: no repo internals

        string skills = Path2("skills");
        (exit, _, var err) = Run("skill", "--install", "--dir", skills);
        Assert.Equal(0, exit);
        string installed = System.IO.Path.Combine(skills, "cellestra", "SKILL.md");
        Assert.Contains(installed, err, StringComparison.Ordinal);
        Assert.Equal(output, File.ReadAllText(installed));
    }

    [Fact]
    public void Schema_prints_a_contract_and_rejects_an_unknown_one()
    {
        var (exit, output, _) = Run("schema", "apply");
        Assert.Equal(0, exit);
        Assert.Contains("\"op\":\"style\"", output, StringComparison.Ordinal);

        (exit, output, _) = Run("schema", "report");
        Assert.Equal(0, exit);
        Assert.Contains("\"changed\"", output, StringComparison.Ordinal);

        Assert.Equal(1, Run("schema", "nope").Exit);
    }

    [Fact]
    public void Edits_preserve_unmodeled_content()
    {
        string ods = Path2("withchart.ods");

        // Build a valid .ods, then inject an unmodeled part (as a chart/image would be).
        var workbook = Workbook.CreateNew();
        workbook.ActiveSheet.SetValue(0, 0, CellValue.Number(1));
        var package = OdsPackage.Read(new MemoryStream(OdsWriter.WriteToBytes(workbook)));
        package.SetBytes("Pictures/chart.bin", [1, 2, 3, 4]);
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
    [InlineData("add-sheet", null, "Sheet1", "--in-place")]                // duplicate sheet (InvalidOperationException)
    [InlineData("rename-sheet", null, "Sheet1", "bad/name", "--in-place")] // invalid name (ArgumentException)
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

    /// <summary>A CSV of text cells — the shape Clean Up exists for.</summary>
    private string MessyBook()
    {
        string csv = Path2("messy.csv");
        string ods = Path2("messy.ods");
        File.WriteAllText(csv, "Region,Revenue\nWest,  1200.50 \nEast,900\n");
        Run("import", csv, "--into", ods, "--sheet", "Sheet1");
        return ods;
    }

    [Fact]
    public void Clean_up_dry_run_reports_without_writing()
    {
        string ods = MessyBook();
        var before = File.GetLastWriteTimeUtc(ods);

        var (exit, output, _) = Run("clean-up", ods, "--dry-run", "--locale", "en-US");

        Assert.Equal(0, exit);
        Assert.Contains("Sheet1:", output, StringComparison.Ordinal);
        Assert.Equal(before, File.GetLastWriteTimeUtc(ods));
    }

    [Fact]
    public void Clean_up_refuses_to_overwrite_without_being_told_to()
    {
        string ods = MessyBook();

        var (exit, _, err) = Run("clean-up", ods);

        Assert.NotEqual(0, exit);
        Assert.Contains("--in-place", err, StringComparison.Ordinal);
    }

    [Fact]
    public void Clean_up_formats_the_workbook_and_settles_after_one_pass()
    {
        string ods = MessyBook();

        Assert.Equal(0, Run("clean-up", ods, "--in-place", "--locale", "en-US").Exit);

        // The trimmed text is a real number now, and the header row is frozen — through a save.
        var sheet = OdsReader.Read(File.OpenRead(ods)).FindSheet("Sheet1")!;
        Assert.Equal(1200.5, sheet.GetValue(1, 1).AsNumber);
        Assert.Equal(1, sheet.FrozenRows);

        // Idempotent: a second pass has nothing left to say.
        Assert.Contains("Nothing to clean up.", Run("clean-up", ods, "--dry-run", "--locale", "en-US").Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Clean_up_can_be_narrowed_to_one_sheet()
    {
        string ods = MessyBook();
        Run("add-sheet", ods, "Other", "--in-place");
        Run("set", ods, "Other.A1", "Label", "--in-place");
        Run("set", ods, "Other.A2", "7", "--in-place");

        var (exit, output, _) = Run("clean-up", ods, "--sheet", "Sheet1", "--dry-run", "--locale", "en-US");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Other:", output, StringComparison.Ordinal);
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
        Assert.Equal(["Red", "Green", "Blue"], rule.ListValues!);
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

    // ---- Chart sheets -----------------------------------------------------

    private string ChartDataBook()
    {
        string ods = Path2("chartdata.ods");
        Run("new", ods);
        Run("set", ods, "A1", "Region", "--in-place");
        Run("set", ods, "B1", "Q1", "--in-place");
        Run("set", ods, "C1", "Q2", "--in-place");
        Run("set", ods, "A2", "North", "--in-place");
        Run("set", ods, "B2", "10", "--in-place");
        Run("set", ods, "C2", "5", "--in-place");
        Run("set", ods, "A3", "South", "--in-place");
        Run("set", ods, "B3", "20", "--in-place");
        Run("set", ods, "C3", "8", "--in-place");
        return ods;
    }

    [Fact]
    public void Chart_from_a_range_seeds_categories_and_a_series_per_column()
    {
        string ods = ChartDataBook();
        Assert.Equal(0, Run("chart", ods, "--type", "column", "--title", "Sales", "--from", "Sheet1.A1:C3", "--in-place").Exit);

        var sheet = OdsReader.Read(File.OpenRead(ods)).FindSheet("Chart1")!;
        Assert.True(sheet.IsChartSheet);
        var chart = sheet.Chart!;
        Assert.Equal("Sales", chart.Title);
        Assert.Equal(Cellestra.Charts.ChartType.Column, chart.Type);
        Assert.Equal("Sheet1.A2:A3", chart.CategoriesReference); // first column → categories, header skipped
        Assert.Equal(2, chart.Series.Count);                     // Q1 and Q2 columns → two series
        Assert.Equal("Sheet1.B2:B3", chart.Series[0].ValuesReference);
        Assert.Equal("Sheet1.B1", chart.Series[0].NameReference); // header row names the series
    }

    [Fact]
    public void Chart_with_explicit_fields_round_trips()
    {
        string ods = ChartDataBook();
        Assert.Equal(0, Run("chart", ods,
            "--type", "bar", "--stacking", "percent",
            "--categories", "Sheet1.A2:A3",
            "--values", "Sheet1.B2:B3", "--names", "First",
            "--values", "Sheet1.C2:C3", "--names", "Second",
            "--y-title", "Sales", "--no-legend", "--in-place").Exit);

        var chart = OdsReader.Read(File.OpenRead(ods)).FindSheet("Chart1")!.Chart!;
        Assert.Equal(Cellestra.Charts.ChartType.Bar, chart.Type);
        Assert.Equal(Cellestra.Charts.ChartStacking.Percent, chart.Stacking);
        Assert.False(chart.ShowLegend);
        Assert.Equal("Sales", chart.YAxisTitle);
        Assert.Equal(2, chart.Series.Count);
        Assert.Equal("First", chart.Series[0].Name);
        Assert.Equal("Sheet1.C2:C3", chart.Series[1].ValuesReference);
    }

    [Fact]
    public void Scatter_chart_keeps_its_x_values_per_series()
    {
        string ods = ChartDataBook();
        Assert.Equal(0, Run("chart", ods, "--type", "scatter",
            "--x-values", "Sheet1.B2:B3", "--values", "Sheet1.C2:C3", "--in-place").Exit);

        var series = Assert.Single(OdsReader.Read(File.OpenRead(ods)).FindSheet("Chart1")!.Chart!.Series);
        Assert.Equal("Sheet1.B2:B3", series.XValuesReference);
        Assert.Equal("Sheet1.C2:C3", series.ValuesReference);
    }

    [Fact]
    public void Bubble_chart_requires_bubble_sizes()
    {
        string ods = ChartDataBook();
        var (exit, _, err) = Run("chart", ods, "--type", "bubble",
            "--x-values", "Sheet1.A2:A3", "--values", "Sheet1.B2:B3", "--in-place");
        Assert.Equal(1, exit);
        Assert.Contains("--bubble-sizes", err, StringComparison.Ordinal);
    }

    [Fact]
    public void Pie_chart_rejects_more_than_one_series()
    {
        string ods = ChartDataBook();
        var (exit, _, err) = Run("chart", ods, "--type", "pie",
            "--values", "Sheet1.B2:B3", "--values", "Sheet1.C2:C3", "--in-place");
        Assert.Equal(1, exit);
        Assert.Contains("single series", err, StringComparison.Ordinal);
    }

    [Fact]
    public void Stacking_is_rejected_for_chart_types_that_cannot_stack()
    {
        string ods = ChartDataBook();
        var (exit, _, err) = Run("chart", ods, "--type", "line",
            "--from", "Sheet1.A1:C3", "--stacking", "stacked", "--in-place");
        Assert.Equal(1, exit);
        Assert.Contains("stacking", err, StringComparison.OrdinalIgnoreCase);
    }

    // ---- `cellestra <file>` — the launcher at the root of the verb set -----
    // Launching the GUI itself can't be asserted in a headless container; what is asserted is everything
    // that has to be decided before anything launches.

    [Fact]
    public void Launching_a_workbook_that_does_not_exist_is_refused_before_anything_launches()
    {
        var (exit, _, err) = Run(Path2("nope.ods"));
        Assert.Equal(1, exit);
        Assert.Contains("does not exist", err, StringComparison.Ordinal);
    }

    [Fact]
    public void An_argument_that_is_not_a_workbook_names_both_readings()
    {
        // Far more likely a mistyped verb than a workbook, so the error says so rather than letting the
        // app open and put up its own dialog.
        var (exit, _, err) = Run("recal");
        Assert.Equal(1, exit);
        Assert.Contains("isn't a workbook Cellestra opens", err, StringComparison.Ordinal);
        Assert.Contains("cellestra --help", err, StringComparison.Ordinal);
    }

    [Fact]
    public void Bare_cellestra_still_prints_help_rather_than_launching()
    {
        // This is a toolkit first and a launcher second: the verb list is what the bare name came for.
        var (exit, output, _) = Run();
        Assert.Equal(0, exit);
        Assert.Contains("Usage", output, StringComparison.Ordinal);
        Assert.Contains("recalc", output, StringComparison.Ordinal);
    }
}
