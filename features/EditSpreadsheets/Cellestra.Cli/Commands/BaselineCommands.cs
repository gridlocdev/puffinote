using System.CommandLine;
using System.Globalization;
using Cellestra.Cli.Infrastructure;
using Cellestra.Core.Editing;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Formulas.Functions;
using Cellestra.Core.Formulas.Parsing;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Csv;
using Puffinote.Components.Core.Cli;

namespace Cellestra.Cli.Commands;

/// <summary>The genuinely useful core: convert, cat, get, set, recalc, sheets, info, eval, validate.</summary>
internal static class BaselineCommands
{
    public static Command Convert()
    {
        var input = Options.File("input", "Source file (or - for stdin)");
        var output = Options.File("output", "Destination file (or - for stdout)");
        var from = Options.From();
        var to = Options.To();
        var sheet = Options.Sheet();
        var locale = Options.Locale();
        var delimiter = Options.Delimiter();

        var command = new Command("convert", "Convert between .ods and CSV (both directions)")
        {
            input, output, from, to, sheet, locale, delimiter,
        };

        command.SetAction(parse =>
        {
            string inputPath = parse.GetValue(input)!;
            string outputPath = parse.GetValue(output)!;
            var culture = Locale.Resolve(parse.GetValue(locale));
            var csv = Options.Csv(parse.GetValue(delimiter));

            var loaded = Workspace.Load(inputPath, Options.FormatOverride(parse.GetValue(from)), culture, csv);
            var outFormat = Workspace.DetectFormat(outputPath, Options.FormatOverride(parse.GetValue(to)));

            if (outFormat == WorkbookFormat.Ods)
            {
                if (parse.GetValue(sheet) is { } name && loaded.Package is null)
                {
                    loaded.Workbook.RenameSheet(loaded.Workbook.ActiveSheet, name);
                }

                Workspace.Recalculate(loaded.Workbook);
                Workspace.SaveOds(loaded.Workbook, loaded.Package, outputPath);
            }
            else
            {
                var worksheet = Cells.PickSheet(loaded.Workbook, parse.GetValue(sheet));
                Workspace.Recalculate(loaded.Workbook);
                Workspace.SaveText(CsvWriter.Write(loaded.Workbook, worksheet, csv), outputPath);
            }

            return 0;
        });

        return command;
    }

    public static Command Cat()
    {
        var file = Options.File();
        var sheet = Options.Sheet();
        var range = Options.Range();
        var format = Options.Format();
        var formula = Options.Formula();
        var locale = Options.Locale();
        var from = Options.From();
        var json = Options.Json();

        var command = new Command("cat", "Print a sheet or range to the terminal")
        {
            file, sheet, range, format, formula, locale, from, json,
        };

        command.SetAction(parse =>
        {
            var culture = Locale.Resolve(parse.GetValue(locale));
            var loaded = Workspace.Load(parse.GetValue(file)!, Options.FormatOverride(parse.GetValue(from)), culture);
            Workspace.Recalculate(loaded.Workbook);
            var effectiveCulture = culture ?? loaded.Workbook.Settings.EffectiveLocale;

            string? rangeText = parse.GetValue(range);
            Worksheet worksheet;
            CellRange cellRange;
            if (rangeText is not null)
            {
                (worksheet, cellRange) = Cells.ResolveRange(loaded.Workbook, rangeText, parse.GetValue(sheet));
            }
            else
            {
                worksheet = Cells.PickSheet(loaded.Workbook, parse.GetValue(sheet));
                cellRange = Cells.UsedRangeOrA1(worksheet);
            }

            var grid = Cells.BuildGrid(loaded.Workbook, worksheet, cellRange, effectiveCulture, parse.GetValue(formula));
            var outputFormat = parse.GetValue(json) ? OutputFormat.Json : Output.ParseFormat(parse.GetValue(format));
            if (outputFormat == OutputFormat.Json)
            {
                // The grid of display strings for a quick look, plus every populated cell in full —
                // typed value, formula, error, format — which is what a script actually acts on.
                var cells = worksheet.EnumerateCells()
                    .Where(c => cellRange.Contains(c.Row, c.Column))
                    .OrderBy(c => c.Row).ThenBy(c => c.Column)
                    .Select(c => Report.Cell(loaded.Workbook, worksheet, c.Row, c.Column, effectiveCulture))
                    .ToList();
                Report.Write(new { sheet = worksheet.Name, range = cellRange.ToString(), rows = grid, cells });
                return 0;
            }

            Console.Out.Write(Output.Render(outputFormat, worksheet.Name, cellRange, grid));
            return 0;
        });

        return command;
    }

