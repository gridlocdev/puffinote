using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Pivotask.Core.Query;
using Puffinote.Components.Core.Cli;

namespace Pivotask.Cli.Commands;

/// <summary>
/// The verb set. Every one is a <c>Pivotask.Core</c> call — the same brain the board draws — so what the
/// CLI does and what the app does cannot drift.
///
/// <para>Reads print to stdout and change nothing. Writes go through <see cref="BoardDocument"/>, which
/// means they are undoable in principle and atomic in practice, and they save immediately: a CLI has no
/// session to hold an unsaved edit in.</para>
/// </summary>
internal static class TaskCommands
{
    private static Option<string?> BoardOption() =>
        new("--board", "-b") { Description = $"The board file (default: the single *{Workspace.Suffix} here)" };

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ---- Reads ------------------------------------------------------------

    public static Command List()
    {
        var board = BoardOption();
        var group = new Option<string?>("--group", "-g") { Description = "Only this group" };
        var label = new Option<string?>("--label", "-l") { Description = "Only tasks with this label" };
        var starred = new Option<bool>("--starred") { Description = "Only starred tasks" };
        var dueSoon = new Option<bool>("--due-soon") { Description = "Only tasks due within a few days (or overdue)" };
        var open = new Option<bool>("--open") { Description = "Only tasks that aren't done" };
        var json = new Option<bool>("--json") { Description = "Emit JSON instead of a table" };

        var command = new Command("list", "List tasks") { board, group, label, starred, dueSoon, open, json };
        command.SetAction(parse =>
        {
            var document = Workspace.Open(parse.GetValue(board));
            var filter = parse.GetValue(starred) ? TaskFilter.Starred
                : parse.GetValue(dueSoon) ? TaskFilter.DueSoon
                : parse.GetValue(open) ? TaskFilter.Unfinished
                : TaskFilter.All;

            List<(TaskGroup Group, TaskItem Task)> matches = [.. BoardQuery.Search(document.Board, filter, query: null, Today)];

            if (parse.GetValue(group) is { } groupRef)
            {
                var target = Workspace.FindGroup(document.Board, groupRef);
                matches = [.. matches.Where(m => ReferenceEquals(m.Group, target))];
            }

            if (parse.GetValue(label) is { } labelRef)
            {
                var target = Workspace.FindLabel(document.Board, labelRef);
                matches = [.. matches.Where(m => m.Task.Labels.Contains(target.Id, StringComparer.Ordinal))];
            }

            Console.Out.Write(parse.GetValue(json)
                ? AsJson(document.Board, matches)
                : AsTable(document.Board, matches));
            return 0;
        });

        return command;
    }

    public static Command Show()
    {
        var board = BoardOption();
        var reference = new Argument<string>("task") { Description = "A task id, or part of its title" };
        var command = new Command("show", "Everything about one task") { reference, board };

        command.SetAction(parse =>
        {
            var document = Workspace.Open(parse.GetValue(board));
            var (group, task) = Workspace.FindTask(document.Board, parse.GetValue(reference)!);

            var sb = new StringBuilder();
            sb.Append(task.Done ? "[x] " : "[ ] ").AppendLine(task.Title);
            sb.Append("id:      ").AppendLine(task.Id);
            sb.Append("group:   ").AppendLine(group.Name);
            sb.Append("starred: ").AppendLine(task.Important ? "yes" : "no");
            sb.Append("due:     ").AppendLine(task.Due is { } due
                ? $"{Ids.Format(due)} ({BoardQuery.DescribeDue(due, Today)})"
                : "—");
            sb.Append("labels:  ").AppendLine(task.Labels.Count == 0
                ? "—"
                : string.Join(", ", task.Labels.Select(id => document.Board.FindLabel(id)?.Name ?? id)));

            if (task.Notes.Length > 0)
            {
                sb.AppendLine().AppendLine(task.Notes);
            }

            if (task.Steps.Count > 0)
            {
                var (done, total) = task.StepProgress;
                sb.AppendLine().Append("steps (").Append(done).Append('/').Append(total).AppendLine(")");
                foreach (var step in task.Steps)
                {
                    sb.Append("  ").Append(step.Done ? "[x] " : "[ ] ").AppendLine(step.Text);
                }
            }

            Console.Out.Write(sb.ToString());
            return 0;
        });

        return command;
    }

    public static Command Groups()
    {
        var board = BoardOption();
        var command = new Command("groups", "The board's groups, with their task counts") { board };

        command.SetAction(parse =>
        {
            var document = Workspace.Open(parse.GetValue(board));
            var sb = new StringBuilder();
            foreach (var group in document.Board.Groups)
            {
                int open = group.Tasks.Count(t => !t.Done);
                sb.Append(group.Id).Append("  ").Append(group.Name.PadRight(24))
                  .Append(open.ToString(CultureInfo.InvariantCulture)).Append(" open / ")
                  .Append(group.Tasks.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" total");
            }

            Console.Out.Write(sb.ToString());
            return 0;
        });

        return command;
    }

    // ---- Writes -----------------------------------------------------------

