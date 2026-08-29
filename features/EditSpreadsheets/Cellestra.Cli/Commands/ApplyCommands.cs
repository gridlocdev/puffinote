using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Cellestra.Cli.Infrastructure;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Puffinote.Components.Core.Cli;

namespace Cellestra.Cli.Commands;

/// <summary>
/// <c>apply</c>: one JSON document of typed operations, one load, one recalculation, one atomic
/// save, one report. This is the verb a script or an agent builds a sheet with — "here is the table,
/// the formulas, the header style, the frozen row, the chart" — rather than a process per cell.
/// Every operation drives the same Core builder the matching verb (and the GUI) does, so the result
/// is identical; the verbs stay for a human at a shell, and <c>apply</c> is their batch form.
/// </summary>
internal static class ApplyCommands
{
    /// <summary>
    /// The document is either <c>{"ops": [...]}</c> or a bare array. Every op has an <c>"op"</c>;
    /// the original <c>{"cell", "value"|"formula"}</c> entries still work as an implicit
    /// <c>set</c>. Field names are case-insensitive.
    /// </summary>
    public const string Reference = """
        apply — a batch of typed operations from JSON
        =============================================

        Document: {"ops": [ op, op, ... ]}   (or a bare array of ops)
        Every op is an object with "op". Addresses: "Sheet1.B2" / "B2" (cell), "Sheet1.A1:C10" / "A1:C10" (range);
        an address without a sheet means the op's "sheet" if given, else the active/first sheet.
        Row/column spans: "3", "3:5"; "B", "B:D". Colors: "#RRGGBB". Sizes are points.

        Cells
          {"op":"set", "cell":"B2", "value": 42}                   number | string | bool | null (null clears)
          {"op":"set", "cell":"B2", "value": "=SUM(B3:B9)"}        a string starting with = is a formula
          {"op":"set", "cell":"B2", "value": "2026-01-31"}         strings parse like typing: dates, numbers, %, bools
          {"op":"set", "at":"A1", "values": [["Item","Qty"],["Pen",3],["Ink","=B2*2"]]}   a 2-D block
          {"op":"clear", "range":"A1:C10", "what":"contents"}      contents (default) | formats | all

        Formatting
          {"op":"style", "range":"A1:D1", "bold":true, "italic":false, "underline":false, "strikethrough":false,
                         "font":"Helvetica Neue", "size":12, "color":"#1A1A1A", "fill":"#EEEEEE",
                         "align":"center", "valign":"middle", "wrap":true,      align: general|left|center|right; valign: top|middle|bottom
                         "format":"#,##0.00", "border":"thin", "borderColor":"#000000"}   border: none|thin|medium|thick|dashed|dotted|double
                         Only the keys you give change; "fill": null removes a fill; "format":"General" resets.
          {"op":"width",  "columns":"B:D", "width": 120, "sheet":"Sheet1"}
          {"op":"height", "rows":"1", "height": 28}
          {"op":"freeze", "rows":1, "columns":0}                      0/0 unfreezes
          {"op":"merge", "range":"A1:D1"}      {"op":"unmerge", "cell":"A1"}
          {"op":"hide", "rows":"5:8"} / {"op":"hide", "columns":"C"}    {"op":"show", ...} likewise

        Structure
          {"op":"add-sheet", "name":"Data", "at":1, "ifMissing":true}   at: 1-based position; ifMissing: no error if it exists
          {"op":"rename-sheet", "name":"Sheet1", "to":"Data"}   {"op":"delete-sheet", "name":"Old"}
          {"op":"duplicate-sheet", "name":"Sheet1", "as":"Copy"}   {"op":"move-sheet", "name":"Data", "to":1}
          {"op":"settings", "locale":"en-US", "calc":"automatic|manual", "activeSheet":"Data"}
          {"op":"insert-rows", "at":3, "count":2}   {"op":"delete-rows", "at":3, "count":1}      (1-based)
          {"op":"insert-cols", "at":"B", "count":1} {"op":"delete-cols", "at":"B"}

        Blocks
          {"op":"copy", "from":"A1:B3", "to":"Sheet2.D1", "mode":"all|values|formats|formulas"}   relative refs shift, like paste
          {"op":"move", "from":"A1:B3", "to":"D1"}                                              formulas travel unchanged; source cleared
          {"op":"fill", "range":"D2:D20", "from":"D2"}       repeat a source block down/across (default: the range's first row or column)
          {"op":"paint", "from":"A1:D1", "to":"A10:D20"}     format painter: copy formatting only, tiling the pattern
          {"op":"filter", "range":"A1:D100", "header":true, "columns": {"B": {"condition":">5"}, "C": {"values":["Red","Blue"]}}}
          {"op":"filter", "range":"A1:D100", "clear":true}   conditions are SUMIF-style (">5", "<>0", "*ink*"); rows are hidden, never deleted

        Data
          {"op":"sort", "range":"A1:D100", "by":"B,A:desc", "header":true}
          {"op":"table", "range":"A1:D20", "name":"Sales", "header":true, "replace":true}   replace: redefine a table already on the range
          {"op":"table-options", "table":"Sales", "bandedRows":true, "headerColumn":true, "range":"A1:D30"}   table: a name or a cell in it
          {"op":"table-remove", "table":"Sales"}
          {"op":"validation", "range":"C2:C50", "list":["Red","Green"]}  or  "source":"Lists.A1:A8"
          {"op":"validation", "range":"B2:B50", "kind":"whole|decimal|date|text-length", "operator":"between|not-between|equal|not-equal|greater|less|greater-or-equal|less-or-equal",
                              "value1":1, "value2":100, "response":"stop|warning|information", "inputMessage":"…", "errorMessage":"…"}   a date bound may be "2026-01-31"
          {"op":"validation", "range":"B2:B50", "remove":true}
          {"op":"name", "name":"TaxRate", "range":"Config.B1"}   {"op":"unname", "name":"TaxRate"}
          {"op":"hyperlink", "cell":"A2", "url":"https://…", "tooltip":"…"}   "url": null removes it
          {"op":"chart", "type":"column", "title":"Sales", "from":"A1:C13",          or "values":["B2:B13"], "names":["Q1"], "categories":"A2:A13",
                         "stacking":"none", "legend":true, "xTitle":"Month", "yTitle":"USD"}   scatter: "xValues"; bubble: + "bubbleSizes"
          {"op":"chart-update", "sheet":"Chart1", "title":"…", "type":"line", …}   only the keys given change; data keys rebuild the series
          {"op":"chart-remove", "sheet":"Chart1"}

        Run:  cellestra apply book.ods ops.json --in-place [--json] [--dry-run] [--locale en-US]
              cat ops.json | cellestra apply book.ods - -o out.ods --json
        Ops apply in order; the first failing op aborts the whole batch and nothing is written.
        """;

