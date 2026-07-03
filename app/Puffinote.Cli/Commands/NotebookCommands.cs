using System.CommandLine;
using System.Text;
using Puffinote.Cli.Infrastructure;
using Puffinote.Components.Core.Cli;

namespace Puffinote.Cli.Commands;

/// <summary>
/// The notebook-scoped commands (ARCHITECTURE.md §11) — <c>tree</c>, <c>search</c>, <c>info</c> — each a
/// thin call into <see cref="Notebook"/>. They mirror the sidebar's file tree + notebook search (PRD §7.2)
/// for scripts and CI. Reads go to stdout; nothing is ever written, so these need no <c>--in-place</c>.
/// The notebook argument defaults to the current directory.
/// </summary>
internal static class NotebookCommands
{
    // Snippet shaping mirrors the shell's SearchQuery so CLI and sidebar results read the same.
    private const int SnippetContext = 24;
    private const int SnippetLength = 72;

    private static Argument<string> NotebookArg() =>
        new("notebook") { Description = "Notebook folder (default: the current directory)", DefaultValueFactory = _ => "." };

    public static Command Tree()
    {
        var notebook = NotebookArg();
        var command = new Command("tree", "Print the notebook's file tree (folders first, dotfiles hidden)") { notebook };

        command.SetAction(parse =>
        {
            var root = Notebook.ResolveRoot(parse.GetValue(notebook)!);
            var sb = new StringBuilder();
            foreach (var entry in Notebook.Walk(root))
            {
                sb.Append(' ', entry.Depth * 2)
                  .Append(entry.Name)
                  .Append(entry.IsDirectory ? "/" : string.Empty)
                  .Append('\n');
            }

            Console.Out.Write(sb.ToString());
            return 0;
        });

        return command;
    }

    public static Command Search()
    {
        var query = new Argument<string>("query") { Description = "Text to search for (case-insensitive)" };
        var notebook = NotebookArg();
        var max = new Option<int>("--max", "-n") { Description = "Maximum results (default 200)", DefaultValueFactory = _ => 200 };
        var command = new Command("search", "Full-text search the notebook; notes as text, sheets by cell contents (PRD §7.2.3)")
        {
            query, notebook, max,
        };

        command.SetAction(parse =>
        {
            var needle = parse.GetValue(query)!.Trim();
            if (needle.Length == 0)
            {
                throw new CliException("Search query must not be empty.");
            }

            var root = Notebook.ResolveRoot(parse.GetValue(notebook)!);
            var limit = parse.GetValue(max);

            // Score every indexable file by case-insensitive substring count, ranked most-hits first, then
            // by label so the order is stable — the same ranking the sidebar's SearchQuery uses.
            var hits = new List<(string Relative, int Count, string Snippet)>();
            foreach (var file in Notebook.Files(root))
            {
                if (Notebook.IndexText(file) is not { } text)
                {
                    continue;
                }

                var count = CountOccurrences(text, needle);
                if (count > 0)
                {
                    hits.Add((Notebook.Relative(root, file), count, Snippet(text, needle)));
                }
            }

            var sb = new StringBuilder();
            foreach (var hit in hits
                .OrderByDescending(h => h.Count)
                .ThenBy(h => h.Relative, StringComparer.OrdinalIgnoreCase)
                .Take(limit))
            {
                sb.Append(hit.Relative).Append('\t').Append(hit.Count).Append('\t').Append(hit.Snippet).Append('\n');
            }

            Console.Out.Write(sb.ToString());
            return 0;
        });

        return command;
    }

    public static Command Info()
    {
        var notebook = NotebookArg();
        var command = new Command("info", "Summarize the notebook: file counts by kind and total size") { notebook };

        command.SetAction(parse =>
        {
            var root = Notebook.ResolveRoot(parse.GetValue(notebook)!);
            int files = 0, notes = 0, sheets = 0, other = 0;
            long bytes = 0;
            foreach (var file in Notebook.Files(root))
            {
                files++;
                try
                {
                    bytes += new FileInfo(file).Length;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A file that vanished mid-walk just doesn't contribute to the byte total.
                }

                switch (Path.GetExtension(file).ToLowerInvariant())
                {
                    case ".md" or ".markdown" or ".mdown" or ".txt":
                        notes++;
                        break;
                    case ".ods":
                        sheets++;
                        break;
                    default:
                        other++;
                        break;
                }
            }

            Console.Out.Write(
                $"notebook\t{root}\nfiles\t{files}\nnotes\t{notes}\nsheets\t{sheets}\nother\t{other}\nbytes\t{bytes}\n");
            return 0;
        });

        return command;
    }

    private static int CountOccurrences(string text, string query)
    {
        int count = 0, from = 0;
        while (from <= text.Length - query.Length)
        {
            var at = text.IndexOf(query, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                break;
            }

            count++;
            from = at + query.Length;
        }

        return count;
    }

    private static string Snippet(string text, string query)
    {
        var at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, at - SnippetContext);
        var end = Math.Min(text.Length, start + SnippetLength);

        var sb = new StringBuilder();
        if (start > 0)
        {
            sb.Append('…');
        }

        // Collapse whitespace runs (incl. newlines) so the snippet stays one line.
        var lastWasSpace = false;
        for (var i = start; i < end; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                }

                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        if (end < text.Length)
        {
            sb.Append('…');
        }

        return sb.ToString().Trim();
    }
}
