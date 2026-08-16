using Pivotask.Core.Model;
using Pivotask.Core.Serialization;
using Xunit;

namespace Pivotask.Cli.Tests;

/// <summary>
/// The whole verb set, end to end, over real files. One class, run sequentially, because these redirect
/// the process-global <see cref="Console"/> and change the working directory — the same arrangement the
/// sibling CLIs' tests use.
/// </summary>
[Collection("cli")]
public sealed class CliTests : IDisposable
{
    private readonly DirectoryInfo _scratch = Directory.CreateTempSubdirectory("ptask-tests");
    private readonly TextWriter _stdout = Console.Out;
    private readonly TextWriter _stderr = Console.Error;
    private readonly string _cwd = Directory.GetCurrentDirectory();
    private readonly string _board;

    public CliTests()
    {
        _board = Path.Combine(_scratch.FullName, "work.pivotask");
        var board = new Board();
        board.Labels.Add(new Label { Id = "design", Name = "design", Color = "#0e7490" });
        board.Labels.Add(new Label { Id = "ops", Name = "ops", Color = "#4d7c0f" });

        var inbox = new TaskGroup { Id = "g-inbox", Name = "Inbox" };
        var week = new TaskGroup { Id = "g-week", Name = "This week" };
        var overdue = new TaskItem
        {
            Id = "t-invoice",
            Title = "Halden Co. still owes on invoice 2231",
            Important = true,
            Due = DateOnly.FromDateTime(DateTime.Now).AddDays(-2),
        };
        var typeface = new TaskItem { Id = "t-typeface", Title = "Pick a typeface for the docs site" };
        typeface.Labels.Add("design");
        typeface.Steps.Add(new TaskStep { Id = "s1", Text = "Shortlist three", Done = true });
        typeface.Steps.Add(new TaskStep { Id = "s2", Text = "Test them at 13px" });

        inbox.Tasks.Add(overdue);
        inbox.Tasks.Add(typeface);
        week.Tasks.Add(new TaskItem { Id = "t-retro", Title = "Draft the Q3 retro agenda", Done = true });
        board.Groups.Add(inbox);
        board.Groups.Add(week);

        File.WriteAllText(_board, BoardCodec.Write(board));
        Directory.SetCurrentDirectory(_scratch.FullName);
    }

    public void Dispose()
    {
        Console.SetOut(_stdout);
        Console.SetError(_stderr);
        Directory.SetCurrentDirectory(_cwd);
        _scratch.Delete(recursive: true);
    }

    private static (int Exit, string Out) Run(params string[] args)
    {
        var output = new StringWriter();
        Console.SetOut(output);
        int exit = CliApplication.Run(args);
        return (exit, output.ToString());
    }

    private static string Error(params string[] args)
    {
        var errors = new StringWriter();
        Console.SetError(errors);
        Console.SetOut(new StringWriter());
        CliApplication.Run(args);
        return errors.ToString();
    }

    private Board Reread() => BoardCodec.Read(File.ReadAllText(_board));

    // ---- Reads ------------------------------------------------------------

    [Fact]
    public void List_groups_the_tasks_the_way_the_board_does()
    {
        var (exit, text) = Run("list");

        Assert.Equal(0, exit);
        Assert.Contains("Inbox", text, StringComparison.Ordinal);
        Assert.Contains("Halden Co.", text, StringComparison.Ordinal);
        Assert.Contains("[x]", text, StringComparison.Ordinal); // the done one is shown, marked
    }

    [Fact]
    public void List_filters_agree_with_the_board_s_own()
    {
        Assert.DoesNotContain("Draft the Q3", Run("list", "--open").Out, StringComparison.Ordinal);
        Assert.Contains("Halden Co.", Run("list", "--starred").Out, StringComparison.Ordinal);
        Assert.Contains("Halden Co.", Run("list", "--due-tomorrow").Out, StringComparison.Ordinal);
        Assert.Contains("Halden Co.", Run("list", "--due-this-week").Out, StringComparison.Ordinal);
        Assert.DoesNotContain("typeface", Run("list", "--starred").Out, StringComparison.Ordinal);
    }

    [Fact]
    public void List_narrows_by_group_and_by_label()
    {
        Assert.DoesNotContain("Halden", Run("list", "--group", "This week").Out, StringComparison.Ordinal);
        Assert.Contains("typeface", Run("list", "--label", "design").Out, StringComparison.Ordinal);
        Assert.DoesNotContain("Halden", Run("list", "--label", "design").Out, StringComparison.Ordinal);
    }