    public static Command Apply()
    {
        var file = Options.File();
        var edits = Options.File("ops", "JSON file of operations (or - for stdin) — see `cellestra schema apply`");
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var dryRun = new Option<bool>("--dry-run") { Description = "Apply and report, but write nothing" };
        var locale = Options.Locale();
        var json = Options.Json();
        var command = new Command("apply", "Apply a batch of typed operations from JSON: cells, styles, layout, structure, data, charts")
        {
            file, edits, inPlace, output, dryRun, locale, json,
        };

        command.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            bool preview = parse.GetValue(dryRun);
            string target = preview ? path : Options.EditTarget(path, parse.GetValue(inPlace), parse.GetValue(output));
            var ops = ParseDocument(parse.GetValue(edits)!);

            return EditSession.Run(path, target, Locale.Resolve(parse.GetValue(locale)), parse.GetValue(json), session =>
            {
                for (int i = 0; i < ops.Count; i++)
                {
                    try
                    {
                        Run(session, new Op(ops[i].Element, i), ops[i].Kind);
                    }
                    catch (Exception ex) when (ex is CliException or InvalidOperationException or ArgumentException or FormatException)
                    {
                        // Core's own refusals (duplicate sheet, bad name, …) are user errors too, and the
                        // user needs to know which op raised them.
                        throw new CliException($"op {i + 1} ({ops[i].Kind}): {ex.Message}");
                    }
                }

                session.Field("applied", ops.Count);
                session.Note($"Applied {ops.Count} op(s).");
            }, dryRun: preview);
        });

        return command;
    }

    // ---- Parsing the document --------------------------------------------

    private static List<(JsonElement Element, string Kind)> ParseDocument(string path)
    {
        string text = path == "-" ? Console.In.ReadToEnd() : File.ReadAllText(path);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new CliException($"Invalid ops JSON: {ex.Message}");
        }

        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && TryProperty(root, "ops", out var list))
        {
            root = list;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new CliException("The ops document must be an array of operations, or {\"ops\": [...]}.");
        }

        var ops = new List<(JsonElement, string)>();
        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new CliException($"Op {ops.Count + 1} is not an object.");
            }

            string kind = TryProperty(element, "op", out var op) && op.ValueKind == JsonValueKind.String
                ? op.GetString()!.Trim().ToLowerInvariant()
                : TryProperty(element, "cell", out _) ? "set" // the original {cell, value|formula} form
                : throw new CliException($"Op {ops.Count + 1} has no \"op\".");
            ops.Add((element, kind));
        }

        return ops;
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Run one op given as a plain dictionary — how the standalone verbs (<c>style</c>, <c>width</c>,
    /// <c>freeze</c>, …) share the ops' single implementation: build the op, dispatch it.
    /// </summary>
    internal static void RunOp(EditSession session, string kind, IReadOnlyDictionary<string, object?> fields)
    {
        var element = JsonSerializer.SerializeToElement(fields);
        Run(session, new Op(element, 0), kind);
    }

    /// <summary>One op's fields, read leniently (case-insensitive names, numbers-as-strings) with clear errors.</summary>
    private sealed class Op
    {
        private readonly JsonElement _element;

        public Op(JsonElement element, int index)
        {
            _element = element;
            Index = index;
        }

        public int Index { get; }

        public bool Has(string name) => TryProperty(_element, name, out var v) && v.ValueKind != JsonValueKind.Null;

        public JsonElement? Raw(string name) => TryProperty(_element, name, out var v) ? v : null;

        /// <summary>True when the key is present, even with a null value (so "fill": null can mean "remove").</summary>
        public bool Present(string name) => TryProperty(_element, name, out _);

        public string? String(string name)
        {
            if (!TryProperty(_element, name, out var v) || v.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => throw new CliException($"\"{name}\" must be a string."),
            };
        }

        public string Require(string name) =>
            String(name) ?? throw new CliException($"needs \"{name}\".");

        public bool? Bool(string name)
        {
            if (!TryProperty(_element, name, out var v) || v.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(v.GetString(), out bool b) => b,
                _ => throw new CliException($"\"{name}\" must be true or false."),
            };
        }

        public double? Number(string name)
        {
            if (!TryProperty(_element, name, out var v) || v.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (v.ValueKind == JsonValueKind.Number)
            {
                return v.GetDouble();
            }

            if (v.ValueKind == JsonValueKind.String &&
                double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                return d;
            }

            throw new CliException($"\"{name}\" must be a number.");
        }

        public int Int(string name, int fallback)
        {
            double? n = Number(name);
            if (n is null)
            {
                return fallback;
            }

            if (n.Value != Math.Floor(n.Value) || n.Value < int.MinValue || n.Value > int.MaxValue)
            {
                throw new CliException($"\"{name}\" must be a whole number.");
            }

            return (int)n.Value;
        }

        public string[] Strings(string name)
        {
            if (!TryProperty(_element, name, out var v) || v.ValueKind == JsonValueKind.Null)
            {
                return [];
            }

            if (v.ValueKind == JsonValueKind.String)
            {
                // A lone string is the verb's comma-separated spelling ("Red,Green,Blue").
                return v.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            if (v.ValueKind != JsonValueKind.Array)
            {
                throw new CliException($"\"{name}\" must be an array of strings.");
            }

            return [.. v.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText())];
        }
    }

    // ---- Dispatch ---------------------------------------------------------

    private static void Run(EditSession session, Op op, string kind)
    {
        var wb = session.Workbook;
        switch (kind)
        {
            case "set":
                Set(session, op);
                break;
            case "clear":
                Clear(session, op);
                break;
            case "style":
                Style(session, op);
                break;
            case "width":
                Width(session, op);
                break;
            case "height":
                Height(session, op);
                break;
            case "freeze":
                Freeze(session, op);
                break;
            case "merge":
                {
                    var (sheet, range) = Range(session, op, "range");
                    session.Apply(EditCommands.MergeCells(sheet.Name, range));
                    break;
                }

            case "unmerge":
                {
                    var (sheet, address) = Cell(session, op, "cell");
                    session.Apply(EditCommands.UnmergeCells(sheet.Name, address.Row, address.Column));
                    break;
                }

            case "hide":
                Visibility(session, op, hidden: true);
                break;
            case "show":
                Visibility(session, op, hidden: false);
                break;
            case "add-sheet":
                AddSheet(session, op);
                break;
            case "rename-sheet":
                wb.RenameSheet(Cells.PickSheet(wb, op.Require("name")), op.Require("to"));
                break;
            case "duplicate-sheet":
                {
                    var copy = wb.DuplicateSheet(Cells.PickSheet(wb, op.Require("name")));
                    if (op.String("as") is { } newName)
                    {
                        wb.RenameSheet(copy, newName);
                    }

                    session.Field("sheet", copy.Name);
                    break;
                }

            case "move-sheet":
                {
                    var sheet = Cells.PickSheet(wb, op.Require("name"));
                    int to = op.Int("to", 0);
                    if (to < 1 || to > wb.SheetCount)
                    {
                        throw new CliException($"\"to\" must be a 1-based position from 1 to {wb.SheetCount}.");
                    }

                    wb.MoveSheet(wb.IndexOf(sheet), to - 1);
                    break;
                }

            case "settings":
                Settings(session, op);
                break;
            case "copy":
            case "move":
                {
                    var (fromSheet, from) = Range(session, op, "from");
                    var (toSheet, to) = Range(session, op, "to");
                    RangeCommands.CopyOrMove(session, fromSheet, from, toSheet, to, cut: kind == "move", op.String("mode") ?? "all");
                    break;
                }

            case "fill":
                {
                    var (sheet, range) = Range(session, op, "range");
                    CellRange? source = null;
                    if (op.String("from") is { } fromText)
                    {
                        var (fromSheet, from) = Cells.ResolveRange(wb, fromText, sheet.Name);
                        if (fromSheet != sheet)
                        {
                            throw new CliException("a fill's source must be on the same sheet as the range.");
                        }

                        source = from;
                    }

                    RangeCommands.Fill(session, sheet, range, source);
                    break;
                }

            case "paint":
                {
                    var (fromSheet, from) = Range(session, op, "from");
                    var (toSheet, to) = Range(session, op, "to");
                    RangeCommands.Paint(session, fromSheet, from, toSheet, to);
                    break;
                }

            case "filter":
                {
                    var (sheet, range) = Range(session, op, "range");
                    var columns = new Dictionary<int, ColumnFilter>();
                    if (op.Raw("columns") is { ValueKind: JsonValueKind.Object } spec)
                    {
                        foreach (var property in spec.EnumerateObject())
                        {
                            int column = DataCommands.ParseColumn(property.Name);
                            var criteria = new Op(property.Value, op.Index);
                            var allowed = criteria.Strings("values");
                            columns[column] = new ColumnFilter
                            {
                                AllowedValues = allowed.Length > 0 ? new HashSet<string>(allowed, StringComparer.Ordinal) : null,
                                Condition = criteria.String("condition"),
                            };
                        }
                    }

                    RangeCommands.Filter(session, sheet, range, op.Bool("header") ?? true, op.Bool("clear") ?? false, columns);
                    break;
                }
            case "delete-sheet":
                session.Apply(new DeleteSheetCommand(Cells.PickSheet(wb, op.Require("name")).Name));
                break;
            case "insert-rows":
                Structural(session, op, StructuralAxis.Row, StructuralOperation.Insert);
                break;
            case "delete-rows":
                Structural(session, op, StructuralAxis.Row, StructuralOperation.Delete);
                break;
            case "insert-cols":
                Structural(session, op, StructuralAxis.Column, StructuralOperation.Insert);
                break;
            case "delete-cols":
                Structural(session, op, StructuralAxis.Column, StructuralOperation.Delete);
                break;
            case "sort":
                Sort(session, op);
                break;
            case "table":
                Table(session, op);
                break;
            case "validation":
                Validation(session, op);
                break;
            case "table-remove":
                {
                    var (sheet, table) = FindTable(session, op);
                    session.Apply(DataEditCommands.RemoveTable(sheet.Name, table));
                    session.Field("table", table.Name);
                    break;
                }

            case "table-options":
                TableOptions(session, op);
                break;
            case "chart-remove":
                {
                    var sheet = ChartCommands.ChartSheet(wb, op.Require("sheet"));
                    session.Apply(new DeleteSheetCommand(sheet.Name));
                    session.Field("sheet", sheet.Name);
                    break;
                }

            case "chart-update":
                ChartCommands.UpdateChart(session, op.Require("sheet"), op.String("type"), op.String("title"), op.Bool("legend"),
                    op.String("xTitle"), op.String("yTitle"), op.String("stacking"), op.Strings("values"), op.Strings("names"),
                    op.String("categories"), op.String("xValues"), op.String("bubbleSizes"), op.String("from"));
                break;
            case "name":
                Name(session, op);
                break;
            case "unname":
                if (!wb.RemoveNamedRange(op.Require("name")))
                {
                    throw new CliException($"no named range called '{op.String("name")}'.");
                }

                break;
            case "hyperlink":
                Hyperlink(session, op);
                break;
            case "chart":
                ChartCommands.AddChart(session, op.String("type") ?? "column", op.String("title"), !(op.Bool("legend") ?? true),
                    op.String("xTitle"), op.String("yTitle"), op.String("stacking"), op.Strings("values"), op.Strings("names"),
                    op.String("categories"), op.String("xValues"), op.String("bubbleSizes"), op.String("from"));
                break;
            default:
                throw new CliException($"unknown op '{kind}' — `cellestra schema apply` lists them.");
        }
    }

    // ---- Cells ------------------------------------------------------------

    private static void Set(EditSession session, Op op)
    {
        var wb = session.Workbook;
        if (op.Has("values"))
        {
            string anchor = op.String("at") ?? op.String("range") ?? op.String("cell") ?? throw new CliException("a block needs \"at\" (its top-left cell).");
            var (sheet, range) = Cells.ResolveRange(wb, anchor, op.String("sheet"));
            var rows = op.Raw("values")!.Value;
            if (rows.ValueKind != JsonValueKind.Array)
            {
                throw new CliException("\"values\" must be an array of rows (arrays).");
            }

            int r = 0;
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array)
                {
                    throw new CliException($"\"values\" row {r + 1} is not an array.");
                }

                int c = 0;
                foreach (var value in row.EnumerateArray())
                {
                    SetOne(session, sheet, range.Top + r, range.Left + c, value);
                    c++;
                }

                r++;
            }

            return;
        }

        var (ws, address) = Cells.ResolveCell(wb, op.Require("cell"), op.String("sheet"));
        if (op.Has("formula"))
        {
            // The original {"cell", "formula"} form: the leading = is optional there.
            string formula = op.Require("formula");
            session.Apply(EditCommands.SetInput(wb, ws.Name, address.Row, address.Column,
                formula.StartsWith('=') ? formula : "=" + formula, session.Culture));
            return;
        }

        SetOne(session, ws, address.Row, address.Column, op.Raw("value") ?? default);
    }

    private static void SetOne(EditSession session, Worksheet sheet, int row, int column, JsonElement value)
    {
        var wb = session.Workbook;
        switch (value.ValueKind)
        {
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
                session.Apply(EditCommands.ClearContents(sheet.Name, new CellRange(new CellAddress(column, row))));
                session.Touched(sheet.Name, row, column);
                break;
            case JsonValueKind.Number:
                session.Apply(EditCommands.SetValue(sheet.Name, row, column, CellValue.Number(value.GetDouble())));
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                session.Apply(EditCommands.SetValue(sheet.Name, row, column, CellValue.Boolean(value.GetBoolean())));
                break;
            case JsonValueKind.String:
                // The same parse as typing into the GUI: formulas, numbers, dates (with a date format stamped), text.
                session.Apply(EditCommands.SetInput(wb, sheet.Name, row, column, value.GetString()!, session.Culture));
                break;
            default:
                throw new CliException($"a cell value must be a number, string, boolean, or null (at {Report.Address(sheet.Name, row, column)}).");
        }
    }

    private static void Clear(EditSession session, Op op)
    {
        var (sheet, range) = Range(session, op, "range");
        string what = (op.String("what") ?? "contents").ToLowerInvariant();
        session.Apply(what switch
        {
            "contents" or "values" => EditCommands.ClearContents(sheet.Name, range),
            "formats" or "formatting" or "styles" => EditCommands.ClearFormatting(session.Workbook, sheet.Name, range),
            "all" => EditCommands.ClearAll(sheet.Name, range),
            _ => throw new CliException("\"what\" must be contents, formats, or all."),
        });
    }

    // ---- Formatting -------------------------------------------------------

    private static void Style(EditSession session, Op op)
    {
        var (sheet, range) = Range(session, op, "range");
        var changes = new List<Func<CellStyle, CellStyle>>();

        if (op.Bool("bold") is { } bold)
        {
            changes.Add(s => s.WithFont(s.Font.WithBold(bold)));
        }

        if (op.Bool("italic") is { } italic)
        {
            changes.Add(s => s.WithFont(s.Font.WithItalic(italic)));
        }

        if (op.Bool("underline") is { } underline)
        {
            changes.Add(s => s.WithFont(s.Font.WithUnderline(underline)));
        }

        if (op.Bool("strikethrough") is { } strike)
        {
            changes.Add(s => s.WithFont(s.Font.WithStrikethrough(strike)));
        }

        if (op.String("font") is { } family)
        {
            changes.Add(s => s.WithFont(s.Font with { Family = family }));
        }

        if (op.Number("size") is { } size)
        {
            if (size <= 0)
            {
                throw new CliException("\"size\" must be a positive number of points.");
            }

            changes.Add(s => s.WithFont(s.Font with { SizePoints = size }));
        }

        if (op.String("color") is { } color)
        {
            var argb = Color(color, "color");
            changes.Add(s => s.WithFont(s.Font with { Color = argb }));
        }

        if (op.Present("fill"))
        {
            string? fill = op.String("fill");
            var fillValue = fill is null || fill.Equals("none", StringComparison.OrdinalIgnoreCase) ? Fill.None : new Fill(Color(fill, "fill"));
            changes.Add(s => s.WithFill(fillValue));
        }

        if (op.String("align") is { } align)
        {
            var h = align.Trim().ToLowerInvariant() switch
            {
                "general" => HorizontalAlignment.General,
                "left" => HorizontalAlignment.Left,
                "center" or "centre" => HorizontalAlignment.Center,
                "right" => HorizontalAlignment.Right,
                _ => throw new CliException("\"align\" must be general, left, center, or right."),
            };
            changes.Add(s => s.WithAlignment(s.Alignment with { Horizontal = h }));
        }

        if (op.String("valign") is { } valign)
        {
            var v = valign.Trim().ToLowerInvariant() switch
            {
                "top" => VerticalAlignment.Top,
                "middle" or "center" => VerticalAlignment.Middle,
                "bottom" => VerticalAlignment.Bottom,
                _ => throw new CliException("\"valign\" must be top, middle, or bottom."),
            };
            changes.Add(s => s.WithAlignment(s.Alignment with { Vertical = v }));
        }

        if (op.Bool("wrap") is { } wrap)
        {
            changes.Add(s => s.WithAlignment(s.Alignment with { WrapText = wrap }));
        }

        if (op.String("format") is { } format)
        {
            var nf = new NumberFormat(format);
            changes.Add(s => s.WithNumberFormat(nf));
        }

        if (op.Present("border"))
        {
            string style = op.String("border") ?? "none";
            var line = style.Trim().ToLowerInvariant() switch
            {
                "none" => BorderLineStyle.None,
                "thin" => BorderLineStyle.Thin,
                "medium" => BorderLineStyle.Medium,
                "thick" => BorderLineStyle.Thick,
                "dashed" => BorderLineStyle.Dashed,
                "dotted" => BorderLineStyle.Dotted,
                "double" => BorderLineStyle.Double,
                _ => throw new CliException("\"border\" must be none, thin, medium, thick, dashed, dotted, or double."),
            };
            var edge = new BorderEdge(line, op.String("borderColor") is { } bc ? Color(bc, "borderColor") : ArgbColor.Black);
            var border = line == BorderLineStyle.None ? Border.None : new Border(edge, edge, edge, edge);
            changes.Add(s => s.WithBorder(border));
        }

        if (changes.Count == 0)
        {
            throw new CliException("a style op needs at least one of: bold, italic, underline, strikethrough, font, size, color, fill, align, valign, wrap, format, border.");
        }

        session.Apply(EditCommands.ApplyStyle(session.Workbook, sheet.Name, range, style =>
        {
            foreach (var change in changes)
            {
                style = change(style);
            }

            return style;
        }));

        // ApplyStyle reports no cells (a style moves no values), but the report should still say what
        // was touched: every cell that now exists inside the range. Walk whichever is smaller — the
        // range, or the populated cells — so neither a header row on a huge sheet nor a styled
        // whole column costs more than it's worth.
        if ((long)range.RowCount * range.ColumnCount <= 4096)
        {
            foreach (var (row, column) in range.Positions())
            {
                if (sheet.Find(row, column) is not null)
                {
                    session.Touched(sheet.Name, row, column);
                }
            }
        }
        else
        {
            foreach (var (row, column, _) in sheet.EnumerateCells())
            {
                if (range.Contains(row, column))
                {
                    session.Touched(sheet.Name, row, column);
                }
            }
        }
    }

    private static ArgbColor Color(string text, string field) =>
        ArgbColor.TryFromHex(text, out var color) ? color : throw new CliException($"\"{field}\" must be a #RRGGBB color, not '{text}'.");

    private static void Width(EditSession session, Op op)
    {
        var sheet = Cells.PickSheet(session.Workbook, op.String("sheet"));
        var (first, last) = DataCommands.ParseColumnSpan(op.String("columns") ?? op.String("column") ?? throw new CliException("needs \"columns\" (e.g. \"B\" or \"B:D\")."));
        double width = op.Number("width") ?? throw new CliException("needs \"width\" (points).");
        if (width <= 0)
        {
            throw new CliException("\"width\" must be positive.");
        }

        for (int column = first; column <= last; column++)
        {
            session.Apply(EditCommands.SetColumnWidth(sheet.Name, column, width));
        }
    }

    private static void Height(EditSession session, Op op)
    {
        var sheet = Cells.PickSheet(session.Workbook, op.String("sheet"));
        var (first, last) = DataCommands.ParseRowSpan(op.String("rows") ?? op.String("row") ?? throw new CliException("needs \"rows\" (e.g. \"1\" or \"2:5\")."));
        double height = op.Number("height") ?? throw new CliException("needs \"height\" (points).");
        if (height <= 0)
        {
            throw new CliException("\"height\" must be positive.");
        }

        for (int row = first; row <= last; row++)
        {
            session.Apply(EditCommands.SetRowHeight(sheet.Name, row, height));
        }
    }

    private static void Freeze(EditSession session, Op op)
    {
        var sheet = Cells.PickSheet(session.Workbook, op.String("sheet"));
        int rows = op.Int("rows", 0);
        int columns = op.Int("columns", 0);
        if (rows < 0 || columns < 0)
        {
            throw new CliException("\"rows\" and \"columns\" must be 0 or more.");
        }

        session.Apply(EditCommands.SetFrozenPanes(sheet.Name, rows, columns));
    }

    private static void Visibility(EditSession session, Op op, bool hidden)
    {
        var sheet = Cells.PickSheet(session.Workbook, op.String("sheet"));
        if (op.String("rows") is { } rows)
        {
            var (first, last) = DataCommands.ParseRowSpan(rows);
            session.Apply(EditCommands.SetRowsHidden(sheet.Name, first, last, hidden));
        }
        else if (op.String("columns") is { } columns)
        {
            var (first, last) = DataCommands.ParseColumnSpan(columns);
            session.Apply(EditCommands.SetColumnsHidden(sheet.Name, first, last, hidden));
        }
        else
        {
            throw new CliException("needs \"rows\" or \"columns\".");
        }
    }

    // ---- Structure --------------------------------------------------------

    private static void AddSheet(EditSession session, Op op)
    {
        var wb = session.Workbook;
        string name = op.Require("name");
        if (wb.FindSheet(name) is { } existing)
        {
            if (op.Bool("ifMissing") == true)
            {
                session.Field("sheet", existing.Name); // already there: nothing to do, and not an error
                return;
            }

            throw new CliException($"a sheet named '{name}' already exists (add \"ifMissing\": true to allow that).");
        }

        var sheet = wb.AddSheet(name);
        if (op.Has("at"))
        {
            int at = op.Int("at", 0);
            if (at < 1 || at > wb.SheetCount)
            {
                throw new CliException($"\"at\" must be a 1-based position from 1 to {wb.SheetCount}.");
            }

            wb.MoveSheet(wb.IndexOf(sheet), at - 1);
        }

        session.Field("sheet", sheet.Name);
    }

    private static void Settings(EditSession session, Op op)
    {
        var wb = session.Workbook;
        bool any = false;
        if (op.String("locale") is { } locale)
        {
            wb.Settings.Locale = Locale.Resolve(locale);
            any = true;
        }

        if (op.String("calc") is { } calc)
        {
            wb.Settings.CalculationMode = calc.Trim().ToLowerInvariant() switch
            {
                "auto" or "automatic" => CalculationMode.Automatic,
                "manual" => CalculationMode.Manual,
                _ => throw new CliException("\"calc\" must be automatic or manual."),
            };
            any = true;
        }

        if (op.String("activeSheet") is { } active)
        {
            wb.Settings.ActiveSheetIndex = wb.IndexOf(Cells.PickSheet(wb, active));
            any = true;
        }

        if (!any)
        {
            throw new CliException("a settings op needs at least one of: locale, calc, activeSheet.");
        }
    }

    private static void Structural(EditSession session, Op op, StructuralAxis axis, StructuralOperation operation)
    {
        var sheet = Cells.PickSheet(session.Workbook, op.String("sheet"));
        string at = op.Require("at");
        int index = axis == StructuralAxis.Row ? DataCommands.ParseRow(at) : DataCommands.ParseColumn(at);
        int count = op.Int("count", 1);
        if (count < 1)
        {
            throw new CliException("\"count\" must be at least 1.");
        }

        session.Apply(new StructuralEditCommand(sheet.Name, axis, operation, index, count));
    }

    // ---- Data -------------------------------------------------------------

    private static void Sort(EditSession session, Op op)
    {
        var (sheet, range) = Range(session, op, "range");
        var levels = DataCommands.ParseSortLevels(op.Require("by"));
        session.Apply(DataEditCommands.Sort(sheet.Name, range, new SortSpec(levels, HasHeader: op.Bool("header") ?? true), session.Culture));
    }

    private static void Table(EditSession session, Op op)
    {
        var (sheet, range) = Range(session, op, "range");
        var overlapping = sheet.Tables.Where(t => t.Range.Intersects(range)).ToList();
        if (overlapping.Count > 0)
        {
            if (op.Bool("replace") != true)
            {
                throw new CliException($"the range overlaps table '{overlapping[0].Name}'; a cell can belong to only one table (add \"replace\": true to redefine it).");
            }

            foreach (var old in overlapping)
            {
                session.Apply(DataEditCommands.RemoveTable(sheet.Name, old));
            }
        }

        var table = new Table(op.String("name") ?? DataCommands.NextTableName(sheet), range, HasHeader: op.Bool("header") ?? true);
        session.Apply(DataEditCommands.CreateTable(session.Workbook, sheet.Name, table));
        session.Field("table", table.Name);
    }

    private static void Validation(EditSession session, Op op)
    {
        var (sheet, range) = Range(session, op, "range");
        var existing = sheet.Validations.Where(v => v.Target.Intersects(range)).ToList();

        if (op.Bool("remove") == true)
        {
            if (existing.Count == 0)
            {
                throw new CliException($"no validation rule covers {range}.");
            }

            session.Apply(RemoveValidations(sheet.Name, existing));
            session.Field("removed", existing.Count);
            return;
        }

        var rule = BuildValidation(session, op, range);

        // One rule per range: setting a new rule on cells that already have one replaces it, as the app does.
        if (existing.Count > 0)
        {
            session.Apply(RemoveValidations(sheet.Name, existing));
        }

        session.Apply(DataEditCommands.SetValidation(sheet.Name, rule));
    }

    private static DataValidation BuildValidation(EditSession session, Op op, CellRange range)
    {
        string? kindText = op.String("kind");
        var kind = kindText?.Trim().ToLowerInvariant() switch
        {
            null when op.Has("list") || op.Has("source") => ValidationKind.List,
            null => throw new CliException("needs \"list\" or \"source\" (a list rule), or \"kind\" whole|decimal|date|text-length."),
            "list" => ValidationKind.List,
            "whole" or "whole-number" or "wholenumber" or "integer" => ValidationKind.WholeNumber,
            "decimal" or "number" => ValidationKind.Decimal,
            "date" => ValidationKind.Date,
            "text-length" or "textlength" or "length" => ValidationKind.TextLength,
            "any" => ValidationKind.Any,
            _ => throw new CliException("\"kind\" must be list, whole, decimal, date, or text-length."),
        };

        var rule = new DataValidation(kind, range)
        {
            InputMessage = op.String("inputMessage"),
            ErrorMessage = op.String("errorMessage"),
            Response = (op.String("response") ?? "stop").Trim().ToLowerInvariant() switch
            {
                "stop" => ValidationResponse.Stop,
                "warning" or "warn" => ValidationResponse.Warning,
                "information" or "info" => ValidationResponse.Information,
                _ => throw new CliException("\"response\" must be stop, warning, or information."),
            },
        };

        if (kind == ValidationKind.List)
        {
            if (op.String("source") is { } source)
            {
                if (!CellRange.TryParse(source, out var listSource))
                {
                    throw new CliException($"'{source}' is not a valid range for \"source\".");
                }

                return rule with { ListSource = listSource };
            }

            var list = op.Strings("list");
            if (list.Length == 0)
            {
                throw new CliException("a list rule needs \"list\" (an array of allowed values) or \"source\" (a range).");
            }

            return rule with { ListValues = list };
        }

        if (kind == ValidationKind.Any)
        {
            return rule;
        }

        var comparison = (op.String("operator") ?? "between").Trim().ToLowerInvariant() switch
        {
            "between" => ValidationOperator.Between,
            "not-between" or "notbetween" => ValidationOperator.NotBetween,
            "equal" or "=" or "==" => ValidationOperator.Equal,
            "not-equal" or "notequal" or "<>" or "!=" => ValidationOperator.NotEqual,
            "greater" or "greater-than" or ">" => ValidationOperator.GreaterThan,
            "less" or "less-than" or "<" => ValidationOperator.LessThan,
            "greater-or-equal" or ">=" => ValidationOperator.GreaterOrEqual,
            "less-or-equal" or "<=" => ValidationOperator.LessOrEqual,
            _ => throw new CliException("\"operator\" must be between, not-between, equal, not-equal, greater, less, greater-or-equal, or less-or-equal."),
        };
        bool twoBounds = comparison is ValidationOperator.Between or ValidationOperator.NotBetween;
        double value1 = Bound(session, op, "value1", kind) ?? throw new CliException($"a {kindText} rule needs \"value1\"{(twoBounds ? " and \"value2\"" : "")}.");
        double value2 = twoBounds
            ? Bound(session, op, "value2", kind) ?? throw new CliException($"a between rule needs \"value2\" (the upper bound).")
            : 0;
        return rule with { Operator = comparison, Operand1 = value1, Operand2 = value2 };
    }

    /// <summary>A rule bound: a number, or for a date rule any date the document's locale would accept (ISO always works).</summary>
    private static double? Bound(EditSession session, Op op, string name, ValidationKind kind)
    {
        if (!op.Has(name))
        {
            return null;
        }

        if (kind == ValidationKind.Date && op.Raw(name) is { ValueKind: JsonValueKind.String } text)
        {
            var parsed = InputParser.Parse(text.GetString()!, session.Culture);
            if (!parsed.IsFormula && parsed.Value.IsNumber)
            {
                return parsed.Value.AsNumber;
            }

            throw new CliException($"\"{name}\" is not a date: '{text.GetString()}'.");
        }

        return op.Number(name);
    }

    private static IEditCommand RemoveValidations(string sheet, IReadOnlyList<DataValidation> rules) =>
        new ActionCommand("Remove Data Validation",
            wb =>
            {
                var ws = wb.FindSheet(sheet) ?? throw new InvalidOperationException($"Sheet '{sheet}' not found.");
                foreach (var rule in rules)
                {
                    ws.RemoveValidation(rule);
                }

                return EditResult.None;
            },
            wb =>
            {
                var ws = wb.FindSheet(sheet) ?? throw new InvalidOperationException($"Sheet '{sheet}' not found.");
                foreach (var rule in rules)
                {
                    ws.AddValidation(rule);
                }

                return EditResult.None;
            });

    private static (Worksheet Sheet, Table Table) FindTable(EditSession session, Op op)
    {
        string reference = op.Require("table");
        var (sheet, range) = Range(session, op, "table"); // a table name resolves to its range; so does A1-style
        var table = sheet.Tables.FirstOrDefault(t => string.Equals(t.Name, reference, StringComparison.OrdinalIgnoreCase))
            ?? sheet.Tables.FirstOrDefault(t => t.Range.Intersects(range))
            ?? throw new CliException($"no table at or named '{reference}'.");
        return (sheet, table);
    }

    private static void TableOptions(EditSession session, Op op)
    {
        var (sheet, table) = FindTable(session, op);
        bool any = false;
        if (op.String("range") is { } newRange)
        {
            var (targetSheet, range) = Cells.ResolveRange(session.Workbook, newRange, sheet.Name);
            if (targetSheet != sheet)
            {
                throw new CliException("a table can't move to another sheet; remove it and create it there.");
            }

            if (sheet.Tables.Any(t => t != table && t.Range.Intersects(range)))
            {
                throw new CliException("the new range overlaps another table.");
            }

            var resized = table with { Range = range };
            session.Apply(DataEditCommands.ReplaceTable(sheet.Name, table, resized));
            table = resized;
            any = true;
        }

        if (op.Bool("bandedRows") is { } banded)
        {
            session.Apply(DataEditCommands.SetTableBandedRows(sheet.Name, table, banded));
            table = table with { HasBandedRows = banded };
            any = true;
        }

        if (op.Bool("headerColumn") is { } headerColumn)
        {
            session.Apply(DataEditCommands.SetTableHeaderColumn(session.Workbook, sheet.Name, table, headerColumn));
            any = true;
        }

        if (!any)
        {
            throw new CliException("a table-options op needs at least one of: range, bandedRows, headerColumn.");
        }

        session.Field("table", table.Name);
    }

    private static void Name(EditSession session, Op op)
    {
        var (sheet, range) = Range(session, op, "range");
        session.Workbook.AddNamedRange(new NamedRange(op.Require("name"), range.WithSheet(range.Sheet ?? sheet.Name)));
    }

    private static void Hyperlink(EditSession session, Op op)
    {
        var (sheet, address) = Cell(session, op, "cell");
        if (!op.Present("url"))
        {
            throw new CliException("needs \"url\" (a link, or null to remove the cell's link).");
        }

        string? url = op.String("url");
        var link = url is null ? null : new Hyperlink(url) { Tooltip = op.String("tooltip") };
        session.Apply(EditCommands.SetHyperlink(session.Workbook, sheet.Name, address.Row, address.Column, link));
    }

    // ---- Address helpers --------------------------------------------------

    private static (Worksheet Sheet, CellRange Range) Range(EditSession session, Op op, string field) =>
        Cells.ResolveRange(session.Workbook, op.Require(field), op.String("sheet"));

    private static (Worksheet Sheet, CellAddress Address) Cell(EditSession session, Op op, string field) =>
        Cells.ResolveCell(session.Workbook, op.Require(field), op.String("sheet"));
}
