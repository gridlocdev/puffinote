using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cellestra.Cli.Infrastructure;
using Cellestra.Core.Editing;
using Cellestra.Core.Formulas.Functions;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Csv;
using Cellestra.Core.Values;
using Puffinote.Components.Core.Cli;

namespace Cellestra.Cli.Commands;

/// <summary>The nice-to-haves: new, import/export, stats, find, diff, functions, sheet management, open, completions.</summary>
internal static class ExtraCommands
{
    public static Command New()
    {
        var file = Options.File("file", "Workbook to create (.ods)");
        var sheets = new Option<string?>("--sheets") { Description = "Comma-separated sheet names (default: Sheet1)" };
        var json = Options.Json();
        var command = new Command("new", "Create a new workbook") { file, sheets, json };

        command.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            if (path != "-" && File.Exists(path))
            {
                throw new CliException($"'{path}' already exists — refusing to overwrite.");
            }

            var names = (parse.GetValue(sheets) ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var workbook = Workbook.CreateNew();
            if (names.Length > 0)
            {
                workbook.RenameSheet(workbook.ActiveSheet, names[0]);
                foreach (var name in names.Skip(1))
                {
                    workbook.AddSheet(name);
                }
            }

            return EditSession.Run(new LoadedWorkbook(workbook, null), path, null, parse.GetValue(json), session =>
                session.Field("sheets", workbook.Worksheets.Select(w => w.Name).ToList()));
        });

