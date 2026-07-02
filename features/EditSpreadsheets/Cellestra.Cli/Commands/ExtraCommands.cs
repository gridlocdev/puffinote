using System.CommandLine;
using Puffinote.Components.Core.Cli;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cellestra.Cli.Infrastructure;
using Cellestra.Core.Editing;
using Cellestra.Core.Formulas.Functions;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Csv;
using Cellestra.Core.Values;

namespace Cellestra.Cli.Commands;

/// <summary>The nice-to-haves: new, import/export, stats, find, diff, functions, sheet management, apply, open, completions.</summary>
internal static class ExtraCommands
{
    public static Command New()
    {
        var file = Options.File("file", "Workbook to create (.ods)");
        var sheets = new Option<string?>("--sheets") { Description = "Comma-separated sheet names (default: Sheet1)" };
        var command = new Command("new", "Create a new workbook") { file, sheets };

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

            Workspace.SaveOds(workbook, null, path);
            return 0;
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
        var command = new Command("import", "Import a CSV into a sheet of an .ods workbook") { csvFile, into, sheet, delimiter, locale };

        command.SetAction(parse =>
        {
            string target = parse.GetValue(into)!;
            string sheetName = parse.GetValue(sheet)!;
            var culture = Locale.Resolve(parse.GetValue(locale));
            var csv = Options.Csv(parse.GetValue(delimiter));
            string text = ReadCsv(parse.GetValue(csvFile)!);

            var loaded = File.Exists(target)
                ? Workspace.Load(target, WorkbookFormat.Ods, culture)
                : new LoadedWorkbook(Workbook.CreateNew(), null);
            var workbook = loaded.Workbook;

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
            Workspace.Recalculate(workbook);
            Workspace.SaveOds(workbook, loaded.Package, target);
            return 0;
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
        var command = new Command("stats", "Aggregate a range: sum, average, count, min, max") { file, range, locale };

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
        var into = new Option<string>("--in") { Description = "Where to search: values | formulas | both" };
        into.DefaultValueFactory = _ => "both";
        var locale = Options.Locale();
        var command = new Command("find", "Search cells by value or formula") { file, term, regex, into, locale };

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

            int hits = 0;
            foreach (var worksheet in loaded.Workbook.Worksheets)
            {
                foreach (var (row, column, cell) in worksheet.EnumerateCells())
                {
                    bool match =
                        (searchFormulas && cell.IsFormula && Matches("=" + cell.Formula)) ||
                        (searchValues && Matches(Cells.Display(loaded.Workbook, worksheet, row, column, effectiveCulture)));
                    if (match)
                    {
                        var address = new CellAddress(column, row, sheet: worksheet.Name);
                        Console.Out.WriteLine($"{address}\t{Cells.FormulaOrValue(loaded.Workbook, worksheet, row, column, effectiveCulture)}");
                        hits++;
                    }
                }
            }

            return hits > 0 ? 0 : 1;
        });

        return command;
    }

    public static Command Diff()
    {
        var left = Options.File("before", "First workbook");
        var right = Options.File("after", "Second workbook");
        var format = new Option<string>("--format", "-f") { Description = "unified (default)" };
        format.DefaultValueFactory = _ => "unified";
        var command = new Command("diff", "Compare two workbooks by value/structure (exit 1 if they differ)") { left, right, format };

        command.SetAction(parse =>
        {
            var a = Workspace.Load(parse.GetValue(left)!, null, null);
            var b = Workspace.Load(parse.GetValue(right)!, null, null);
            Workspace.Recalculate(a.Workbook);
            Workspace.Recalculate(b.Workbook);
            var culture = CultureInfo.InvariantCulture;

            int differences = 0;
            var sheetNames = a.Workbook.Worksheets.Select(s => s.Name)
                .Union(b.Workbook.Worksheets.Select(s => s.Name), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var name in sheetNames)
            {
                var sheetA = a.Workbook.FindSheet(name);
                var sheetB = b.Workbook.FindSheet(name);
                if (sheetA is null)
                {
                    Console.Out.WriteLine($"+ sheet {name}");
                    differences++;
                    continue;
                }

                if (sheetB is null)
                {
                    Console.Out.WriteLine($"- sheet {name}");
                    differences++;
                    continue;
                }

                var union = UsedUnion(sheetA, sheetB);
                foreach (var (row, column) in union)
                {
                    string left1 = Cells.FormulaOrValue(a.Workbook, sheetA, row, column, culture);
                    string right1 = Cells.FormulaOrValue(b.Workbook, sheetB, row, column, culture);
                    if (!string.Equals(left1, right1, StringComparison.Ordinal))
                    {
                        var address = new CellAddress(column, row, sheet: name);
                        Console.Out.WriteLine($"{address}: \"{left1}\" -> \"{right1}\"");
                        differences++;
                    }
                }
            }

            return differences > 0 ? 1 : 0;
        });

        return command;
    }

