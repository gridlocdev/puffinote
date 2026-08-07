using Pivotask.Core;
using Pivotask.Core.Model;
using Puffinote.Components.Core.Cli;

namespace Pivotask.Cli;

/// <summary>
/// Finding the board, and finding things in it. Every verb starts here, so "which board?" and "which
/// task?" have one answer each rather than one per command.
/// </summary>
internal static class Workspace
{
    /// <summary>The compound extension. <c>Path.GetExtension</c> returns ".json" for these, which is why
    /// nothing in the family may match a board by extension — it is a suffix.</summary>
    public const string Suffix = ".pivotask.json";

    /// <summary>
    /// Open the board at <paramref name="path"/>, or — when none was given — the single
    /// <c>*.pivotask.json</c> in the working directory.
    ///
    /// <para>Exactly one: with none there is nothing to act on, and with several, guessing which board
    /// <c>ptask done</c> meant is the kind of help that loses work. Both cases say what to do instead.</para>
    /// </summary>
    public static BoardDocument Open(string? path)
    {
        string resolved = path ?? Discover();
        if (!File.Exists(resolved))
        {
            throw new CliException($"'{resolved}' does not exist.");
        }

        return BoardDocument.Open(resolved);
    }

    private static string Discover()
    {
        var boards = Directory.GetFiles(Directory.GetCurrentDirectory(), $"*{Suffix}")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        return boards.Count switch
        {
            1 => boards[0],
            0 => throw new CliException(
                $"No *{Suffix} here. Pass one with --board, or run this where your board lives."),
            _ => throw new CliException(
                $"There are {boards.Count} boards here ({string.Join(", ", boards.Select(Path.GetFileName))}). " +
                "Say which one with --board."),
        };
    }

    /// <summary>
    /// The task <paramref name="reference"/> names: an id, or a unique case-insensitive substring of a
    /// title. Titles are how people refer to their own tasks; ids are what a script uses. An ambiguous
    /// title lists the candidates rather than picking one.
    /// </summary>
    public static (TaskGroup Group, TaskItem Task) FindTask(Board board, string reference)
    {
        if (board.Locate(reference) is { } byId)
        {
            return byId;
        }

        var matches = board.AllTasks()
            .Where(pair => pair.Task.Title.Contains(reference, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new CliException($"No task matches '{reference}'."),
            _ => throw new CliException(
                $"'{reference}' matches {matches.Count} tasks:\n" +
                string.Join('\n', matches.Select(m => $"  {m.Task.Id}  {m.Task.Title}")) +
                "\nUse one of those ids."),
        };
    }

    /// <summary>The group <paramref name="reference"/> names: an id, or a case-insensitive name.</summary>
    public static TaskGroup FindGroup(Board board, string reference)
    {
        if (board.FindGroup(reference) is { } byId)
        {
            return byId;
        }

        var matches = board.Groups
            .Where(g => g.Name.Equals(reference, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new CliException(
                $"No group called '{reference}'. This board has: {string.Join(", ", board.Groups.Select(g => g.Name))}."),
            _ => throw new CliException($"More than one group is called '{reference}'. Use its id."),
        };
    }

    /// <summary>The label <paramref name="reference"/> names, by id or name.</summary>
    public static Label FindLabel(Board board, string reference) =>
        board.FindLabel(reference)
        ?? board.Labels.Find(l => l.Name.Equals(reference, StringComparison.OrdinalIgnoreCase))
        ?? throw new CliException(
            $"No label called '{reference}'. This board has: " +
            (board.Labels.Count == 0 ? "none yet" : string.Join(", ", board.Labels.Select(l => l.Name))) + ".");
}