    public static Command Get()
    {
        var file = Options.File();
        var cell = new Argument<string>("cell") { Description = "Cell reference, e.g. Sheet1.B7 or B7" };
        var formula = Options.Formula();
        var locale = Options.Locale();
        var json = Options.Json();

        var command = new Command("get", "Read one cell's value (or --formula for its source)")
        {
            file, cell, formula, locale, json,
        };

        command.SetAction(parse =>
        {
            var culture = Locale.Resolve(parse.GetValue(locale));
            var loaded = Workspace.Load(parse.GetValue(file)!, null, culture);
            Workspace.Recalculate(loaded.Workbook);
            var effectiveCulture = culture ?? loaded.Workbook.Settings.EffectiveLocale;

            var (worksheet, address) = Cells.ResolveCell(loaded.Workbook, parse.GetValue(cell)!);
            if (parse.GetValue(json))
            {
                Report.Write(Report.Cell(loaded.Workbook, worksheet, address.Row, address.Column, effectiveCulture));
                return 0;
            }

            string text = parse.GetValue(formula)
                ? Cells.FormulaOrValue(loaded.Workbook, worksheet, address.Row, address.Column, effectiveCulture)
                : Cells.Display(loaded.Workbook, worksheet, address.Row, address.Column, effectiveCulture);
            Console.Out.WriteLine(text);
            return 0;
        });

        return command;
    }

    public static Command Set()
    {
        var file = Options.File();
        var cell = new Argument<string>("cell") { Description = "Cell reference, e.g. Sheet1.B7" };
        var value = new Argument<string>("value") { Description = "Value or formula, e.g. \"=SUM(B1:B6)\"" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var locale = Options.Locale();
        var json = Options.Json();

        var command = new Command("set", "Set a cell and save the workbook (atomic)")
        {
            file, cell, value, inPlace, output, locale, json,
        };

        command.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            string target = Options.EditTarget(path, parse.GetValue(inPlace), parse.GetValue(output));
            return EditSession.Run(path, target, Locale.Resolve(parse.GetValue(locale)), parse.GetValue(json), session =>
            {
                var (worksheet, address) = Cells.ResolveCell(session.Workbook, parse.GetValue(cell)!);
                // Reuse the app's input parsing (formula vs value, date coercion + format stamping).
                session.Apply(EditCommands.SetInput(session.Workbook, worksheet.Name, address.Row, address.Column,
                    parse.GetValue(value) ?? string.Empty, session.Culture));
            }, format: null);
        });

