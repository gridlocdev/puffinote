using System.CommandLine;
using Cellestra.Cli.Infrastructure;
using Puffinote.Components.Core.Cli;

namespace Cellestra.Cli.Commands;

/// <summary>
/// The formatting and layout verbs a person at a shell reaches for — <c>style</c>, <c>width</c>,
/// <c>height</c>, <c>freeze</c>, <c>clear</c>, <c>settings</c>, and the sheet-arranging pair — each a
/// thin front on the matching <c>apply</c> op, so a verb and its op can never disagree.
/// </summary>
internal static class LayoutCommands
{
    public static Command Style()
    {
        var file = Options.File();
        var range = new Argument<string>("range") { Description = "Range (or cell, name, table) to format, e.g. Sheet1.A1:D1" };
        var bold = OnOff("--bold");
        var italic = OnOff("--italic");
        var underline = OnOff("--underline");
        var strike = OnOff("--strikethrough");
        var wrap = OnOff("--wrap");
        var font = new Option<string?>("--font") { Description = "Font family" };
        var size = new Option<double?>("--size") { Description = "Font size in points" };
        var color = new Option<string?>("--color") { Description = "Text color, #RRGGBB" };
        var fill = new Option<string?>("--fill") { Description = "Background, #RRGGBB (or none)" };
        var align = new Option<string?>("--align") { Description = "general | left | center | right" };
        var valign = new Option<string?>("--valign") { Description = "top | middle | bottom" };
        var format = new Option<string?>("--number-format") { Description = "Number format code, e.g. \"#,##0.00\" or General" };
        var border = new Option<string?>("--border") { Description = "none | thin | medium | thick | dashed | dotted | double (all four edges)" };
        var borderColor = new Option<string?>("--border-color") { Description = "Border color, #RRGGBB" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("style", "Format a range: font, fill, alignment, number format, border")
        {
            file, range, bold, italic, underline, strike, wrap, font, size, color, fill, align, valign, format, border, borderColor, inPlace, output, json,
        };

        command.SetAction(parse => Dispatch(parse, file, inPlace, output, json, "style", new Dictionary<string, object?>
        {
            ["range"] = parse.GetValue(range),
            ["bold"] = Flag(parse, bold),
            ["italic"] = Flag(parse, italic),
            ["underline"] = Flag(parse, underline),
            ["strikethrough"] = Flag(parse, strike),
            ["wrap"] = Flag(parse, wrap),
            ["font"] = parse.GetValue(font),
            ["size"] = parse.GetValue(size),
            ["color"] = parse.GetValue(color),
            ["fill"] = parse.GetValue(fill),
            ["align"] = parse.GetValue(align),
            ["valign"] = parse.GetValue(valign),
            ["format"] = parse.GetValue(format),
            ["border"] = parse.GetValue(border),
            ["borderColor"] = parse.GetValue(borderColor),
        }));

        return command;
    }

    public static Command Width()
    {
        var file = Options.File();
        var columns = new Argument<string>("columns") { Description = "Column or span, e.g. B or B:D" };
        var width = new Argument<double>("width") { Description = "Width in points" };
        var sheet = Options.Sheet();
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("width", "Set column width(s)") { file, columns, width, sheet, inPlace, output, json };
        command.SetAction(parse => Dispatch(parse, file, inPlace, output, json, "width", new Dictionary<string, object?>
        {
            ["columns"] = parse.GetValue(columns),
            ["width"] = parse.GetValue(width),
            ["sheet"] = parse.GetValue(sheet),
        }));
        return command;
    }

    public static Command Height()
    {
        var file = Options.File();
        var rows = new Argument<string>("rows") { Description = "Row or span, e.g. 1 or 2:5" };
        var height = new Argument<double>("height") { Description = "Height in points" };
        var sheet = Options.Sheet();
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("height", "Set row height(s)") { file, rows, height, sheet, inPlace, output, json };
        command.SetAction(parse => Dispatch(parse, file, inPlace, output, json, "height", new Dictionary<string, object?>
        {
            ["rows"] = parse.GetValue(rows),
            ["height"] = parse.GetValue(height),
            ["sheet"] = parse.GetValue(sheet),
        }));
        return command;
    }

    public static Command Freeze()
    {
        var file = Options.File();
        var rows = new Option<int>("--rows") { Description = "Rows to freeze at the top (0 = none)" };
        var columns = new Option<int>("--columns") { Description = "Columns to freeze at the left (0 = none)" };
        var sheet = Options.Sheet();
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("freeze", "Freeze panes (--rows 0 --columns 0 unfreezes)") { file, rows, columns, sheet, inPlace, output, json };
        command.SetAction(parse => Dispatch(parse, file, inPlace, output, json, "freeze", new Dictionary<string, object?>
        {
            ["rows"] = parse.GetValue(rows),
            ["columns"] = parse.GetValue(columns),
            ["sheet"] = parse.GetValue(sheet),
        }));
        return command;
    }

    public static Command Clear()
    {
        var file = Options.File();
        var range = new Argument<string>("range") { Description = "Range to clear, e.g. Sheet1.A2:D20" };
        var what = new Option<string>("--what")
        {
            Description = "contents | formats | all",
            DefaultValueFactory = _ => "contents",
        };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("clear", "Clear a range's contents, formats, or everything") { file, range, what, inPlace, output, json };
        command.SetAction(parse => Dispatch(parse, file, inPlace, output, json, "clear", new Dictionary<string, object?>
        {
            ["range"] = parse.GetValue(range),
            ["what"] = parse.GetValue(what),
        }));
        return command;
    }

    public static Command Settings()
    {
        var file = Options.File();
        var locale = new Option<string?>("--locale") { Description = "Document locale, e.g. en-US (parsing and display)" };
        var calc = new Option<string?>("--calc") { Description = "automatic | manual" };
        var active = new Option<string?>("--active-sheet") { Description = "The sheet the app opens on" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("settings", "Set document settings: locale, calculation mode, active sheet") { file, locale, calc, active, inPlace, output, json };
        command.SetAction(parse => Dispatch(parse, file, inPlace, output, json, "settings", new Dictionary<string, object?>
        {
            ["locale"] = parse.GetValue(locale),
            ["calc"] = parse.GetValue(calc),
            ["activeSheet"] = parse.GetValue(active),
        }));
        return command;
    }

    public static Command DuplicateSheet()
    {
        var file = Options.File();
        var name = new Argument<string>("name") { Description = "Sheet to copy" };
        var asName = new Option<string?>("--as") { Description = "Name for the copy (default: the app's next free name)" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("duplicate-sheet", "Copy a worksheet, contents and formatting") { file, name, asName, inPlace, output, json };
        command.SetAction(parse => Dispatch(parse, file, inPlace, output, json, "duplicate-sheet", new Dictionary<string, object?>
        {
            ["name"] = parse.GetValue(name),
            ["as"] = parse.GetValue(asName),
        }));
        return command;
    }

    public static Command MoveSheet()
    {
        var file = Options.File();
        var name = new Argument<string>("name") { Description = "Sheet to move" };
        var to = new Argument<int>("to") { Description = "New 1-based position" };
        var inPlace = Options.InPlace();
        var output = Options.Output();
        var json = Options.Json();
        var command = new Command("move-sheet", "Reorder a worksheet") { file, name, to, inPlace, output, json };
        command.SetAction(parse => Dispatch(parse, file, inPlace, output, json, "move-sheet", new Dictionary<string, object?>
        {
            ["name"] = parse.GetValue(name),
            ["to"] = parse.GetValue(to),
        }));
        return command;
    }

    // ---- Shared -----------------------------------------------------------

    /// <summary>A tri-state switch: <c>--bold</c> sets, <c>--bold false</c> (or off/no) clears, absent leaves alone.</summary>
    private static Option<string?> OnOff(string name) => new(name)
    {
        Description = $"{name[2..]} on (`{name} false` turns it off)",
        Arity = ArgumentArity.ZeroOrOne,
    };

    internal static bool? Flag(ParseResult parse, Option<string?> option)
    {
        var result = parse.GetResult(option);
        if (result is null)
        {
            return null; // not on the command line: leave that attribute alone
        }

        string value = parse.GetValue(option) ?? string.Empty;
        return value.ToLowerInvariant() switch
        {
            "" or "true" or "on" or "yes" => true,
            "false" or "off" or "no" => false,
            _ => throw new CliException($"'{value}' must be true or false."),
        };
    }

    internal static int Dispatch(ParseResult parse, Argument<string> file, Option<bool> inPlace, Option<string?> output, Option<bool> json, string op, Dictionary<string, object?> fields)
    {
        string path = parse.GetValue(file)!;
        string target = Options.EditTarget(path, parse.GetValue(inPlace), parse.GetValue(output));
        // Absent options must be absent keys, not nulls: "fill": null means "remove the fill".
        var present = fields.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        return EditSession.Run(path, target, null, parse.GetValue(json), session => ApplyCommands.RunOp(session, op, present));
    }
}
