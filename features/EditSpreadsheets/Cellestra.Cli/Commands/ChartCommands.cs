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
        var type = new Option<string>("--type") { Description = "column|bar|line|area|pie|doughnut|scatter|bubble|radar|filled-radar" };
        type.DefaultValueFactory = _ => "column";
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

        var command = new Command("chart", "Add a chart sheet plotting data from the workbook")
        {
            file, type, title, from, categories, values, names, xValues, bubbleSizes, stacking, noLegend, xTitle, yTitle, inPlace, output,
        };

        command.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            string target = Options.EditTarget(path, parse.GetValue(inPlace), parse.GetValue(output));
            var loaded = Workspace.Load(path, WorkbookFormat.Ods, null);
            var workbook = loaded.Workbook;

            var chartType = ParseType(parse.GetValue(type)!);
            var definition = new ChartDefinition
            {
                Type = chartType,
                Title = parse.GetValue(title) ?? "Chart",
                ShowLegend = !parse.GetValue(noLegend),
                XAxisTitle = parse.GetValue(xTitle),
                YAxisTitle = parse.GetValue(yTitle),
                Stacking = ParseStacking(parse.GetValue(stacking), chartType),
            };

            var explicitValues = parse.GetValue(values) ?? Array.Empty<string>();
            if (explicitValues.Length > 0)
            {
                BuildExplicit(definition, chartType, explicitValues, parse.GetValue(names) ?? Array.Empty<string>(),
                    parse.GetValue(categories), parse.GetValue(xValues), parse.GetValue(bubbleSizes));
            }
            else if (parse.GetValue(from) is { Length: > 0 } fromRange)
            {
                BuildFromRange(definition, chartType, workbook, fromRange);
            }
            else
            {
                throw new CliException("Give the data to plot with --from <range> or one or more --values <range>.");
            }

            Validate(definition, chartType);

            var sheet = workbook.AddChartSheet(definition);
            Workspace.Recalculate(workbook);
            Workspace.SaveOds(workbook, loaded.Package, target);
            Console.Error.WriteLine($"Added chart sheet '{sheet.Name}'.");
            return 0;
        });

        return command;
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
