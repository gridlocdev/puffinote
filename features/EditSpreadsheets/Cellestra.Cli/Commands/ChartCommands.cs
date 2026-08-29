using System.CommandLine;
using Cellestra.Charts;
using Cellestra.Cli.Infrastructure;
using Cellestra.Core.Model;
using Puffinote.Components.Core.Cli;

namespace Cellestra.Cli.Commands;

/// <summary>
/// Creates chart sheets from the command line — the headless face of the desktop's Insert ▸ Chart
/// Sheet. A chart can be seeded from a tabular range (<c>--from</c>, like dragging a selection in the
/// GUI) or wired field-by-field (<c>--values</c>, <c>--categories</c>, <c>--x-values</c>,
/// <c>--bubble-sizes</c>, <c>--names</c>). Each chart type only accepts the fields it can plot, and
/// the command validates that up front. The result round-trips as an ODF embedded chart object, same
/// as a GUI-made one (no third-party charting library — Golden Rule #6 / ARCHITECTURE §2.1).
/// </summary>
internal static class ChartCommands
{
    public static Command Chart()
    {
        var file = Options.File();
        var type = new Option<string>("--type")
        {
            Description = "column|bar|line|area|pie|doughnut|scatter|bubble|radar|filled-radar",
            DefaultValueFactory = _ => "column"
        };
        var title = new Option<string?>("--title") { Description = "Chart title" };
        var from = new Option<string?>("--from") { Description = "Seed from a tabular range: 1st column = categories (or X for scatter/bubble), the rest = series; a text header row names them" };
        var categories = new Option<string?>("--categories") { Description = "Category-label range (category & pie families)" };
        var values = new Option<string[]>("--values") { Description = "A series' value range; repeat for multiple series", AllowMultipleArgumentsPerToken = true };
        var names = new Option<string[]>("--names") { Description = "Literal series names, paired with --values in order", AllowMultipleArgumentsPerToken = true };
        var xValues = new Option<string?>("--x-values") { Description = "Shared X-value range (scatter & bubble)" };
        var bubbleSizes = new Option<string?>("--bubble-sizes") { Description = "Shared bubble-size range (bubble only)" };
        var stacking = new Option<string?>("--stacking") { Description = "none|stacked|percent (column/bar/area only)" };
        var noLegend = new Option<bool>("--no-legend") { Description = "Hide the legend" };
        var xTitle = new Option<string?>("--x-title") { Description = "Category/X axis title" };
        var yTitle = new Option<string?>("--y-title") { Description = "Value/Y axis title" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();

        var command = new Command("chart", "Add a chart sheet plotting data from the workbook")
        {
            file, type, title, from, categories, values, names, xValues, bubbleSizes, stacking, noLegend, xTitle, yTitle, inPlace, output, json,
        };

        command.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            string target = Options.EditTarget(path, parse.GetValue(inPlace), parse.GetValue(output));
            return EditSession.Run(path, target, null, parse.GetValue(json), session =>
                AddChart(session, parse.GetValue(type)!, parse.GetValue(title), parse.GetValue(noLegend), parse.GetValue(xTitle),
                    parse.GetValue(yTitle), parse.GetValue(stacking), parse.GetValue(values) ?? [], parse.GetValue(names) ?? [],
                    parse.GetValue(categories), parse.GetValue(xValues), parse.GetValue(bubbleSizes), parse.GetValue(from)));
        });

