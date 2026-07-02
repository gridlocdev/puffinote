using System.CommandLine;
using Puffinote.Components.Core.Cli;
using System.Globalization;
using Cellestra.Cli.Infrastructure;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;

namespace Cellestra.Cli.Commands;

/// <summary>
/// The data + structural operations the desktop app exposes but the baseline CLI did not: sort,
/// remove-duplicates, text-to-columns, tables, data validation, named ranges, hyperlinks, insert /
/// delete rows &amp; columns, hide / unhide lines, and merge / unmerge. Each reuses the same Core edit
/// builders the GUI drives, so the result is identical, and each follows the CLI edit contract:
/// nothing overwrites without <c>--in-place</c> or <c>-o</c>, writes are atomic, and unmodeled
/// content is preserved (§8.16).
/// </summary>
internal static class DataCommands
{
    // ---- Sort -------------------------------------------------------------

    public static Command Sort()
    {
        var file = Options.File();
        var range = new Argument<string>("range") { Description = "Range to sort, e.g. Sheet1.A1:D100" };
        var by = new Option<string>("--by") { Description = "Sort keys: column letters/numbers, :desc to reverse (e.g. B,A:desc)", Required = true };
        var noHeader = new Option<bool>("--no-header") { Description = "Treat the first row as data, not a header" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var locale = Options.Locale();
        var command = new Command("sort", "Sort a range by one or more columns") { file, range, by, noHeader, inPlace, output, locale };

        command.SetAction(parse => RunEdit(parse, file, inPlace, output, locale, (wb, culture) =>
        {
            var (sheet, cellRange) = Cells.ResolveRange(wb, parse.GetValue(range)!);
            var levels = ParseSortLevels(parse.GetValue(by)!);
            var spec = new SortSpec(levels, HasHeader: !parse.GetValue(noHeader));
            return new[] { DataEditCommands.Sort(sheet.Name, cellRange, spec, culture) };
        }));

        return command;
    }

    private static List<SortLevel> ParseSortLevels(string spec)
    {
        var levels = new List<SortLevel>();
        foreach (var token in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = token.Split(':', 2);
            bool ascending = parts.Length < 2 || parts[1].Trim().ToLowerInvariant() is not ("desc" or "descending" or "d");
            levels.Add(new SortLevel(ParseColumn(parts[0].Trim()), ascending));
        }

        if (levels.Count == 0)
        {
            throw new CliException("--by needs at least one column, e.g. --by B or --by B,A:desc.");
        }

        return levels;
    }

    // ---- Remove duplicates -----------------------------------------------

    public static Command RemoveDuplicates()
    {
        var file = Options.File();
        var range = new Argument<string>("range") { Description = "Range to de-dup, e.g. Sheet1.A1:D100" };
        var columns = new Option<string?>("--columns") { Description = "Key columns to compare (default: all), e.g. A,C" };
        var noHeader = new Option<bool>("--no-header") { Description = "Treat the first row as data, not a header" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var command = new Command("remove-duplicates", "Drop repeated rows from a range") { file, range, columns, noHeader, inPlace, output };

        command.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            string target = Options.EditTarget(path, parse.GetValue(inPlace), parse.GetValue(output));
            var loaded = Workspace.Load(path, WorkbookFormat.Ods, null);
            var (sheet, cellRange) = Cells.ResolveRange(loaded.Workbook, parse.GetValue(range)!);
            var keyColumns = (parse.GetValue(columns) ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseColumn).ToList();

            var edit = DataEditCommands.RemoveDuplicates(sheet.Name, cellRange, keyColumns, hasHeader: !parse.GetValue(noHeader), out var removed);
            edit.Apply(loaded.Workbook);
            Workspace.Recalculate(loaded.Workbook);
            Workspace.SaveOds(loaded.Workbook, loaded.Package, target);
            Console.Error.WriteLine($"Removed {removed()} duplicate row(s).");
            return 0;
        });

        return command;
    }

    // ---- Text to columns --------------------------------------------------

    public static Command Split()
    {
        var file = Options.File();
        var range = new Argument<string>("column") { Description = "Single-column range to split, e.g. Sheet1.A1:A100" };
        var delimiter = new Option<string>("--delimiter", "-d") { Description = "Delimiter to split on (default ,)" };
        delimiter.DefaultValueFactory = _ => ",";
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var command = new Command("split", "Split a delimited column into multiple columns (text to columns)") { file, range, delimiter, inPlace, output };

        command.SetAction(parse => RunEdit(parse, file, inPlace, output, locale: null, (wb, _) =>
        {
            var (sheet, cellRange) = Cells.ResolveRange(wb, parse.GetValue(range)!);
            string d = parse.GetValue(delimiter) ?? ",";
            if (d.Length != 1)
            {
                throw new CliException("--delimiter must be a single character.");
            }

            return new[] { DataEditCommands.TextToColumns(sheet, cellRange, d[0]) };
        }));

        return command;
    }