    public static Command Add()
    {
        var board = BoardOption();
        var title = new Argument<string>("title") { Description = "What the task is" };
        var group = new Option<string?>("--group", "-g") { Description = "Which group (default: the first)" };
        var label = new Option<string[]>("--label", "-l") { Description = "A label to attach (repeatable)", Arity = ArgumentArity.ZeroOrMore };
        var due = new Option<string?>("--due") { Description = "A due date, YYYY-MM-DD" };
        var star = new Option<bool>("--star") { Description = "Star it" };

        var command = new Command("add", "Add a task") { title, group, label, due, star, board };
        command.SetAction(parse =>
        {
            var document = Workspace.Open(parse.GetValue(board));
            if (document.Board.Groups.Count == 0)
            {
                throw new CliException("This board has no groups to add a task to.");
            }

            var target = parse.GetValue(group) is { } groupRef
                ? Workspace.FindGroup(document.Board, groupRef)
                : document.Board.Groups[0];

            var task = new TaskItem
            {
                Id = Ids.New(),
                Title = parse.GetValue(title)!,
                Important = parse.GetValue(star),
            };

            if (parse.GetValue(due) is { Length: > 0 } dueText)
            {
                task.Due = Ids.ParseDate(dueText)
                    ?? throw new CliException($"'{dueText}' isn't a date. Dates are YYYY-MM-DD — this format has no times.");
            }

            foreach (string name in parse.GetValue(label) ?? [])
            {
                task.Labels.Add(Workspace.FindLabel(document.Board, name).Id);
            }

            document.Execute(BoardCommands.AddTask(target, task));
            document.Save();
            Console.Out.WriteLine($"{task.Id}  {task.Title}");
            return 0;
        });

        return command;
    }

    public static Command Done(bool done)
    {
        var board = BoardOption();
        var reference = new Argument<string>("task") { Description = "A task id, or part of its title" };
        var command = new Command(done ? "done" : "undone", done ? "Complete a task" : "Reopen a task") { reference, board };

        command.SetAction(parse =>
        {
            var document = Workspace.Open(parse.GetValue(board));
            var (_, task) = Workspace.FindTask(document.Board, parse.GetValue(reference)!);
            document.Execute(BoardCommands.SetDone(task, done));
            document.Save();
            Console.Out.WriteLine($"{(done ? "[x]" : "[ ]")} {task.Title}");
            return 0;
        });

        return command;
    }

    public static Command Move()
    {
        var board = BoardOption();
        var reference = new Argument<string>("task") { Description = "A task id, or part of its title" };
        var group = new Option<string>("--group", "-g") { Description = "The group to move it to", Required = true };
        var before = new Option<string?>("--before") { Description = "Put it in front of this task" };

        var command = new Command("move", "Move a task to another group") { reference, group, before, board };
        command.SetAction(parse =>
        {
            var document = Workspace.Open(parse.GetValue(board));
            var (_, task) = Workspace.FindTask(document.Board, parse.GetValue(reference)!);
            var target = Workspace.FindGroup(document.Board, parse.GetValue(group)!);
            var anchor = parse.GetValue(before) is { } beforeRef
                ? Workspace.FindTask(document.Board, beforeRef).Task
                : null;

            document.Execute(BoardCommands.MoveTask(document.Board, task, target, anchor));
            document.Save();
            Console.Out.WriteLine($"{task.Title} → {target.Name}");
            return 0;
        });

        return command;
    }

    // ---- Rendering --------------------------------------------------------

    private static string AsTable(Board board, List<(TaskGroup Group, TaskItem Task)> matches)
    {
        if (matches.Count == 0)
        {
            return "No tasks match.\n";
        }

        var sb = new StringBuilder();
        foreach (var byGroup in matches.GroupBy(m => m.Group))
        {
            sb.AppendLine(byGroup.Key.Name);
            foreach (var (_, task) in byGroup)
            {
                sb.Append("  ").Append(task.Done ? "[x] " : "[ ] ")
                  .Append(task.Important ? "★ " : "  ")
                  .Append(task.Id).Append("  ")
                  .Append(task.Title);

                var tail = new List<string>();
                if (task.Due is { } due)
                {
                    tail.Add(BoardQuery.DescribeDue(due, Today));
                }

                if (task.Labels.Count > 0)
                {
                    tail.AddRange(task.Labels.Select(id => "#" + (board.FindLabel(id)?.Name ?? id)));
                }

                var (stepsDone, stepsTotal) = task.StepProgress;
                if (stepsTotal > 0)
                {
                    tail.Add($"{stepsDone}/{stepsTotal}");
                }

                if (tail.Count > 0)
                {
                    sb.Append("  (").Append(string.Join(", ", tail)).Append(')');
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string AsJson(Board board, List<(TaskGroup Group, TaskItem Task)> matches)
    {
        // Shaped for a script, not for the file format: the group is flattened onto each task and label
        // ids are resolved to names, because a caller piping this into jq wants answers, not references.
        var rows = matches.Select(m => new
        {
            id = m.Task.Id,
            title = m.Task.Title,
            group = m.Group.Name,
            done = m.Task.Done,
            important = m.Task.Important,
            due = m.Task.Due is { } d ? Ids.Format(d) : null,
            labels = m.Task.Labels.Select(id => board.FindLabel(id)?.Name ?? id).ToArray(),
            steps = new { done = m.Task.StepProgress.Done, total = m.Task.StepProgress.Total },
        });

        return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }
}