        return command;
    }

    /// <summary>Build, validate, and add one chart sheet — shared by the <c>chart</c> verb and <c>apply</c>.</summary>
    internal static void AddChart(
        EditSession session, string typeText, string? title, bool noLegend, string? xTitle, string? yTitle, string? stackingText,
        string[] values, string[] names, string? categories, string? xValues, string? bubbleSizes, string? from)
    {
        var definition = BuildDefinition(session.Workbook, typeText, title, noLegend, xTitle, yTitle, stackingText, values, names, categories, xValues, bubbleSizes, from);
        var sheet = session.Workbook.AddChartSheet(definition);
        session.Field("sheet", sheet.Name);
        session.Note($"Added chart sheet '{sheet.Name}'.");
    }

    /// <summary>
    /// Change an existing chart in place. Only the options given change; giving data (<c>--from</c>
    /// or <c>--values</c>…) rebuilds the series, otherwise they stay. The sheet keeps its name.
    /// </summary>
    internal static void UpdateChart(
        EditSession session, string sheetName, string? typeText, string? title, bool? legend, string? xTitle, string? yTitle, string? stackingText,
        string[] values, string[] names, string? categories, string? xValues, string? bubbleSizes, string? from)
    {
        var sheet = ChartSheet(session.Workbook, sheetName);
        var current = sheet.Chart!;
        bool rebuildData = values.Length > 0 || from is { Length: > 0 };
        var chartType = typeText is null ? current.Type : ParseType(typeText);

        ChartDefinition next;
        if (rebuildData)
        {
            next = BuildDefinition(session.Workbook, typeText ?? current.Type.ToString(), title ?? current.Title, legend is false || (legend is null && !current.ShowLegend),
                xTitle ?? current.XAxisTitle, yTitle ?? current.YAxisTitle, stackingText ?? Spell(current.Stacking), values, names, categories, xValues, bubbleSizes, from);
        }
        else
        {
            next = current.Clone();
            next.Type = chartType;
            next.Title = title ?? current.Title;
            next.ShowLegend = legend ?? current.ShowLegend;
            next.XAxisTitle = xTitle ?? current.XAxisTitle;
            next.YAxisTitle = yTitle ?? current.YAxisTitle;
            next.Stacking = ParseStacking(stackingText ?? Spell(current.Stacking), chartType);
            if (categories is not null)
            {
                next.CategoriesReference = RequireRange(categories, "--categories");
            }

            Validate(next, chartType);
        }

        next.ObjectName = current.ObjectName; // the same embedded object, rewritten
        sheet.Chart = next;
        session.Field("sheet", sheet.Name);
        session.Note($"Updated chart sheet '{sheet.Name}'.");
    }

    internal static Worksheet ChartSheet(Workbook workbook, string name)
    {
        var sheet = Cells.PickSheet(workbook, name);
        return sheet.IsChartSheet ? sheet : throw new CliException($"'{sheet.Name}' is not a chart sheet. `cellestra charts list` shows them.");
    }

    private static string Spell(ChartStacking stacking) => stacking.ToString().ToLowerInvariant();

    /// <summary>Everything a chart is, as JSON — what <c>charts show</c> prints and <c>charts list</c> summarizes.</summary>
    internal static object Describe(Worksheet sheet)
    {
        var c = sheet.Chart!;
        return new
        {
            sheet = sheet.Name,
            type = c.Type.ToString().ToLowerInvariant(),
            title = c.Title,
            stacking = c.Stacking == ChartStacking.None ? null : Spell(c.Stacking),
            legend = c.ShowLegend,
            xTitle = c.XAxisTitle,
            yTitle = c.YAxisTitle,
            categories = c.CategoriesReference,
            series = c.Series.Select(s => new
            {
                name = s.Name,
                nameRef = s.NameReference,
                values = s.ValuesReference,
                xValues = s.XValuesReference,
                bubbleSizes = s.BubbleSizeReference,
                type = s.Type?.ToString().ToLowerInvariant(),
            }).ToList(),
        };
    }

    public static Command Charts()
    {
        var group = new Command("charts", "List, show, update, or remove the workbook's chart sheets");
        group.Subcommands.Add(ChartsList());
        group.Subcommands.Add(ChartsShow());
        group.Subcommands.Add(ChartsRemove());
        group.Subcommands.Add(ChartsUpdate());
        return group;
    }

    private static Command ChartsList()
    {
        var file = Options.File();
        var json = Options.Json();
        var command = new Command("list", "List chart sheets: name, type, title, series") { file, json };
        command.SetAction(parse =>
        {
            var loaded = Workspace.Load(parse.GetValue(file)!, WorkbookFormat.Ods, null);
            var charts = loaded.Workbook.Worksheets.Where(s => s.IsChartSheet).ToList();
            if (parse.GetValue(json))
            {
                Report.Write(charts.Select(Describe).ToList());
                return 0;
            }

            foreach (var sheet in charts)
            {
                var c = sheet.Chart!;
                Console.Out.WriteLine($"{sheet.Name}\t{c.Type.ToString().ToLowerInvariant()}\t{c.Title}\t{c.Series.Count} series");
            }

            return 0;
        });
        return command;
    }

    private static Command ChartsShow()
    {
        var file = Options.File();
        var name = new Argument<string>("sheet") { Description = "The chart sheet" };
        var command = new Command("show", "Print a chart's definition as JSON") { file, name };
        command.SetAction(parse =>
        {
            var loaded = Workspace.Load(parse.GetValue(file)!, WorkbookFormat.Ods, null);
            Report.Write(Describe(ChartSheet(loaded.Workbook, parse.GetValue(name)!)));
            return 0;
        });
        return command;
    }

    private static Command ChartsRemove()
    {
        var file = Options.File();
        var name = new Argument<string>("sheet") { Description = "The chart sheet to remove" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("remove", "Remove a chart sheet") { file, name, inPlace, output, json };
        command.SetAction(parse => LayoutCommands.Dispatch(parse, file, inPlace, output, json, "chart-remove", new() { ["sheet"] = parse.GetValue(name) }));
        return command;
    }

    private static Command ChartsUpdate()
    {
        var file = Options.File();
        var name = new Argument<string>("sheet") { Description = "The chart sheet to change" };
        var type = new Option<string?>("--type") { Description = "column|bar|line|area|pie|doughnut|scatter|bubble|radar|filled-radar" };
        var title = new Option<string?>("--title") { Description = "Chart title" };
        var from = new Option<string?>("--from") { Description = "Rebuild the series from a tabular range" };
        var categories = new Option<string?>("--categories") { Description = "Category-label range" };
        var values = new Option<string[]>("--values") { Description = "Rebuild the series: a value range per series", AllowMultipleArgumentsPerToken = true };
        var names = new Option<string[]>("--names") { Description = "Series names, paired with --values", AllowMultipleArgumentsPerToken = true };
        var xValues = new Option<string?>("--x-values") { Description = "Shared X-value range (scatter & bubble)" };
        var bubbleSizes = new Option<string?>("--bubble-sizes") { Description = "Shared bubble-size range (bubble only)" };
        var stacking = new Option<string?>("--stacking") { Description = "none|stacked|percent" };
        var legend = new Option<string?>("--legend") { Description = "true | false" };
        var xTitle = new Option<string?>("--x-title") { Description = "Category/X axis title" };
        var yTitle = new Option<string?>("--y-title") { Description = "Value/Y axis title" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("update", "Change a chart: any option given replaces that part; data options rebuild the series")
        {
            file, name, type, title, from, categories, values, names, xValues, bubbleSizes, stacking, legend, xTitle, yTitle, inPlace, output, json,
        };
        command.SetAction(parse => LayoutCommands.Dispatch(parse, file, inPlace, output, json, "chart-update", new()
        {
            ["sheet"] = parse.GetValue(name),
            ["type"] = parse.GetValue(type),
            ["title"] = parse.GetValue(title),
            ["from"] = parse.GetValue(from),
            ["categories"] = parse.GetValue(categories),
            ["values"] = parse.GetValue(values) is { Length: > 0 } v ? v : null,
            ["names"] = parse.GetValue(names) is { Length: > 0 } n ? n : null,
            ["xValues"] = parse.GetValue(xValues),
            ["bubbleSizes"] = parse.GetValue(bubbleSizes),
            ["stacking"] = parse.GetValue(stacking),
            ["legend"] = parse.GetValue(legend) is { } l ? l.Trim().ToLowerInvariant() is "true" or "on" or "yes" : null,
            ["xTitle"] = parse.GetValue(xTitle),
            ["yTitle"] = parse.GetValue(yTitle),
        }));
        return command;
    }

    private static ChartDefinition BuildDefinition(
        Workbook workbook, string typeText, string? title, bool noLegend, string? xTitle, string? yTitle, string? stackingText,
        string[] values, string[] names, string? categories, string? xValues, string? bubbleSizes, string? from)
    {
        var chartType = ParseType(typeText);
        var definition = new ChartDefinition
        {
            Type = chartType,
            Title = title ?? "Chart",
            ShowLegend = !noLegend,
            XAxisTitle = xTitle,
            YAxisTitle = yTitle,
            Stacking = ParseStacking(stackingText, chartType),
        };

        if (values.Length > 0)
        {
            BuildExplicit(definition, chartType, values, names, categories, xValues, bubbleSizes);
        }
        else if (from is { Length: > 0 } fromRange)
        {
            BuildFromRange(definition, chartType, workbook, fromRange);
        }
        else
        {
            throw new CliException("Give the data to plot with --from <range> or one or more --values <range>.");
        }

        Validate(definition, chartType);
        return definition;
    }

    // ---- Building the definition -----------------------------------------

    private static void BuildExplicit(
        ChartDefinition definition, ChartType type, string[] values, string[] names,
        string? categories, string? xValues, string? bubbleSizes)
    {
        bool xy = ChartTypes.IsXY(type);
        if (xy && string.IsNullOrWhiteSpace(xValues))
        {
            throw new CliException($"A {type.ToString().ToLowerInvariant()} chart needs --x-values.");
        }

        if (type == ChartType.Bubble && string.IsNullOrWhiteSpace(bubbleSizes))
        {
            throw new CliException("A bubble chart needs --bubble-sizes.");
        }

        if (!xy && !string.IsNullOrWhiteSpace(categories))
        {
            definition.CategoriesReference = RequireRange(categories!, "--categories");
        }

        string? xRef = xy ? RequireRange(xValues!, "--x-values") : null;
        string? sizeRef = type == ChartType.Bubble ? RequireRange(bubbleSizes!, "--bubble-sizes") : null;

        for (int i = 0; i < values.Length; i++)
        {
            var series = new ChartSeries
            {
                ValuesReference = RequireRange(values[i], "--values"),
                XValuesReference = xRef,
                BubbleSizeReference = sizeRef,
                Name = i < names.Length ? names[i] : null,
            };
            definition.Series.Add(series);
        }
    }

    private static void BuildFromRange(ChartDefinition definition, ChartType type, Workbook workbook, string fromRange)
    {
        if (type == ChartType.Bubble)
        {
            throw new CliException("A bubble chart needs three ranges per series; build it with --values, --x-values, and --bubble-sizes instead of --from.");
        }

        var (sheet, range) = Cells.ResolveRange(workbook, fromRange);
        if (range.RowCount < 2)
        {
            throw new CliException("--from needs at least two rows (a header or first data point plus values).");
        }

        bool xy = ChartTypes.IsXY(type);
        int top = range.Top, bottom = range.Bottom, left = range.Left, right = range.Right;
        bool headerRow = sheet.GetValue(top, left).IsText;
        int dataTop = headerRow && bottom > top ? top + 1 : top;

        string Ref(int column, int fromRow, int toRow) =>
            $"{sheet.Name}.{SheetDimensions.ColumnToLetters(column)}{fromRow + 1}:{SheetDimensions.ColumnToLetters(column)}{toRow + 1}";

        // The first column is the shared axis: category labels for the category/pie families, or the
        // X values for scatter. Every remaining column becomes a value series; a header row names it.
        if (right > left)
        {
            string firstColumn = Ref(left, dataTop, bottom);
            if (xy)
            {
                // scatter: first column is X (bubble is excluded above)
            }
            else
            {
                definition.CategoriesReference = firstColumn;
            }

            for (int column = left + 1; column <= right; column++)
            {
                var series = new ChartSeries
                {
                    ValuesReference = Ref(column, dataTop, bottom),
                    XValuesReference = xy ? firstColumn : null,
                };
                if (headerRow)
                {
                    series.NameReference = $"{sheet.Name}.{SheetDimensions.ColumnToLetters(column)}{top + 1}";
                }

                definition.Series.Add(series);
            }
        }
        else
        {
            // A single column charts as one series with no category/X axis.
            definition.Series.Add(new ChartSeries { ValuesReference = Ref(left, dataTop, bottom) });
        }
    }

    // ---- Validation -------------------------------------------------------

    private static void Validate(ChartDefinition definition, ChartType type)
    {
        if (definition.Series.Count == 0)
        {
            throw new CliException("The chart has no data series to plot.");
        }

        if (type is ChartType.Pie or ChartType.Doughnut && definition.Series.Count > 1)
        {
            throw new CliException($"A {type.ToString().ToLowerInvariant()} chart plots a single series; pass one --values (or a two-column --from).");
        }
    }

    // ---- Parsing helpers --------------------------------------------------

    private static ChartType ParseType(string text) => text.Trim().ToLowerInvariant() switch
    {
        "column" => ChartType.Column,
        "bar" => ChartType.Bar,
        "line" => ChartType.Line,
        "area" => ChartType.Area,
        "pie" => ChartType.Pie,
        "doughnut" or "donut" => ChartType.Doughnut,
        "scatter" => ChartType.Scatter,
        "bubble" => ChartType.Bubble,
        "radar" => ChartType.Radar,
        "filled-radar" or "filledradar" => ChartType.FilledRadar,
        _ => throw new CliException($"Unknown chart type '{text}'. Use one of: column, bar, line, area, pie, doughnut, scatter, bubble, radar, filled-radar."),
    };

    private static ChartStacking ParseStacking(string? text, ChartType type)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ChartStacking.None;
        }

        var stacking = text.Trim().ToLowerInvariant() switch
        {
            "none" => ChartStacking.None,
            "stacked" => ChartStacking.Stacked,
            "percent" or "percentage" => ChartStacking.Percent,
            _ => throw new CliException($"Unknown stacking '{text}'. Use none, stacked, or percent."),
        };

        if (stacking != ChartStacking.None && !ChartTypes.SupportsStacking(type))
        {
            throw new CliException($"--stacking applies only to column, bar, and area charts, not {type.ToString().ToLowerInvariant()}.");
        }

        return stacking;
    }

    /// <summary>Validate a chart data reference parses as a range and return it verbatim for storage.</summary>
    private static string RequireRange(string reference, string option)
    {
        if (!CellRange.TryParse(reference, out _))
        {
            throw new CliException($"'{reference}' is not a valid range for {option} (e.g. Sheet1.B2:B7).");
        }

        return reference;
    }
}
