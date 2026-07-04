using System.CommandLine;
using Cellestra.Cli.Infrastructure;
using Cellestra.Core.Serialization.Csv;
using Puffinote.Components.Core.Cli;

namespace Cellestra.Cli.Commands;

/// <summary>Factory methods for the options/arguments shared across commands (fresh instance each call).</summary>
internal static class Options
{
    public static Argument<string> File(string name = "file", string description = "Workbook file (or - for stdin)") =>
        new(name) { Description = description };

    public static Option<string?> Sheet() =>
        new("--sheet", "-s") { Description = "Worksheet name (default: the active/first sheet)" };

    public static Option<string?> Range() =>
        new("--range", "-r") { Description = "Cell range, e.g. A1:D20 (default: the used range)" };

    public static Option<string> Format()
    {
        var option = new Option<string>("--format", "-f")
        {
            Description = "table | csv | tsv | json",
            DefaultValueFactory = _ => "table"
        };
        return option;
    }

    public static Option<string?> Locale() =>
        new("--locale") { Description = "Locale override for parsing/formatting, e.g. en-US" };

    public static Option<string?> From() =>
        new("--from") { Description = "Input format override: ods | csv" };

    public static Option<string?> To() =>
        new("--to") { Description = "Output format override: ods | csv" };

    public static Option<string?> Delimiter() =>
        new("--delimiter", "-d") { Description = "CSV delimiter (default ,)" };

    public static Option<bool> InPlace() =>
        new("--in-place", "-i") { Description = "Edit the file in place (atomic write)" };

    public static Option<string?> Output() =>
        new("--output", "-o") { Description = "Write the result to this file instead of in place" };

    public static Option<bool> Formula() =>
        new("--formula") { Description = "Show formula sources instead of computed values" };

    public static WorkbookFormat? FormatOverride(string? value) => value?.ToLowerInvariant() switch
    {
        null or "" => null,
        "ods" => WorkbookFormat.Ods,
        "csv" or "tsv" => WorkbookFormat.Csv,
        _ => throw new CliException($"Unknown format '{value}'. Use ods or csv."),
    };

    public static CsvOptions? Csv(string? delimiter)
    {
        if (string.IsNullOrEmpty(delimiter))
        {
            return null;
        }

        if (delimiter.Length != 1)
        {
            throw new CliException("--delimiter must be a single character.");
        }

        return new CsvOptions { Delimiter = delimiter[0] };
    }

    /// <summary>Resolve the destination for an edit command: explicit -o, or the input file with --in-place.</summary>
    public static string EditTarget(string inputPath, bool inPlace, string? output)
    {
        if (output is not null)
        {
            return output;
        }

        if (inPlace)
        {
            return inputPath;
        }

        throw new CliException("Refusing to overwrite without --in-place or -o <file>.");
    }
}
