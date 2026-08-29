using System.CommandLine;
using System.Globalization;
using Cellestra.Cli.Infrastructure;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Puffinote.Components.Core.Cli;

namespace Cellestra.Cli.Commands;

/// <summary>
/// Moving blocks around and filtering them — <c>copy</c>, <c>move</c>, <c>fill</c>, <c>paint</c>,
/// <c>filter</c> — the desktop's clipboard, fill handle, format painter, and AutoFilter, driven by
/// the same Core commands (so a copied formula's relative references shift exactly as they do in
/// the app, and a Stop validation rule aborts the paste the same way). Verbs are fronts on the ops.
/// </summary>
internal static class RangeCommands
{
    public static Command Copy() => Transfer("copy", "Copy a block to a destination (relative references shift, like the app's paste)", cut: false);

    public static Command Move() => Transfer("move", "Move a block to a destination (formulas travel unchanged; the source is cleared)", cut: true);

    private static Command Transfer(string verb, string description, bool cut)
    {
        var file = Options.File();
        var from = new Argument<string>("from") { Description = "Source range, e.g. Sheet1.A1:B3 (a name or table works too)" };
        var to = new Argument<string>("to") { Description = "Destination top-left cell or range, e.g. Sheet2.D1" };
        var mode = new Option<string>("--mode")
        {
            Description = "all | values | formats | formulas (what to paste — copy only)",
            DefaultValueFactory = _ => "all",
        };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command(verb, description) { file, from, to, mode, inPlace, output, json };
        command.SetAction(parse => LayoutCommands.Dispatch(parse, file, inPlace, output, json, verb, new Dictionary<string, object?>
        {
            ["from"] = parse.GetValue(from),
            ["to"] = parse.GetValue(to),
            ["mode"] = cut ? null : parse.GetValue(mode),
        }));
        return command;
    }