        return command;
    }

    public static Command Import()
    {
        var csvFile = Options.File("csv", "CSV file to import (or - for stdin)");
        var into = new Option<string>("--into") { Description = "Target .ods workbook (created if absent)", Required = true };
        var sheet = new Option<string>("--sheet", "-s") { Description = "Sheet to fill (created/replaced)", Required = true };
        var delimiter = Options.Delimiter();
        var locale = Options.Locale();
        var json = Options.Json();
        var command = new Command("import", "Import a CSV into a sheet of an .ods workbook") { csvFile, into, sheet, delimiter, locale, json };

        command.SetAction(parse =>
        {
            string target = parse.GetValue(into)!;
            string sheetName = parse.GetValue(sheet)!;
            var culture = Locale.Resolve(parse.GetValue(locale));
            var csv = Options.Csv(parse.GetValue(delimiter));
            string text = ReadText(parse.GetValue(csvFile)!);

            var loaded = File.Exists(target)
                ? Workspace.Load(target, WorkbookFormat.Ods, culture)
                : new LoadedWorkbook(Workbook.CreateNew(), null);

            return EditSession.Run(loaded, target, culture, parse.GetValue(json), session =>
            {
                var workbook = session.Workbook;
                var worksheet = workbook.FindSheet(sheetName);
                if (worksheet is null)
                {
                    worksheet = workbook.AddSheet(sheetName);
                }
                else
                {
                    ClearSheet(worksheet);
                }

                CsvReader.Fill(workbook, worksheet, text, csv, culture);
                var used = worksheet.GetUsedRange();
                session.Field("sheet", worksheet.Name);
                session.Field("range", used?.ToString());
                session.Note($"Imported {worksheet.CellCount} cell(s) into '{worksheet.Name}'.");
            });
        });

        return command;
    }

    /// <summary>
    /// Drop a grid at an anchor — the missing middle between <c>set</c> (one cell) and <c>import</c>
    /// (a whole sheet). Each field is parsed like typing, so numbers, dates, and <c>=</c>formulas land typed.
    /// </summary>
    public static Command Put()
    {
        var file = Options.File();
        var at = new Argument<string>("at") { Description = "Top-left cell to write at, e.g. Sheet1.B3" };
        var from = new Option<string>("--from") { Description = "CSV/TSV file to read the grid from (or - for stdin)", Required = true };
        var delimiter = Options.Delimiter();
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var locale = Options.Locale();
        var json = Options.Json();
        var command = new Command("put", "Write a CSV/TSV grid into a sheet at a cell (values and formulas, parsed like typing)")
        {
            file, at, from, delimiter, inPlace, output, locale, json,
        };

        command.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            string target = Options.EditTarget(path, parse.GetValue(inPlace), parse.GetValue(output));
            string source = parse.GetValue(from)!;
            var csv = Options.Csv(parse.GetValue(delimiter)) ?? (source.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ? CsvOptions.Tab : null);
            var rows = CsvReader.Parse(ReadText(source), csv);

            return EditSession.Run(path, target, Locale.Resolve(parse.GetValue(locale)), parse.GetValue(json), session =>
            {
                var (sheet, anchor) = Cells.ResolveCell(session.Workbook, parse.GetValue(at)!);
                int written = 0;
                for (int r = 0; r < rows.Count; r++)
                {
                    for (int c = 0; c < rows[r].Count; c++)
                    {
                        session.Apply(EditCommands.SetInput(session.Workbook, sheet.Name, anchor.Row + r, anchor.Column + c, rows[r][c], session.Culture));
                        written++;
                    }
                }

                int width = rows.Count == 0 ? 0 : rows.Max(row => row.Count);
                CellRange? covered = rows.Count == 0 ? null : new CellRange(anchor.WithoutSheet(), new CellAddress(anchor.Column + width - 1, anchor.Row + rows.Count - 1));
                session.Field("sheet", sheet.Name);
                session.Field("range", covered?.ToString());
                session.Note($"Wrote {written} cell(s) at {Report.Address(sheet.Name, anchor.Row, anchor.Column)}.");
            });
        });

        return command;
    }

    public static Command Export()
    {
        var file = Options.File("file", "Source .ods workbook");
        var output = Options.File("output", "Destination CSV (or - for stdout)");
        var sheet = Options.Sheet();
        var delimiter = Options.Delimiter();
        var command = new Command("export", "Export a sheet to CSV") { file, output, sheet, delimiter };

        command.SetAction(parse =>
        {
            var csv = Options.Csv(parse.GetValue(delimiter));
            var loaded = Workspace.Load(parse.GetValue(file)!, WorkbookFormat.Ods, null);
            Workspace.Recalculate(loaded.Workbook);
            var worksheet = Cells.PickSheet(loaded.Workbook, parse.GetValue(sheet));
            Workspace.SaveText(CsvWriter.Write(loaded.Workbook, worksheet, csv), parse.GetValue(output)!);
            return 0;
        });

        return command;
    }

    public static Command Stats()
    {
        var file = Options.File();
        var range = new Argument<string>("range") { Description = "Range to aggregate, e.g. Sheet1.A1:A100 (a name or table works too)" };
        var locale = Options.Locale();
        var json = Options.Json();
        var command = new Command("stats", "Describe a range: count, blanks, types, sum, average, median, stdev, min, max, distinct") { file, range, locale, json };

        command.SetAction(parse =>
        {
            var culture = Locale.Resolve(parse.GetValue(locale));
            var loaded = Workspace.Load(parse.GetValue(file)!, null, culture);
            Workspace.Recalculate(loaded.Workbook);
            var (worksheet, cellRange) = Cells.ResolveRange(loaded.Workbook, parse.GetValue(range)!);

            int count = 0, texts = 0, booleans = 0, errors = 0, formulas = 0;
            var numbers = new List<double>();
            var distinct = new HashSet<string>(StringComparer.Ordinal);

            // Walk only the populated cells (O(populated)), not every position in the bounding box —
            // so `stats Sheet1.A1:A1048576` over a sparse column stays fast.
            foreach (var (row, column, cell) in worksheet.EnumerateCells())
            {
                if (!cellRange.Contains(row, column) || cell.Value.IsEmpty)
                {
                    continue;
                }

                count++;
                distinct.Add(cell.Value.ToInvariantString());
                if (cell.IsFormula)
                {
                    formulas++;
                }

                if (cell.Value.IsNumber)
                {
                    numbers.Add(cell.Value.AsNumber);
                }
                else if (cell.Value.IsText)
                {
                    texts++;
                }
                else if (cell.Value.IsBoolean)
                {
                    booleans++;
                }
                else if (cell.Value.IsError)
                {
                    errors++;
                }
            }

            long total = (long)cellRange.RowCount * cellRange.ColumnCount;
            long blanks = total - count;
            double? sum = numbers.Count > 0 ? numbers.Sum() : null;
            double? average = numbers.Count > 0 ? numbers.Average() : null;
            double? min = numbers.Count > 0 ? numbers.Min() : null;
            double? max = numbers.Count > 0 ? numbers.Max() : null;
            double? median = Median(numbers);
            double? stdev = SampleStdev(numbers);

            if (parse.GetValue(json))
            {
                Report.Write(new
                {
                    sheet = worksheet.Name,
                    range = cellRange.ToString(),
                    cells = total,
                    count,
                    blanks,
                    types = new { number = numbers.Count, text = texts, boolean = booleans, error = errors, formula = formulas },
                    distinct = distinct.Count,
                    sum,
                    average,
                    median,
                    stdev,
                    min,
                    max,
                });
                return 0;
            }

            var inv = CultureInfo.InvariantCulture;
            string N(double? n) => n is { } v ? v.ToString("0.##########", inv) : string.Empty;
            Console.Out.WriteLine($"cells\t{total}");
            Console.Out.WriteLine($"count\t{count}");
            Console.Out.WriteLine($"blanks\t{blanks}");
            Console.Out.WriteLine($"numeric\t{numbers.Count}");
            Console.Out.WriteLine($"text\t{texts}");
            Console.Out.WriteLine($"boolean\t{booleans}");
            Console.Out.WriteLine($"error\t{errors}");
            Console.Out.WriteLine($"formula\t{formulas}");
            Console.Out.WriteLine($"distinct\t{distinct.Count}");
            Console.Out.WriteLine($"sum\t{N(sum)}");
            Console.Out.WriteLine($"average\t{N(average)}");
            Console.Out.WriteLine($"median\t{N(median)}");
            Console.Out.WriteLine($"stdev\t{N(stdev)}");
            Console.Out.WriteLine($"min\t{N(min)}");
            Console.Out.WriteLine($"max\t{N(max)}");
            return 0;
        });

        return command;
    }

    private static double? Median(List<double> numbers)
    {
        if (numbers.Count == 0)
        {
            return null;
        }

        var sorted = numbers.Order().ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>Sample standard deviation (n − 1), the same as the STDEV function; null below two numbers.</summary>
    private static double? SampleStdev(List<double> numbers)
    {
        if (numbers.Count < 2)
        {
            return null;
        }

        double mean = numbers.Average();
        return Math.Sqrt(numbers.Sum(n => (n - mean) * (n - mean)) / (numbers.Count - 1));
    }

    public static Command Find()
    {
        var file = Options.File();
        var term = new Argument<string>("term") { Description = "Text to search for (case-insensitive unless --regex)" };
        var regex = new Option<bool>("--regex") { Description = "Treat the term as a regular expression" };
        var into = new Option<string>("--in")
        {
            Description = "Where to search: values | formulas | both",
            DefaultValueFactory = _ => "both"
        };
        var replace = new Option<string?>("--replace") { Description = "Replace every match with this text (needs --in-place or -o); $1 groups with --regex" };
        var sheetOption = Options.Sheet();
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var locale = Options.Locale();
        var json = Options.Json();
        var command = new Command("find", "Search cells by value or formula — and optionally replace") { file, term, regex, into, replace, sheetOption, inPlace, output, locale, json };

        command.SetAction(parse =>
        {
            string where = (parse.GetValue(into) ?? "both").ToLowerInvariant();
            bool searchValues = where is "values" or "both";
            bool searchFormulas = where is "formulas" or "both";
            if (!searchValues && !searchFormulas)
            {
                throw new CliException("--in must be values, formulas, or both.");
            }

            string termText = parse.GetValue(term) ?? string.Empty;
            Regex pattern;
            try
            {
                pattern = parse.GetValue(regex)
                    ? new Regex(termText, RegexOptions.None)
                    : new Regex(Regex.Escape(termText), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                throw new CliException($"Invalid regex '{termText}': {ex.Message}");
            }

            string? replacement = parse.GetValue(replace);
            string path = parse.GetValue(file)!;
            string? only = parse.GetValue(sheetOption);
            bool asJson = parse.GetValue(json);
            var culture = Locale.Resolve(parse.GetValue(locale));

            if (replacement is not null)
            {
                string target = Options.EditTarget(path, parse.GetValue(inPlace), parse.GetValue(output));
                return EditSession.Run(path, target, culture, asJson, session =>
                {
                    int replaced = 0;
                    foreach (var worksheet in session.Workbook.Worksheets.Where(s => only is null || string.Equals(s.Name, only, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Snapshot first: the edits mutate the sheet we're walking.
                        var hits = worksheet.EnumerateCells().Select(c => (c.Row, c.Column, c.Cell)).ToList();
                        foreach (var (row, column, cell) in hits)
                        {
                            if (searchFormulas && cell.IsFormula && pattern.IsMatch(cell.Formula!))
                            {
                                string formula = pattern.Replace(cell.Formula!, replacement);
                                session.Apply(EditCommands.SetFormula(worksheet.Name, row, column, formula));
                                replaced++;
                            }
                            else if (searchValues && !cell.IsFormula && cell.Value.IsText && pattern.IsMatch(cell.Value.AsText))
                            {
                                string text = pattern.Replace(cell.Value.AsText, replacement);
                                session.Apply(EditCommands.SetValue(worksheet.Name, row, column, CellValue.Text(text)));
                                replaced++;
                            }
                        }
                    }

                    session.Field("replaced", replaced);
                    session.Note($"Replaced in {replaced} cell(s).");
                });
            }

            var loaded = Workspace.Load(path, null, culture);
            Workspace.Recalculate(loaded.Workbook);
            var effectiveCulture = culture ?? loaded.Workbook.Settings.EffectiveLocale;
            int found = 0;
            var matches = new List<CellReport>();
            foreach (var worksheet in loaded.Workbook.Worksheets.Where(s => only is null || string.Equals(s.Name, only, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var (row, column, cell) in worksheet.EnumerateCells())
                {
                    bool match =
                        (searchFormulas && cell.IsFormula && pattern.IsMatch("=" + cell.Formula)) ||
                        (searchValues && pattern.IsMatch(Cells.Display(loaded.Workbook, worksheet, row, column, effectiveCulture)));
                    if (!match)
                    {
                        continue;
                    }

                    found++;
                    if (asJson)
                    {
                        matches.Add(Report.Cell(loaded.Workbook, worksheet, row, column, effectiveCulture));
                        continue;
                    }

                    var address = new CellAddress(column, row, sheet: worksheet.Name);
                    Console.Out.WriteLine($"{address}\t{Cells.FormulaOrValue(loaded.Workbook, worksheet, row, column, effectiveCulture)}");
                }
            }

            if (asJson)
            {
                Report.Write(new { term = termText, matches });
            }

            return found > 0 ? 0 : 1;
        });

        return command;
    }

    public static Command Diff()
    {
        var left = Options.File("before", "First workbook");
        var right = Options.File("after", "Second workbook");
        var format = new Option<string>("--format", "-f")
        {
            Description = "unified (default)",
            DefaultValueFactory = _ => "unified"
        };
        var styles = new Option<bool>("--styles") { Description = "Also compare cell formatting (bold, fill, number format, …)" };
        var json = Options.Json();
        var command = new Command("diff", "Compare two workbooks: values/formulas, structure (sheets, layout, tables, charts, names), and with --styles formatting (exit 1 if they differ)")
        {
            left, right, format, styles, json,
        };

        command.SetAction(parse =>
        {
            var a = Workspace.Load(parse.GetValue(left)!, null, null);
            var b = Workspace.Load(parse.GetValue(right)!, null, null);
            Workspace.Recalculate(a.Workbook);
            Workspace.Recalculate(b.Workbook);
            var culture = CultureInfo.InvariantCulture;
            bool asJson = parse.GetValue(json);
            bool compareStyles = parse.GetValue(styles);

            var addedSheets = new List<string>();
            var removedSheets = new List<string>();
            var cells = new List<object>();
            var structure = new List<object>();
            var sheetNames = a.Workbook.Worksheets.Select(s => s.Name)
                .Union(b.Workbook.Worksheets.Select(s => s.Name), StringComparer.OrdinalIgnoreCase)
                .ToList();

            void Line(string text)
            {
                if (!asJson)
                {
                    Console.Out.WriteLine(text);
                }
            }

            foreach (var name in sheetNames)
            {
                var sheetA = a.Workbook.FindSheet(name);
                var sheetB = b.Workbook.FindSheet(name);
                if (sheetA is null)
                {
                    addedSheets.Add(name);
                    Line($"+ sheet {name}");
                    continue;
                }

                if (sheetB is null)
                {
                    removedSheets.Add(name);
                    Line($"- sheet {name}");
                    continue;
                }

                foreach (var (row, column) in UsedUnion(sheetA, sheetB))
                {
                    string left1 = Cells.FormulaOrValue(a.Workbook, sheetA, row, column, culture);
                    string right1 = Cells.FormulaOrValue(b.Workbook, sheetB, row, column, culture);
                    var address = new CellAddress(column, row, sheet: name);
                    if (!string.Equals(left1, right1, StringComparison.Ordinal))
                    {
                        cells.Add(new { cell = address.ToString(), before = left1, after = right1 });
                        Line($"{address}: \"{left1}\" -> \"{right1}\"");
                    }

                    if (compareStyles)
                    {
                        string styleA = StyleText(a.Workbook, sheetA, row, column);
                        string styleB = StyleText(b.Workbook, sheetB, row, column);
                        if (!string.Equals(styleA, styleB, StringComparison.Ordinal))
                        {
                            cells.Add(new { cell = address.ToString(), styleBefore = styleA, styleAfter = styleB });
                            Line($"{address} style: {styleA} -> {styleB}");
                        }
                    }
                }

                // Layout and structure: the same description sheets --json prints, compared key by key.
                CompareObjects(name, Report.Sheet(sheetA), Report.Sheet(sheetB), structure, Line, skip: ["cells", "rows", "columns", "usedRange"]);
                if (sheetA.IsChartSheet && sheetB.IsChartSheet)
                {
                    CompareObjects(name, ChartCommands.Describe(sheetA), ChartCommands.Describe(sheetB), structure, Line, skip: ["sheet"]);
                }
            }

            CompareObjects("(workbook)", new { namedRanges = a.Workbook.NamedRanges.Select(Report.NamedRange).ToList() },
                new { namedRanges = b.Workbook.NamedRanges.Select(Report.NamedRange).ToList() }, structure, Line, skip: []);

            int differences = addedSheets.Count + removedSheets.Count + cells.Count + structure.Count;
            if (asJson)
            {
                Report.Write(new { same = differences == 0, differences, addedSheets, removedSheets, cells, structure });
            }

            return differences > 0 ? 1 : 0;
        });

        return command;
    }

    private static string StyleText(Workbook workbook, Worksheet sheet, int row, int column)
    {
        var cell = sheet.Find(row, column);
        var style = cell is null ? null : Report.Style(workbook.Styles.Get(cell.StyleId));
        return style is null ? "{}" : JsonSerializer.Serialize(style);
    }

    /// <summary>Report every top-level property whose JSON differs between two descriptions of the same thing.</summary>
    private static void CompareObjects(string scope, object before, object after, List<object> into, Action<string> line, string[] skip)
    {
        using var docA = JsonDocument.Parse(JsonSerializer.Serialize(before, Report.JsonOptions));
        using var docB = JsonDocument.Parse(JsonSerializer.Serialize(after, Report.JsonOptions));
        var keys = docA.RootElement.EnumerateObject().Select(p => p.Name)
            .Union(docB.RootElement.EnumerateObject().Select(p => p.Name), StringComparer.Ordinal)
            .Where(k => !skip.Contains(k, StringComparer.Ordinal));
        foreach (var key in keys)
        {
            string? valueA = docA.RootElement.TryGetProperty(key, out var pa) ? pa.GetRawText() : null;
            string? valueB = docB.RootElement.TryGetProperty(key, out var pb) ? pb.GetRawText() : null;
            if (!string.Equals(valueA, valueB, StringComparison.Ordinal))
            {
                into.Add(new { sheet = scope, property = key, before = valueA, after = valueB });
                line($"{scope} {key}: {Compact(valueA)} -> {Compact(valueB)}");
            }
        }
    }

    private static string Compact(string? json) =>
        json is null ? "(none)" : Regex.Replace(json, @"\s*\n\s*", " ");

    public static Command Functions()
    {
        var name = new Argument<string?>("name")
        {
            Description = "A function to describe (omit to list all)",
            Arity = ArgumentArity.ZeroOrOne
        };
        var json = Options.Json();
        var command = new Command("functions", "List supported functions, or describe one") { name, json };

        command.SetAction(parse =>
        {
            var registry = FunctionRegistry.CreateDefault();
            string? wanted = parse.GetValue(name);
            bool asJson = parse.GetValue(json);
            if (string.IsNullOrEmpty(wanted))
            {
                if (asJson)
                {
                    Report.Write(registry.AllMetadata.Select(FunctionJson).ToList());
                    return 0;
                }

                foreach (var meta in registry.AllMetadata)
                {
                    Console.Out.WriteLine($"{meta.Signature,-40}  {meta.Category}");
                }

                return 0;
            }

            if (!registry.TryGet(wanted, out var function))
            {
                throw new CliException($"No function named '{wanted}'.");
            }

            var m = function.Metadata;
            if (asJson)
            {
                Report.Write(FunctionJson(m));
                return 0;
            }

            Console.Out.WriteLine(m.Signature);
            Console.Out.WriteLine($"Category: {m.Category}");
            Console.Out.WriteLine($"Returns:  {m.ReturnKind}");
            Console.Out.WriteLine(m.Summary);
            if (m.Arguments.Count > 0)
            {
                Console.Out.WriteLine("Arguments:");
                foreach (var arg in m.Arguments)
                {
                    string flags = (arg.Required ? "required" : "optional") + (arg.Repeatable ? ", repeatable" : "");
                    Console.Out.WriteLine($"  {arg.Name} ({arg.Kind}, {flags}){(arg.Description is null ? "" : " — " + arg.Description)}");
                }
            }

            Console.Out.WriteLine($"Example: {m.Example}");
            return 0;
        });

        return command;
    }

    public static Command AddSheet()
    {
        var file = Options.File();
        var name = new Argument<string>("name") { Description = "New sheet name" };
        var at = new Option<int?>("--at") { Description = "1-based position for the new sheet (default: last)" };
        var ifMissing = new Option<bool>("--if-missing") { Description = "Succeed quietly if a sheet of that name already exists" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("add-sheet", "Add a worksheet") { file, name, at, ifMissing, inPlace, output, json };
        command.SetAction(parse => LayoutCommands.Dispatch(parse, file, inPlace, output, json, "add-sheet", new Dictionary<string, object?>
        {
            ["name"] = parse.GetValue(name),
            ["at"] = parse.GetValue(at),
            ["ifMissing"] = parse.GetValue(ifMissing) ? true : null,
        }));
        return command;
    }

    public static Command RenameSheet()
    {
        var file = Options.File();
        var oldName = new Argument<string>("old") { Description = "Existing sheet name" };
        var newName = new Argument<string>("new") { Description = "New sheet name" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("rename-sheet", "Rename a worksheet") { file, oldName, newName, inPlace, output, json };
        command.SetAction(parse => Mutate(parse, file, inPlace, output, json, session =>
        {
            session.Workbook.RenameSheet(Cells.PickSheet(session.Workbook, parse.GetValue(oldName)), parse.GetValue(newName)!);
            session.Field("sheet", parse.GetValue(newName));
        }));
        return command;
    }

    public static Command DeleteSheet()
    {
        var file = Options.File();
        var name = new Argument<string>("name") { Description = "Sheet to delete" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("delete-sheet", "Delete a worksheet (dependent formulas become #REF!)") { file, name, inPlace, output, json };
        command.SetAction(parse => Mutate(parse, file, inPlace, output, json, session =>
        {
            var sheet = Cells.PickSheet(session.Workbook, parse.GetValue(name));
            session.Apply(new DeleteSheetCommand(sheet.Name));
            session.Field("sheet", sheet.Name);
        }));
        return command;
    }

    /// <summary>
    /// The explicit spelling of what <c>cellestra &lt;file&gt;</c> also does. Both go through the
    /// family's shared launcher, which on macOS asks Launch Services for the installed bundle — the
    /// only route that reaches an already-running Cellestra rather than starting a second one, and the
    /// only one that exists at all once the app is installed as a bundle rather than published beside
    /// this binary.
    /// </summary>
    public static Command Open()
    {
        var file = Options.File("file", "Workbook to open in the Cellestra GUI");
        var command = new Command("open", "Open a workbook in the Cellestra desktop app") { file };

        command.SetAction(parse => Workspace.Launch(Path.GetFullPath(parse.GetValue(file)!)));

        return command;
    }

    public static Command Completions()
    {
        var shell = new Argument<string>("shell") { Description = "zsh | bash" };
        var command = new Command("completions", "Print a shell completion script") { shell };

        command.SetAction(parse =>
        {
            var names = CliApplication.BuildRoot().Subcommands.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal);
            string list = string.Join(" ", names);
            string which = (parse.GetValue(shell) ?? string.Empty).ToLowerInvariant();
            Console.Out.WriteLine(which switch
            {
                "zsh" => $"#compdef cellestra\n_cellestra() {{ _arguments '1:command:({list})' '*::arg:_files' }}\ncompdef _cellestra cellestra",
                "bash" => $"_cellestra() {{ COMPREPLY=( $(compgen -W \"{list}\" -- \"${{COMP_WORDS[COMP_CWORD]}}\") ); }}\ncomplete -o default -F _cellestra cellestra",
                _ => throw new CliException("Supported shells: zsh, bash."),
            });
            return 0;
        });

        return command;
    }

    // ---- Helpers ----------------------------------------------------------

    /// <summary>The edit contract every writing verb follows: nothing overwrites without --in-place or -o.</summary>
    private static int Mutate(ParseResult parse, Argument<string> file, Option<bool> inPlace, Option<string?> output, Option<bool> json, Action<EditSession> mutate)
    {
        string path = parse.GetValue(file)!;
        string target = Options.EditTarget(path, parse.GetValue(inPlace), parse.GetValue(output));
        return EditSession.Run(path, target, null, parse.GetValue(json), mutate);
    }

    private static object FunctionJson(FunctionMetadata m) => new
    {
        name = m.Name,
        signature = m.Signature,
        category = m.Category.ToString(),
        returns = m.ReturnKind.ToString(),
        summary = m.Summary,
        arguments = m.Arguments.Select(a => new
        {
            name = a.Name,
            kind = a.Kind.ToString(),
            required = a.Required,
            repeatable = a.Repeatable,
            description = a.Description,
        }).ToList(),
        example = m.Example,
    };

    private static void ClearSheet(Worksheet sheet)
    {
        // Snapshot populated positions first (can't mutate while enumerating), then clear them —
        // O(populated), and it doesn't touch the millions of empty cells in the bounding box.
        var positions = sheet.EnumerateCells().Select(c => (c.Row, c.Column)).ToList();
        foreach (var (row, column) in positions)
        {
            sheet.PutCell(row, column, null);
        }
    }

    private static IEnumerable<(int Row, int Column)> UsedUnion(Worksheet a, Worksheet b)
    {
        var seen = new HashSet<(int, int)>();
        foreach (var (row, column, _) in a.EnumerateCells())
        {
            if (seen.Add((row, column)))
            {
                yield return (row, column);
            }
        }

        foreach (var (row, column, _) in b.EnumerateCells())
        {
            if (seen.Add((row, column)))
            {
                yield return (row, column);
            }
        }
    }

    private static string ReadText(string path) => path == "-" ? Console.In.ReadToEnd() : File.ReadAllText(path);
}
