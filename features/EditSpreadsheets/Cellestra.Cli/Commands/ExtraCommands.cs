using System.CommandLine;
using System.Globalization;
using System.Text.RegularExpressions;
using Cellestra.Cli.Infrastructure;
using Cellestra.Core.Editing;
using Cellestra.Core.Formulas.Functions;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Csv;
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
        var range = new Argument<string>("range") { Description = "Range to aggregate, e.g. Sheet1.A1:A100" };
        var locale = Options.Locale();
        var json = Options.Json();
        var command = new Command("stats", "Aggregate a range: sum, average, count, min, max") { file, range, locale, json };

        command.SetAction(parse =>
        {
            var culture = Locale.Resolve(parse.GetValue(locale));
            var loaded = Workspace.Load(parse.GetValue(file)!, null, culture);
            Workspace.Recalculate(loaded.Workbook);
            var (worksheet, cellRange) = Cells.ResolveRange(loaded.Workbook, parse.GetValue(range)!);

            int count = 0;
            int numeric = 0;
            double sum = 0, min = double.PositiveInfinity, max = double.NegativeInfinity;

            // Walk only the populated cells (O(populated)), not every position in the bounding box —
            // so `stats Sheet1.A1:A1048576` over a sparse column stays fast.
            foreach (var (row, column, cell) in worksheet.EnumerateCells())
            {
                if (!cellRange.Contains(row, column) || cell.Value.IsEmpty)
                {
                    continue;
                }

                count++;
                if (cell.Value.IsNumber)
                {
                    numeric++;
                    double n = cell.Value.AsNumber;
                    sum += n;
                    min = Math.Min(min, n);
                    max = Math.Max(max, n);
                }
            }

            if (parse.GetValue(json))
            {
                Report.Write(new
                {
                    sheet = worksheet.Name,
                    range = cellRange.ToString(),
                    count,
                    numeric,
                    sum,
                    average = numeric > 0 ? sum / numeric : (double?)null,
                    min = numeric > 0 ? min : (double?)null,
                    max = numeric > 0 ? max : (double?)null,
                });
                return 0;
            }

            var invariant = CultureInfo.InvariantCulture;
            Console.Out.WriteLine($"count\t{count}");
            Console.Out.WriteLine($"numeric\t{numeric}");
            Console.Out.WriteLine($"sum\t{sum.ToString("0.##########", invariant)}");
            Console.Out.WriteLine($"average\t{(numeric > 0 ? (sum / numeric).ToString("0.##########", invariant) : "")}");
            Console.Out.WriteLine($"min\t{(numeric > 0 ? min.ToString("0.##########", invariant) : "")}");
            Console.Out.WriteLine($"max\t{(numeric > 0 ? max.ToString("0.##########", invariant) : "")}");
            return 0;
        });

        return command;
    }

    public static Command Find()
    {
        var file = Options.File();
        var term = new Argument<string>("term") { Description = "Text to search for" };
        var regex = new Option<bool>("--regex") { Description = "Treat the term as a regular expression" };
        var into = new Option<string>("--in")
        {
            Description = "Where to search: values | formulas | both",
            DefaultValueFactory = _ => "both"
        };
        var locale = Options.Locale();
        var json = Options.Json();
        var command = new Command("find", "Search cells by value or formula") { file, term, regex, into, locale, json };

        command.SetAction(parse =>
        {
            var culture = Locale.Resolve(parse.GetValue(locale));
            var loaded = Workspace.Load(parse.GetValue(file)!, null, culture);
            Workspace.Recalculate(loaded.Workbook);
            var effectiveCulture = culture ?? loaded.Workbook.Settings.EffectiveLocale;

            string where = (parse.GetValue(into) ?? "both").ToLowerInvariant();
            bool searchValues = where is "values" or "both";
            bool searchFormulas = where is "formulas" or "both";
            string termText = parse.GetValue(term) ?? string.Empty;
            Regex? pattern = null;
            if (parse.GetValue(regex))
            {
                try
                {
                    pattern = new Regex(termText, RegexOptions.None);
                }
                catch (ArgumentException ex)
                {
                    throw new CliException($"Invalid regex '{termText}': {ex.Message}");
                }
            }

            bool Matches(string candidate) => pattern is not null
                ? pattern.IsMatch(candidate)
                : candidate.Contains(termText, StringComparison.OrdinalIgnoreCase);

            bool asJson = parse.GetValue(json);
            int hits = 0;
            var matches = new List<CellReport>();
            foreach (var worksheet in loaded.Workbook.Worksheets)
            {
                foreach (var (row, column, cell) in worksheet.EnumerateCells())
                {
                    bool match =
                        (searchFormulas && cell.IsFormula && Matches("=" + cell.Formula)) ||
                        (searchValues && Matches(Cells.Display(loaded.Workbook, worksheet, row, column, effectiveCulture)));
                    if (!match)
                    {
                        continue;
                    }

                    hits++;
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

            return hits > 0 ? 0 : 1;
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
        var json = Options.Json();
        var command = new Command("diff", "Compare two workbooks by value/structure (exit 1 if they differ)") { left, right, format, json };

        command.SetAction(parse =>
        {
            var a = Workspace.Load(parse.GetValue(left)!, null, null);
            var b = Workspace.Load(parse.GetValue(right)!, null, null);
            Workspace.Recalculate(a.Workbook);
            Workspace.Recalculate(b.Workbook);
            var culture = CultureInfo.InvariantCulture;
            bool asJson = parse.GetValue(json);

            var addedSheets = new List<string>();
            var removedSheets = new List<string>();
            var cells = new List<object>();
            var sheetNames = a.Workbook.Worksheets.Select(s => s.Name)
                .Union(b.Workbook.Worksheets.Select(s => s.Name), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var name in sheetNames)
            {
                var sheetA = a.Workbook.FindSheet(name);
                var sheetB = b.Workbook.FindSheet(name);
                if (sheetA is null)
                {
                    addedSheets.Add(name);
                    if (!asJson)
                    {
                        Console.Out.WriteLine($"+ sheet {name}");
                    }

                    continue;
                }

                if (sheetB is null)
                {
                    removedSheets.Add(name);
                    if (!asJson)
                    {
                        Console.Out.WriteLine($"- sheet {name}");
                    }

                    continue;
                }

                var union = UsedUnion(sheetA, sheetB);
                foreach (var (row, column) in union)
                {
                    string left1 = Cells.FormulaOrValue(a.Workbook, sheetA, row, column, culture);
                    string right1 = Cells.FormulaOrValue(b.Workbook, sheetB, row, column, culture);
                    if (string.Equals(left1, right1, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var address = new CellAddress(column, row, sheet: name);
                    cells.Add(new { cell = address.ToString(), before = left1, after = right1 });
                    if (!asJson)
                    {
                        Console.Out.WriteLine($"{address}: \"{left1}\" -> \"{right1}\"");
                    }
                }
            }

            int differences = addedSheets.Count + removedSheets.Count + cells.Count;
            if (asJson)
            {
                Report.Write(new { same = differences == 0, differences, addedSheets, removedSheets, cells });
            }

            return differences > 0 ? 1 : 0;
        });

        return command;
    }

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
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("add-sheet", "Add a worksheet") { file, name, inPlace, output, json };
        command.SetAction(parse => Mutate(parse, file, inPlace, output, json, session =>
        {
            var sheet = session.Workbook.AddSheet(parse.GetValue(name)!);
            session.Field("sheet", sheet.Name);
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
