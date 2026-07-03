using System.Text;
using Cellestra.Core.Serialization.Ods;
using Puffinote.Components.Core.Cli;

namespace Puffinote.Cli.Infrastructure;

/// <summary>One entry in a notebook walk: absolute path, display name, tree depth, and file-vs-folder.</summary>
internal readonly record struct NotebookEntry(string Path, string Name, int Depth, bool IsDirectory);

/// <summary>
/// Reads the open notebook for the router's notebook-scoped commands (ARCHITECTURE.md §11). This is the CLI's
/// own thin, filesystem-only mirror of the shell's <c>NotebookService</c>/<c>NotebookNode</c> +
/// <c>NotebookSearchService</c> — the sub-CLIs keep their own <c>Workspace</c> too (per-app by design, since
/// the shell versions are Avalonia-coupled and carry incremental-index metadata the CLI doesn't need).
/// The rules that matter for parity are kept identical: dotfiles and the <c>.puffinote/</c> state
/// directory are skipped, directories sort before files, and <c>.ods</c> is searched by cell text via the
/// same <see cref="OdsReader"/> read path the shell uses.
/// </summary>
internal static class Notebook
{
    /// <summary>The per-notebook state directory, hidden from the walk (PRD §7.1) — also covered by the dotfile rule.</summary>
    public const string StateDirectoryName = ".puffinote";

    /// <summary>Resolve a notebook argument to an absolute path, or throw a clean CLI error if it isn't a folder.</summary>
    public static string ResolveRoot(string path)
    {
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full))
        {
            throw new CliException($"Notebook not found: {path}");
        }

        return full;
    }

    /// <summary>
    /// Every entry under <paramref name="root"/> in tree order (a folder's contents immediately after it,
    /// directories before files at each level, each alphabetical case-insensitive) — the same order the
    /// sidebar tree uses. Dotfiles and <c>.puffinote/</c> are skipped. Never throws on an unreadable
    /// directory (it yields nothing for that branch, matching the tree's degrade-don't-throw rule).
    /// </summary>
    public static IReadOnlyList<NotebookEntry> Walk(string root)
    {
        var entries = new List<NotebookEntry>();
        WalkInto(root, 0, entries);
        return entries;
    }

    /// <summary>Every file under <paramref name="root"/> (absolute path), in the same tree order as <see cref="Walk"/>.</summary>
    public static IEnumerable<string> Files(string root)
    {
        foreach (var entry in Walk(root))
        {
            if (!entry.IsDirectory)
            {
                yield return entry.Path;
            }
        }
    }

    /// <summary>The notebook-relative label for a file (for readable output).</summary>
    public static string Relative(string root, string path) => Path.GetRelativePath(root, path);

    /// <summary>
    /// Searchable text for a file, or null to skip it (binary / unreadable / an unparsable workbook) — the
    /// same classification the shell's index uses: notes as raw text, sheets by cell contents, everything
    /// else ignored. Never throws on one bad file.
    /// </summary>
    public static string? IndexText(string path)
    {
        try
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".md" or ".markdown" or ".mdown" or ".txt" => File.ReadAllText(path),
                ".ods" => ReadOdsText(path),
                _ => null,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            return null;
        }
    }

    private static void WalkInto(string directory, int depth, List<NotebookEntry> into)
    {
        FileSystemInfo[] entries;
        try
        {
            entries = new DirectoryInfo(directory).GetFileSystemInfos();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return; // an unreadable branch contributes nothing rather than aborting the whole walk
        }

        Array.Sort(entries, static (a, b) =>
        {
            var aDir = a is DirectoryInfo;
            var bDir = b is DirectoryInfo;
            if (aDir != bDir)
            {
                return aDir ? -1 : 1; // directories before files
            }

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var entry in entries)
        {
            var name = entry.Name;
            if (string.IsNullOrEmpty(name) || name[0] == '.')
            {
                continue; // dotfiles and the .puffinote state directory never enter the tree (PRD §7.1)
            }

            var isDirectory = entry is DirectoryInfo;
            into.Add(new NotebookEntry(entry.FullName, name, depth, isDirectory));
            if (isDirectory)
            {
                WalkInto(entry.FullName, depth + 1, into);
            }
        }
    }

    private static string ReadOdsText(string path)
    {
        using var stream = File.OpenRead(path);
        var workbook = OdsReader.Read(stream);

        var sb = new StringBuilder();
        foreach (var sheet in workbook.Worksheets)
        {
            foreach (var (_, _, cell) in sheet.EnumerateCells())
            {
                if (!cell.Value.IsEmpty)
                {
                    sb.Append(cell.Value.ToInvariantString()).Append('\n');
                }
            }
        }

        return sb.ToString();
    }
}