    public static Command Fill()
    {
        var file = Options.File();
        var range = new Argument<string>("range") { Description = "Range to fill, e.g. D2:D20 — the fill handle's target" };
        var from = new Option<string?>("--from") { Description = "Source block to repeat (default: the range's first row, or first column when it is wider than tall)" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("fill", "Fill a range by repeating a source block (formulas shift like the fill handle)") { file, range, from, inPlace, output, json };
        command.SetAction(parse => LayoutCommands.Dispatch(parse, file, inPlace, output, json, "fill", new Dictionary<string, object?>
        {
            ["range"] = parse.GetValue(range),
            ["from"] = parse.GetValue(from),
        }));
        return command;
    }

    public static Command Paint()
    {
        var file = Options.File();
        var from = new Argument<string>("from") { Description = "Range whose formatting to copy, e.g. A1:D1" };
        var to = new Argument<string>("to") { Description = "Range to format like it (the pattern tiles)" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("paint", "Copy formatting from one range onto another (format painter)") { file, from, to, inPlace, output, json };
        command.SetAction(parse => LayoutCommands.Dispatch(parse, file, inPlace, output, json, "paint", new Dictionary<string, object?>
        {
            ["from"] = parse.GetValue(from),
            ["to"] = parse.GetValue(to),
        }));
        return command;
    }

    public static Command Filter()
    {
        var file = Options.File();
        var range = new Argument<string>("range") { Description = "Range (or table) to filter, e.g. A1:D100" };
        var where = new Option<string[]>("--where")
        {
            Description = "A condition on a column: \"B >5\", \"C *ink*\", \"D <>0\" (SUMIF-style; repeat for more columns)",
            AllowMultipleArgumentsPerToken = false,
        };
        var values = new Option<string[]>("--values")
        {
            Description = "Allowed values for a column: \"C Red,Blue\" (repeat for more columns)",
            AllowMultipleArgumentsPerToken = false,
        };
        var clear = new Option<bool>("--clear") { Description = "Remove the filter: show every row again" };
        var noHeader = new Option<bool>("--no-header") { Description = "Treat the first row as data, not a header" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var locale = Options.Locale();
        var json = Options.Json();
        var command = new Command("filter", "AutoFilter a range: hide the rows that don't match (never deletes data)") { file, range, where, values, clear, noHeader, inPlace, output, locale, json };

        command.SetAction(parse =>
        {
            var columns = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var spec in parse.GetValue(where) ?? [])
            {
                var (column, rest) = SplitColumn(spec, "--where");
                columns[column] = Merge(columns, column, "condition", rest);
            }

            foreach (var spec in parse.GetValue(values) ?? [])
            {
                var (column, rest) = SplitColumn(spec, "--values");
                columns[column] = Merge(columns, column, "values", rest.Split(',', StringSplitOptions.TrimEntries));
            }

            var fields = new Dictionary<string, object?>
            {
                ["range"] = parse.GetValue(range),
                ["header"] = parse.GetValue(noHeader) ? false : null,
                ["clear"] = parse.GetValue(clear) ? true : null,
                ["columns"] = columns.Count > 0 ? columns : null,
            };
            string path = parse.GetValue(file)!;
            string target = Options.EditTarget(path, parse.GetValue(inPlace), parse.GetValue(output));
            var present = fields.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            return EditSession.Run(path, target, Locale.Resolve(parse.GetValue(locale)), parse.GetValue(json),
                session => ApplyCommands.RunOp(session, "filter", present));
        });

        return command;
    }

    private static (string Column, string Criteria) SplitColumn(string spec, string option)
    {
        int space = spec.IndexOf(' ', StringComparison.Ordinal);
        if (space <= 0 || space == spec.Length - 1)
        {
            throw new CliException($"{option} needs \"<column> <criteria>\", e.g. {option} \"B >5\".");
        }

        return (spec[..space].Trim(), spec[(space + 1)..].Trim());
    }

    private static Dictionary<string, object?> Merge(Dictionary<string, object?> columns, string column, string key, object value)
    {
        var existing = columns.TryGetValue(column, out var found) && found is Dictionary<string, object?> d ? d : new Dictionary<string, object?>(StringComparer.Ordinal);
        existing[key] = value;
        return existing;
    }

    // ---- The ops themselves (called from ApplyCommands) --------------------

    internal static void CopyOrMove(EditSession session, Worksheet fromSheet, CellRange from, Worksheet toSheet, CellRange to, bool cut, string modeText)
    {
        var mode = modeText.Trim().ToLowerInvariant() switch
        {
            "all" => PasteMode.All,
            "values" => PasteMode.Values,
            "formats" or "formatting" => PasteMode.Formats,
            "formulas" => PasteMode.Formulas,
            _ => throw new CliException("\"mode\" must be all, values, formats, or formulas."),
        };
        if (cut && mode != PasteMode.All)
        {
            throw new CliException("a move always carries everything; \"mode\" applies to copy.");
        }

        var block = ClipboardBlock.Capture(fromSheet, ClipboardBlock.CopyRange(fromSheet, from), isCut: cut);
        var paste = new PasteBlockCommand(toSheet.Name, to, block, mode);
        Preflight(session, toSheet, paste.ProjectWrites());
        session.Apply(paste);
    }

    internal static void Fill(EditSession session, Worksheet sheet, CellRange range, CellRange? source)
    {
        var seed = source ?? (range.RowCount >= range.ColumnCount
            ? new CellRange(new CellAddress(range.Left, range.Top), new CellAddress(range.Right, range.Top))
            : new CellRange(new CellAddress(range.Left, range.Top), new CellAddress(range.Left, range.Bottom)));
        if (!range.Contains(seed.Top, seed.Left) || !range.Contains(seed.Bottom, seed.Right))
        {
            throw new CliException($"the source {seed} must lie inside the range {range} being filled.");
        }

        var fill = new FillCommand(sheet.Name, seed, range);
        Preflight(session, sheet, fill.ProjectWrites(session.Workbook));
        session.Apply(fill);
    }

    internal static void Paint(EditSession session, Worksheet fromSheet, CellRange from, Worksheet toSheet, CellRange to)
    {
        var block = FormatBlock.Capture(session.Workbook, fromSheet, from);
        session.Apply(block.ApplyTo(toSheet.Name, to));
        foreach (var (row, column) in to.Positions())
        {
            session.Touched(toSheet.Name, row, column);
        }
    }

    internal static void Filter(EditSession session, Worksheet sheet, CellRange range, bool header, bool clear, IReadOnlyDictionary<int, ColumnFilter> columns)
    {
        if (clear)
        {
            session.Apply(DataEditCommands.ClearFilter(sheet.Name, range));
            return;
        }

        if (columns.Count == 0)
        {
            throw new CliException("a filter needs at least one column criterion (\"columns\": {\"B\": {\"condition\": \">5\"}}), or \"clear\": true.");
        }

        foreach (int column in columns.Keys)
        {
            if (column < range.Left || column > range.Right)
            {
                throw new CliException($"column {SheetDimensions.ColumnToLetters(column)} is outside {range}.");
            }
        }

        var spec = new FilterSpec(columns, header);
        session.Apply(DataEditCommands.ApplyFilter(sheet.Name, range, spec, session.Culture));
        int hidden = 0;
        for (int row = range.Top + (header ? 1 : 0); row <= range.Bottom; row++)
        {
            if (sheet.IsRowHidden(row))
            {
                hidden++;
            }
        }

        session.Field("hiddenRows", hidden);
        session.Note($"Filtered {range}: {hidden} row(s) hidden.");
    }

    /// <summary>The app's rule: a paste or fill that would violate a Stop validation rule doesn't happen at all.</summary>
    private static void Preflight(EditSession session, Worksheet sheet, IEnumerable<(int Row, int Column, CellValue Value, bool IsFormula)> writes)
    {
        var violations = ValidationPreflight.FindStopViolations(sheet, writes, session.Culture, range => Resolve(sheet, range));
        if (violations.Count > 0)
        {
            var (row, column) = violations[0];
            throw new CliException($"{Report.Address(sheet.Name, row, column)} would violate its validation rule ({violations.Count} cell(s) in all); nothing was written.");
        }
    }

    private static IEnumerable<CellValue> Resolve(Worksheet sheet, CellRange range)
    {
        foreach (var (row, column) in range.Positions())
        {
            yield return sheet.GetValue(row, column);
        }
    }

    internal static CultureInfo CultureOf(EditSession session) => session.Culture;
}