        return command;
    }

    public static Command Recalc()
    {
        var file = Options.File();
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var locale = Options.Locale();
        var json = Options.Json();

        var command = new Command("recalc", "Recalculate and refresh the file's cached values")
        {
            file, inPlace, output, locale, json,
        };

        command.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            string target = Options.EditTarget(path, parse.GetValue(inPlace), parse.GetValue(output));
            return EditSession.Run(path, target, Locale.Resolve(parse.GetValue(locale)), parse.GetValue(json), session =>
            {
                // The session recalculates on save; this command exists to do exactly that.
                int formulas = session.Workbook.Worksheets.Sum(s => s.EnumerateCells().Count(c => c.Cell.IsFormula));
                session.Note($"Recalculated {formulas} formula cell(s).");
            }, format: null);
        });

        return command;
    }

    public static Command Sheets()
    {
        var file = Options.File();
        var json = Options.Json();
        var command = new Command("sheets", "List worksheets with their used dimensions") { file, json };

        command.SetAction(parse =>
        {
            var loaded = Workspace.Load(parse.GetValue(file)!, null, null);
            if (parse.GetValue(json))
            {
                Report.Write(loaded.Workbook.Worksheets.Select(Report.Sheet).ToList());
                return 0;
            }

            foreach (var worksheet in loaded.Workbook.Worksheets)
            {
                var used = worksheet.GetUsedRange();
                string dims = used is { } r ? r.ToString() : "empty";
                Console.Out.WriteLine($"{worksheet.Name}\t{dims}\t{worksheet.CellCount} cells");
            }

            return 0;
        });

        return command;
    }

    public static Command Info()
    {
        var file = Options.File();
        var json = Options.Json();
        var command = new Command("info", "Workbook overview: sheets, named ranges, locale, cell counts") { file, json };

        command.SetAction(parse =>
        {
            var loaded = Workspace.Load(parse.GetValue(file)!, null, null);
            var workbook = loaded.Workbook;
            if (parse.GetValue(json))
            {
                Report.Write(new
                {
                    file = parse.GetValue(file),
                    sheets = workbook.Worksheets.Select(Report.Sheet).ToList(),
                    namedRanges = workbook.NamedRanges.Select(Report.NamedRange).ToList(),
                    locale = workbook.Settings.Locale?.Name,
                    calculation = workbook.Settings.CalculationMode.ToString(),
                    populatedCells = workbook.Worksheets.Sum(s => s.CellCount),
                });
                return 0;
            }

            Console.Out.WriteLine($"Sheets: {workbook.SheetCount}");
            foreach (var worksheet in workbook.Worksheets)
            {
                var used = worksheet.GetUsedRange();
                Console.Out.WriteLine($"  {worksheet.Name}  {(used is { } r ? r.ToString() : "empty")}  {worksheet.CellCount} cells");
            }

            Console.Out.WriteLine($"Named ranges: {workbook.NamedRanges.Count}");
            foreach (var named in workbook.NamedRanges)
            {
                string scope = named.IsGlobal ? "workbook" : named.Scope!;
                Console.Out.WriteLine($"  {named.Name} = {named.Target}  ({scope})");
            }

            string locale = workbook.Settings.Locale?.Name ?? "(unspecified — system default)";
            Console.Out.WriteLine($"Locale: {locale}");
            Console.Out.WriteLine($"Calculation: {workbook.Settings.CalculationMode}");
            Console.Out.WriteLine($"Populated cells: {workbook.Worksheets.Sum(s => s.CellCount)}");
            return 0;
        });

        return command;
    }

    public static Command Eval()
    {
        var formula = new Argument<string>("formula") { Description = "A formula to evaluate, e.g. \"=ROUND(355/113, 4)\"" };
        var locale = Options.Locale();
        var json = Options.Json();
        var command = new Command("eval", "Evaluate a formula with no file — pure engine") { formula, locale, json };

        command.SetAction(parse =>
        {
            var culture = Locale.Resolve(parse.GetValue(locale)) ?? CultureInfo.CurrentCulture;
            string text = (parse.GetValue(formula) ?? string.Empty).Trim();
            if (text.StartsWith('='))
            {
                text = text[1..];
            }

            bool asJson = parse.GetValue(json);
            if (!Parser.TryParse(text, out var expr, out var error) || expr is null)
            {
                if (asJson)
                {
                    Report.Write(new { ok = false, formula = "=" + text, error = "parse", message = error?.ToString() ?? "invalid formula" });
                    return 1;
                }

                throw new CliException($"Parse error: {error?.ToString() ?? "invalid formula"}");
            }

            var workbook = new Workbook();
            workbook.AddSheet(Cellestra.Core.Defaults.FirstSheetName);
            workbook.Settings.Locale = culture;
            var context = new WorkbookEvaluationContext(workbook, workbook.ActiveSheet.Name, culture);
            var evaluator = new Evaluator(context, FunctionRegistry.CreateDefault());
            var value = evaluator.Evaluate(expr);
            string display = value.IsError ? value.AsError.Token : Cellestra.Core.Styling.NumberFormatter.Format(value, "General", culture);

            if (asJson)
            {
                object? typed = value.IsNumber ? value.AsNumber : value.IsText ? value.AsText : value.IsBoolean ? value.AsBoolean : null;
                Report.Write(new
                {
                    ok = !value.IsError,
                    formula = "=" + text,
                    value = typed,
                    display,
                    error = value.IsError ? value.AsError.Token : null,
                });
                return value.IsError ? 1 : 0;
            }

            if (value.IsError)
            {
                Console.Error.WriteLine(display);
                return 1;
            }

            Console.Out.WriteLine(display);
            return 0;
        });

        return command;
    }

    public static Command Validate()
    {
        var file = Options.File();
        var json = Options.Json();
        var command = new Command("validate", "Check the workbook opens and is free of formula/#REF!/circular errors") { file, json };

        command.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            bool asJson = parse.GetValue(json);
            LoadedWorkbook loaded;
            try
            {
                loaded = Workspace.Load(path, null, null);
            }
            catch (CliException ex)
            {
                if (asJson)
                {
                    Report.Write(new { ok = false, file = path, opens = false, message = ex.Message, errors = Array.Empty<CellReport>(), circular = Array.Empty<string>() });
                    return 1;
                }

                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            var result = Workspace.Recalculate(loaded.Workbook);
            var errors = Report.Errors(loaded.Workbook, loaded.Workbook.Settings.EffectiveLocale);
            var circular = result.CircularCells.Select(c => Report.Address(c.Sheet, c.Row, c.Column)).ToList();
            bool ok = errors.Count == 0 && !result.HasCircularReference;

            if (asJson)
            {
                Report.Write(new { ok, file = path, opens = true, errors, circular });
                return ok ? 0 : 1;
            }

            foreach (var problem in errors.Take(50))
            {
                Console.Error.WriteLine($"{problem.Cell}: {problem.Error}");
            }

            if (ok)
            {
                Console.Out.WriteLine("OK");
                return 0;
            }

            if (result.HasCircularReference)
            {
                Console.Error.WriteLine($"{circular.Count} circular reference(s).");
            }

            Console.Error.WriteLine($"{errors.Count} cell error(s).");
            return 1;
        });

        return command;
    }
}