    // ---- Tables -----------------------------------------------------------

    public static Command Table()
    {
        var file = Options.File();
        var range = new Argument<string>("range") { Description = "Range to turn into a table, e.g. Sheet1.A1:D20" };
        var name = new Option<string?>("--name") { Description = "Table name (default: the next free Table N)" };
        var noHeader = new Option<bool>("--no-header") { Description = "The range has no header row" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var command = new Command("table", "Turn a range into a table (database range)") { file, range, name, noHeader, inPlace, output };

        command.SetAction(parse => RunEdit(parse, file, inPlace, output, locale: null, (wb, _) =>
        {
            var (sheet, cellRange) = Cells.ResolveRange(wb, parse.GetValue(range)!);
            if (sheet.Tables.Any(t => t.Range.Intersects(cellRange)))
            {
                throw new CliException("The range overlaps an existing table; a cell can belong to only one table.");
            }

            string tableName = parse.GetValue(name) ?? NextTableName(sheet);
            var table = new Core.Data.Table(tableName, cellRange, HasHeader: !parse.GetValue(noHeader));
            return new[] { DataEditCommands.CreateTable(wb, sheet.Name, table) };
        }));

        return command;
    }

    private static string NextTableName(Worksheet sheet)
    {
        var taken = new HashSet<string>(sheet.Tables.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
        {
            string candidate = $"Table{i}";
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }
    }

    // ---- Data validation --------------------------------------------------

    public static Command Validation()
    {
        var file = Options.File();
        var range = new Argument<string>("range") { Description = "Range to validate, e.g. Sheet1.A1:A20" };
        var list = new Option<string?>("--list") { Description = "Allowed values, comma-separated (e.g. Red,Green,Blue)" };
        var source = new Option<string?>("--source") { Description = "A cell range whose values are the allowed list (e.g. Lists.A1:A8)" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var command = new Command("data-validation", "Restrict a range to a list of allowed values") { file, range, list, source, inPlace, output };

        command.SetAction(parse => RunEdit(parse, file, inPlace, output, locale: null, (wb, _) =>
        {
            var (sheet, cellRange) = Cells.ResolveRange(wb, parse.GetValue(range)!);
            string? listText = parse.GetValue(list);
            string? sourceText = parse.GetValue(source);
            var rule = new DataValidation(ValidationKind.List, cellRange);

            if (!string.IsNullOrWhiteSpace(sourceText))
            {
                if (!CellRange.TryParse(sourceText, out var listSource))
                {
                    throw new CliException($"'{sourceText}' is not a valid range for --source.");
                }

                rule = rule with { ListSource = listSource };
            }
            else if (!string.IsNullOrWhiteSpace(listText))
            {
                rule = rule with
                {
                    ListValues = listText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                };
            }
            else
            {
                throw new CliException("Give either --list \"a,b,c\" or --source <range>.");
            }

            return new[] { DataEditCommands.SetValidation(sheet.Name, rule) };
        }));

        return command;
    }

    // ---- Named ranges -----------------------------------------------------

    public static Command Name()
    {
        var command = new Command("name", "Manage named ranges");
        command.Subcommands.Add(NameList());
        command.Subcommands.Add(NameAdd());
        command.Subcommands.Add(NameRemove());
        return command;
    }

    private static Command NameList()
    {
        var file = Options.File();
        var command = new Command("list", "List the workbook's named ranges") { file };
        command.SetAction(parse =>
        {
            var loaded = Workspace.Load(parse.GetValue(file)!, WorkbookFormat.Ods, null);
            foreach (var named in loaded.Workbook.NamedRanges)
            {
                string scope = named.IsGlobal ? "workbook" : named.Scope!;
                Console.Out.WriteLine($"{named.Name}\t{named.Target}\t{scope}");
            }

            return 0;
        });
        return command;
    }

    private static Command NameAdd()
    {
        var file = Options.File();
        var nameArg = new Argument<string>("name") { Description = "Name to define, e.g. TaxRate" };
        var range = new Argument<string>("range") { Description = "Range it points to, e.g. Sheet1.B1:B10" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var command = new Command("add", "Define a workbook-scoped named range") { file, nameArg, range, inPlace, output };

        command.SetAction(parse => RunEdit(parse, file, inPlace, output, locale: null, (wb, _) =>
        {
            var (sheet, cellRange) = Cells.ResolveRange(wb, parse.GetValue(range)!);
            // Store fully sheet-qualified so the name resolves regardless of the active sheet.
            var target = cellRange.WithSheet(cellRange.Sheet ?? sheet.Name);
            wb.AddNamedRange(new NamedRange(parse.GetValue(nameArg)!, target));
            return Array.Empty<IEditCommand>();
        }));

        return command;
    }

    private static Command NameRemove()
    {
        var file = Options.File();
        var nameArg = new Argument<string>("name") { Description = "Name to remove" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var command = new Command("remove", "Remove a named range") { file, nameArg, inPlace, output };

        command.SetAction(parse => RunEdit(parse, file, inPlace, output, locale: null, (wb, _) =>
        {
            if (!wb.RemoveNamedRange(parse.GetValue(nameArg)!))
            {
                throw new CliException($"No named range called '{parse.GetValue(nameArg)}'.");
            }

            return Array.Empty<IEditCommand>();
        }));

        return command;
    }

    // ---- Hyperlinks -------------------------------------------------------

    public static Command Hyperlink()
    {
        var file = Options.File();
        var cell = new Argument<string>("cell") { Description = "Cell to link, e.g. Sheet1.B2" };
        var url = new Argument<string?>("url") { Description = "Link target (omit with --remove)" };
        url.Arity = ArgumentArity.ZeroOrOne;
        var tooltip = new Option<string?>("--tooltip") { Description = "Hover tooltip" };
        var remove = new Option<bool>("--remove") { Description = "Remove the cell's hyperlink" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var command = new Command("hyperlink", "Add, change, or remove a cell's hyperlink") { file, cell, url, tooltip, remove, inPlace, output };

        command.SetAction(parse => RunEdit(parse, file, inPlace, output, locale: null, (wb, _) =>
        {
            var (sheet, address) = Cells.ResolveCell(wb, parse.GetValue(cell)!);
            Core.Model.Hyperlink? link = null;
            if (!parse.GetValue(remove))
            {
                string? target = parse.GetValue(url);
                if (string.IsNullOrWhiteSpace(target))
                {
                    throw new CliException("Give a url, or pass --remove to clear the link.");
                }

                link = new Core.Model.Hyperlink(target) { Tooltip = parse.GetValue(tooltip) };
            }

            return new[] { EditCommands.SetHyperlink(wb, sheet.Name, address.Row, address.Column, link) };
        }));

        return command;
    }

    // ---- Insert / delete rows & columns -----------------------------------

    public static Command InsertRows() => StructuralCommand("insert-rows", "Insert rows before a row", StructuralAxis.Row, StructuralOperation.Insert);

    public static Command DeleteRows() => StructuralCommand("delete-rows", "Delete rows", StructuralAxis.Row, StructuralOperation.Delete);

    public static Command InsertColumns() => StructuralCommand("insert-cols", "Insert columns before a column", StructuralAxis.Column, StructuralOperation.Insert);

    public static Command DeleteColumns() => StructuralCommand("delete-cols", "Delete columns", StructuralAxis.Column, StructuralOperation.Delete);

    private static Command StructuralCommand(string verb, string description, StructuralAxis axis, StructuralOperation operation)
    {
        var file = Options.File();
        bool isRow = axis == StructuralAxis.Row;
        var at = new Argument<string>("at")
        {
            Description = isRow ? "Row number (1-based), e.g. 3" : "Column letter or number, e.g. B",
        };
        var count = new Option<int>("--count") { Description = "How many lines (default 1)" };
        count.DefaultValueFactory = _ => 1;
        var sheet = Options.Sheet();
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var command = new Command(verb, description) { file, at, count, sheet, inPlace, output };

        command.SetAction(parse => RunEdit(parse, file, inPlace, output, locale: null, (wb, _) =>
        {
            var worksheet = Cells.PickSheet(wb, parse.GetValue(sheet));
            int index = isRow ? ParseRow(parse.GetValue(at)!) : ParseColumn(parse.GetValue(at)!);
            int n = parse.GetValue(count);
            if (n < 1)
            {
                throw new CliException("--count must be at least 1.");
            }

            return new[] { (IEditCommand)new StructuralEditCommand(worksheet.Name, axis, operation, index, n) };
        }));

        return command;
    }

    // ---- Hide / unhide rows & columns -------------------------------------

    public static Command HideRows() => VisibilityCommand("hide-rows", "Hide rows", isRow: true, hidden: true);

    public static Command ShowRows() => VisibilityCommand("show-rows", "Unhide rows", isRow: true, hidden: false);

    public static Command HideColumns() => VisibilityCommand("hide-cols", "Hide columns", isRow: false, hidden: true);

    public static Command ShowColumns() => VisibilityCommand("show-cols", "Unhide columns", isRow: false, hidden: false);

    private static Command VisibilityCommand(string verb, string description, bool isRow, bool hidden)
    {
        var file = Options.File();
        var lines = new Argument<string>("lines")
        {
            Description = isRow ? "Row(s), e.g. 3 or 3:5" : "Column(s), e.g. B or B:D",
        };
        var sheet = Options.Sheet();
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var command = new Command(verb, description) { file, lines, sheet, inPlace, output };

        command.SetAction(parse => RunEdit(parse, file, inPlace, output, locale: null, (wb, _) =>
        {
            var worksheet = Cells.PickSheet(wb, parse.GetValue(sheet));
            var (first, last) = isRow ? ParseRowSpan(parse.GetValue(lines)!) : ParseColumnSpan(parse.GetValue(lines)!);
            var edit = isRow
                ? EditCommands.SetRowsHidden(worksheet.Name, first, last, hidden)
                : EditCommands.SetColumnsHidden(worksheet.Name, first, last, hidden);
            return new[] { edit };
        }));

        return command;
    }

    // ---- Merge / unmerge --------------------------------------------------

    public static Command Merge()
    {
        var file = Options.File();
        var range = new Argument<string>("range") { Description = "Range to merge into one cell, e.g. Sheet1.A1:B2" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var command = new Command("merge", "Merge a range into a single cell") { file, range, inPlace, output };

        command.SetAction(parse => RunEdit(parse, file, inPlace, output, locale: null, (wb, _) =>
        {
            var (sheet, cellRange) = Cells.ResolveRange(wb, parse.GetValue(range)!);
            return new[] { EditCommands.MergeCells(sheet.Name, cellRange) };
        }));

        return command;
    }

    public static Command Unmerge()
    {
        var file = Options.File();
        var cell = new Argument<string>("cell") { Description = "Any cell of the merged region, e.g. Sheet1.A1" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var command = new Command("unmerge", "Split a merged region back into individual cells") { file, cell, inPlace, output };

        command.SetAction(parse => RunEdit(parse, file, inPlace, output, locale: null, (wb, _) =>
        {
            var (sheet, address) = Cells.ResolveCell(wb, parse.GetValue(cell)!);
            return new[] { EditCommands.UnmergeCells(sheet.Name, address.Row, address.Column) };
        }));

        return command;
    }

    // ---- Shared edit flow & parsing --------------------------------------

    /// <summary>Load, run the built edit commands, recalc, and save atomically to the resolved target.</summary>
    private static int RunEdit(
        ParseResult parse,
        Argument<string> file,
        Option<bool> inPlace,
        Option<string?> output,
        Option<string?>? locale,
        Func<Workbook, CultureInfo, IEnumerable<IEditCommand>> build)
    {
        string path = parse.GetValue(file)!;
        string target = Options.EditTarget(path, parse.GetValue(inPlace), parse.GetValue(output));
        var culture = locale is null ? null : Locale.Resolve(parse.GetValue(locale));
        var loaded = Workspace.Load(path, WorkbookFormat.Ods, culture);
        var effective = culture ?? loaded.Workbook.Settings.EffectiveLocale;

        foreach (var edit in build(loaded.Workbook, effective))
        {
            edit.Apply(loaded.Workbook);
        }

        Workspace.Recalculate(loaded.Workbook);
        Workspace.SaveOds(loaded.Workbook, loaded.Package, target);
        return 0;
    }

    /// <summary>A column reference — a letter (A, AB) or a 1-based number — as a 0-based index.</summary>
    private static int ParseColumn(string token)
    {
        token = token.Trim();
        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int oneBased))
        {
            if (oneBased < 1)
            {
                throw new CliException($"Column number '{token}' must be 1 or greater.");
            }

            return oneBased - 1;
        }

        if (SheetDimensions.TryLettersToColumn(token, out int column))
        {
            return column;
        }

        throw new CliException($"'{token}' is not a valid column (use a letter like B or a 1-based number).");
    }

    /// <summary>A 1-based row number as a 0-based index.</summary>
    private static int ParseRow(string token)
    {
        if (!int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int oneBased) || oneBased < 1)
        {
            throw new CliException($"'{token}' is not a valid row number (1 or greater).");
        }

        return oneBased - 1;
    }

    private static (int First, int Last) ParseRowSpan(string token)
    {
        var (a, b) = SplitSpan(token);
        int first = ParseRow(a);
        int last = b is null ? first : ParseRow(b);
        return Order(first, last);
    }

    private static (int First, int Last) ParseColumnSpan(string token)
    {
        var (a, b) = SplitSpan(token);
        int first = ParseColumn(a);
        int last = b is null ? first : ParseColumn(b);
        return Order(first, last);
    }

    private static (string A, string? B) SplitSpan(string token)
    {
        string[] parts = token.Split(':', 2, StringSplitOptions.TrimEntries);
        return (parts[0], parts.Length > 1 ? parts[1] : null);
    }

    private static (int First, int Last) Order(int a, int b) => a <= b ? (a, b) : (b, a);
}