    [Fact]
    public void List_json_is_shaped_for_a_script_not_for_the_file()
    {
        // Flattened group, resolved label names: a caller piping into jq wants answers, not references.
        string json = Run("list", "--json").Out;

        Assert.Contains("\"group\": \"Inbox\"", json, StringComparison.Ordinal);
        Assert.Contains("\"design\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"version\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Show_prints_the_notes_the_steps_and_the_due_date()
    {
        string text = Run("show", "t-typeface").Out;

        Assert.Contains("Pick a typeface", text, StringComparison.Ordinal);
        Assert.Contains("steps (1/2)", text, StringComparison.Ordinal);
        Assert.Contains("Test them at 13px", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Groups_reports_open_and_total_counts()
    {
        string text = Run("groups").Out;

        Assert.Contains("Inbox", text, StringComparison.Ordinal);
        Assert.Contains("2 open / 2 total", text, StringComparison.Ordinal);
        Assert.Contains("0 open / 1 total", text, StringComparison.Ordinal);
    }

    // ---- Writes -----------------------------------------------------------

    [Fact]
    public void Add_writes_the_task_and_prints_its_id()
    {
        var (exit, text) = Run("add", "Book flights for the Lisbon workshop", "--group", "This week", "--star");

        Assert.Equal(0, exit);
        var added = Reread().Groups[1].Tasks.Last();
        Assert.Equal("Book flights for the Lisbon workshop", added.Title);
        Assert.True(added.Important);
        Assert.Contains(added.Id, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_attaches_labels_by_name_and_a_due_date_by_iso_day()
    {
        Run("add", "Renew the domain", "--label", "ops", "--due", "2026-09-01");

        var added = Reread().Groups[0].Tasks.Last();
        Assert.Equal(["ops"], added.Labels);
        Assert.Equal(new DateOnly(2026, 9, 1), added.Due);
    }

    [Fact]
    public void Add_refuses_a_date_that_is_not_a_date()
    {
        // There are no times in this format, so "next tuesday 3pm" has no reading that isn't a guess.
        Assert.Contains("YYYY-MM-DD", Error("add", "x", "--due", "next tuesday"), StringComparison.Ordinal);
    }

    [Fact]
    public void Done_and_undone_flip_a_task_and_save_it()
    {
        Run("done", "t-typeface");
        Assert.True(Reread().Locate("t-typeface")!.Value.Task.Done);

        Run("undone", "t-typeface");
        Assert.False(Reread().Locate("t-typeface")!.Value.Task.Done);
    }

    [Fact]
    public void Move_puts_a_task_in_another_group()
    {
        Run("move", "t-typeface", "--group", "This week");

        var board = Reread();
        Assert.DoesNotContain(board.Groups[0].Tasks, t => t.Id == "t-typeface");
        Assert.Contains(board.Groups[1].Tasks, t => t.Id == "t-typeface");
    }

    [Fact]
    public void Move_before_a_task_lands_in_front_of_it()
    {
        Run("move", "t-typeface", "--group", "This week", "--before", "t-retro");

        Assert.Equal("t-typeface", Reread().Groups[1].Tasks[0].Id);
    }

    // ---- Naming things ----------------------------------------------------

    [Fact]
    public void A_task_can_be_named_by_part_of_its_title()
    {
        // Ids are what a script uses; titles are how a person refers to their own tasks.
        Run("done", "Halden");

        Assert.True(Reread().Locate("t-invoice")!.Value.Task.Done);
    }

    [Fact]
    public void An_ambiguous_title_lists_the_candidates_rather_than_picking_one()
    {
        Run("add", "the same words here");
        Run("add", "the same words twice");

        string error = Error("done", "the same words");
        Assert.Contains("matches 2 tasks", error, StringComparison.Ordinal);
        Assert.Contains("Use one of those ids", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_group_says_what_the_board_actually_has()
    {
        Assert.Contains("Inbox, This week", Error("add", "x", "--group", "Backlog"), StringComparison.Ordinal);
    }

    // ---- Finding the board ------------------------------------------------

    [Fact]
    public void The_single_board_in_the_directory_is_found_without_being_named()
    {
        Assert.Equal(0, Run("groups").Exit);
    }

    [Fact]
    public void Two_boards_here_is_a_question_not_a_guess()
    {
        // Guessing which board `ptask done` meant is the kind of help that loses work.
        File.Copy(_board, Path.Combine(_scratch.FullName, "other.pivotask"));

        Assert.Contains("Say which one with --board", Error("groups"), StringComparison.Ordinal);
    }

    [Fact]
    public void No_board_here_says_what_to_do_instead()
    {
        File.Delete(_board);

        Assert.Contains("--board", Error("groups"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_board_from_the_future_is_a_clean_error()
    {
        File.WriteAllText(_board, """{ "version": 99, "groups": [], "labels": [] }""");

        string error = Error("list");
        Assert.StartsWith("ptask:", error, StringComparison.Ordinal);
        Assert.Contains("newer Pivotask", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_write_leaves_the_file_deterministic()
    {
        // Every save goes through the same codec, so a CLI edit doesn't reformat the whole file and
        // bury one real change in a thousand-line diff.
        Run("done", "t-typeface");
        string after = File.ReadAllText(_board);

        Assert.Equal(after, BoardCodec.Write(BoardCodec.Read(after)));
    }

    [Fact]
    public void A_write_preserves_a_field_this_build_does_not_understand()
    {
        var board = Reread();
        board.Unknown["theme"] = System.Text.Json.JsonDocument.Parse("\"dusk\"").RootElement.Clone();
        File.WriteAllText(_board, BoardCodec.Write(board));

        Run("done", "t-typeface");

        Assert.Contains("\"theme\"", File.ReadAllText(_board), StringComparison.Ordinal);
    }
}
