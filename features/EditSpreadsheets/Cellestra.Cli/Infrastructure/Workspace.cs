using System.Globalization;
using Cellestra.Core.Dependencies;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Csv;
using Cellestra.Core.Serialization.Ods;
using Puffinote.Components.Core.Cli;

namespace Cellestra.Cli.Infrastructure;

/// <summary>The file format the CLI reads or writes.</summary>
public enum WorkbookFormat
{
    Ods,
    Csv,
}

/// <summary>A loaded workbook plus the original <c>.ods</c> package (null for CSV), kept so a save
/// can write unmodeled content (charts, pivots, …) back verbatim (PRD §8.1).</summary>
public sealed record LoadedWorkbook(Workbook Workbook, OdsPackage? Package);

/// <summary>
/// Loads, recalculates, and saves workbooks for the CLI — the same Core serializers, formula
/// engine, and atomic-save rule the Desktop app uses, so CLI output and GUI behaviour can't drift.
/// Handles <c>-</c> for stdin/stdout and never touches the network.
/// </summary>
public static class Workspace
{
    /// <summary>Pick a format from an explicit override, else the file extension.</summary>
    public static WorkbookFormat DetectFormat(string path, WorkbookFormat? explicitFormat)
    {
        if (explicitFormat is { } format)
        {
            return format;
        }

        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".ods" => WorkbookFormat.Ods,
            ".csv" => WorkbookFormat.Csv,
            ".tsv" => WorkbookFormat.Csv,
            _ => throw new CliException(
                $"Can't tell the format of '{path}' from its extension — pass --from ods|csv (or --to)."),
        };
    }

    public static LoadedWorkbook Load(string path, WorkbookFormat? from, CultureInfo? locale, CsvOptions? csv = null)
    {
        var format = DetectFormat(path, from);
        if (format == WorkbookFormat.Ods)
        {
            using var stream = OpenInput(path);
            (Workbook workbook, OdsPackage package) loaded;
            try
            {
                loaded = OdsReader.ReadWithPackage(stream);
            }
            catch (Exception ex) when (ex is not CliException)
            {
                throw new CliException($"Could not read '{path}' as an .ods file: {ex.Message}");
            }

            if (locale is not null)
            {
                loaded.workbook.Settings.Locale = locale;
            }

            return new LoadedWorkbook(loaded.workbook, loaded.package);
        }

        string text = ReadAllInput(path);
        var book = Workbook.CreateNew();
        if (locale is not null)
        {
            book.Settings.Locale = locale;
        }

        CsvReader.Fill(book, book.ActiveSheet, text, csv, locale);
        return new LoadedWorkbook(book, null);
    }

    /// <summary>Recompute every formula so cached values are fresh; returns the result for reporting.</summary>
    public static RecalculationResult Recalculate(Workbook workbook)
    {
        var engine = new RecalculationEngine(workbook);
        engine.RebuildGraph();
        return engine.RecalculateAll();
    }

    /// <summary>
    /// Save a workbook atomically: write to a sibling temp file then rename over the target, so a
    /// crash mid-write never corrupts the original (matches the app's save rule). Passing the source
    /// <paramref name="package"/> preserves unmodeled content verbatim. <c>-</c> writes to stdout.
    /// </summary>
    public static void SaveOds(Workbook workbook, OdsPackage? package, string path)
    {
        if (path == "-")
        {
            using var stdout = Console.OpenStandardOutput();
            OdsWriter.Write(workbook, stdout, package);
            return;
        }

        AtomicWrite(path, stream => OdsWriter.Write(workbook, stream, package));
    }

    public static void SaveText(string content, string path)
    {
        if (path == "-")
        {
            Console.Out.Write(content);
            return;
        }

        AtomicWrite(path, stream =>
        {
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            writer.Write(content);
        });
    }

    private static void AtomicWrite(string path, Action<Stream> write)
    {
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full) ?? ".";
        string temp = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = File.Create(temp))
            {
                write(stream);
            }

            File.Move(temp, full, overwrite: true); // atomic on the same volume
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private static Stream OpenInput(string path)
    {
        if (path == "-")
        {
            // Zip reading needs a seekable stream; buffer stdin into memory.
            var buffer = new MemoryStream();
            using (var stdin = Console.OpenStandardInput())
            {
                stdin.CopyTo(buffer);
            }

            buffer.Position = 0;
            return buffer;
        }

        if (!File.Exists(path))
        {
            throw new CliException($"File not found: {path}");
        }

        return File.OpenRead(path);
    }

    private static string ReadAllInput(string path)
    {
        if (path == "-")
        {
            return Console.In.ReadToEnd();
        }

        if (!File.Exists(path))
        {
            throw new CliException($"File not found: {path}");
        }

        return File.ReadAllText(path);
    }
}