    public static Command Functions()
    {
        var name = new Argument<string?>("name") { Description = "A function to describe (omit to list all)" };
        name.Arity = ArgumentArity.ZeroOrOne;
        var command = new Command("functions", "List supported functions, or describe one") { name };

        command.SetAction(parse =>
        {
            var registry = FunctionRegistry.CreateDefault();
            string? wanted = parse.GetValue(name);
            if (string.IsNullOrEmpty(wanted))
            {
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
        var command = new Command("add-sheet", "Add a worksheet") { file, name };
        command.SetAction(parse => MutateInPlace(parse.GetValue(file)!, wb => wb.AddSheet(parse.GetValue(name)!)));
        return command;
    }

    public static Command RenameSheet()
    {
        var file = Options.File();
        var oldName = new Argument<string>("old") { Description = "Existing sheet name" };
        var newName = new Argument<string>("new") { Description = "New sheet name" };
        var command = new Command("rename-sheet", "Rename a worksheet") { file, oldName, newName };
        command.SetAction(parse => MutateInPlace(parse.GetValue(file)!, wb =>
            wb.RenameSheet(Cells.PickSheet(wb, parse.GetValue(oldName)), parse.GetValue(newName)!)));
        return command;
    }

    public static Command DeleteSheet()
    {
        var file = Options.File();
        var name = new Argument<string>("name") { Description = "Sheet to delete" };
        var command = new Command("delete-sheet", "Delete a worksheet (dependent formulas become #REF!)") { file, name };
        command.SetAction(parse => MutateInPlace(parse.GetValue(file)!, wb =>
        {
            var sheet = Cells.PickSheet(wb, parse.GetValue(name));
            new DeleteSheetCommand(sheet.Name).Apply(wb);
        }));
        return command;
    }

    public static Command Apply()
    {
        var file = Options.File();
        var edits = Options.File("edits", "JSON file of edits (or - for stdin)");
        var locale = Options.Locale();
        var command = new Command("apply", "Apply a batch of cell edits from JSON") { file, edits, locale };

        command.SetAction(parse =>
        {
            var culture = Locale.Resolve(parse.GetValue(locale));
            var loaded = Workspace.Load(parse.GetValue(file)!, WorkbookFormat.Ods, culture);
            var effectiveCulture = culture ?? loaded.Workbook.Settings.EffectiveLocale;

            string json = ReadCsv(parse.GetValue(edits)!); // reads file or stdin text
            EditEntry[] entries;
            try
            {
                entries = JsonSerializer.Deserialize<EditEntry[]>(json, JsonOptions) ?? Array.Empty<EditEntry>();
            }
            catch (JsonException ex)
            {
                throw new CliException($"Invalid edits JSON: {ex.Message}");
            }

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Cell))
                {
                    throw new CliException("Each edit needs a \"cell\".");
                }

                var (worksheet, address) = Cells.ResolveCell(loaded.Workbook, entry.Cell);
                string input = entry.Formula ?? entry.Value ?? string.Empty;
                EditCommands.SetInput(loaded.Workbook, worksheet.Name, address.Row, address.Column, input, effectiveCulture)
                    .Apply(loaded.Workbook);
            }

            Workspace.Recalculate(loaded.Workbook);
            Workspace.SaveOds(loaded.Workbook, loaded.Package, parse.GetValue(file)!);
            Console.Error.WriteLine($"Applied {entries.Length} edit(s).");
            return 0;
        });

        return command;
    }

    public static Command Open()
    {
        var file = Options.File("file", "Workbook to open in the Cellestra GUI");
        var command = new Command("open", "Open a workbook in the Cellestra desktop app") { file };

        command.SetAction(parse =>
        {
            string path = Path.GetFullPath(parse.GetValue(file)!);
            if (!File.Exists(path))
            {
                throw new CliException($"File not found: {path}");
            }

            string? desktop = FindDesktopExecutable();
            try
            {
                if (desktop is not null)
                {
                    Process.Start(new ProcessStartInfo(desktop, $"\"{path}\"") { UseShellExecute = false });
                }
                else
                {
                    // Fall back to the OS default handler for the file.
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                throw new CliException($"Could not launch the GUI: {ex.Message}");
            }

            return 0;
        });

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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record EditEntry(string Cell, string? Value, string? Formula);

    private static int MutateInPlace(string path, Action<Workbook> mutate)
    {
        var loaded = Workspace.Load(path, WorkbookFormat.Ods, null);
        mutate(loaded.Workbook);
        Workspace.Recalculate(loaded.Workbook);
        Workspace.SaveOds(loaded.Workbook, loaded.Package, path);
        return 0;
    }

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

    private static string ReadCsv(string path) => path == "-" ? Console.In.ReadToEnd() : File.ReadAllText(path);

    private static string? FindDesktopExecutable()
    {
        string baseDir = AppContext.BaseDirectory;
        string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Cellestra.Desktop.exe" : "Cellestra.Desktop";
        string[] candidates =
        {
            Path.Combine(baseDir, exeName),
            Path.Combine(baseDir, "..", "..", "..", "..", "Cellestra.Desktop", "bin", "Debug", "net10.0", exeName),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
