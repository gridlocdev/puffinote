using System.Text;
using Typage.Engine.Document;

namespace Typage.Cli.Infrastructure;

/// <summary>
/// Reads and writes Markdown text for the CLI — the same engine the Desktop shell uses, so CLI
/// output and editor behaviour can't drift. Handles <c>-</c> for stdin/stdout, atomic saves
/// (temp + rename on the same volume, matching the app's save rule and PRD §7.1), and never
/// touches the network. Encoding handling here is deliberately simple (UTF-8, BOM-tolerant);
/// the full decision tree (BOM/CRLF preservation, read-only fallback) lives in the shell.
/// </summary>
public static class Workspace
{
    /// <summary>Read a document as a <see cref="TextDocument"/>. <c>-</c> reads stdin.</summary>
    public static TextDocument Load(string path)
    {
        return TextDocument.FromString(ReadAllInput(path));
    }

    /// <summary>The display title for a document (file name without extension, or "stdin").</summary>
    public static string TitleFor(string path) =>
        path == "-" ? "stdin" : Path.GetFileNameWithoutExtension(path);

    /// <summary>Write text atomically to <paramref name="path"/>; <c>-</c> writes stdout.</summary>
    public static void Save(string content, string path)
    {
        if (path == "-")
        {
            Console.Out.Write(content);
            return;
        }

        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full) ?? ".";
        var temp = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = File.Create(temp))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }

            File.Move(temp, full, overwrite: true); // atomic on the same volume
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
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
}
