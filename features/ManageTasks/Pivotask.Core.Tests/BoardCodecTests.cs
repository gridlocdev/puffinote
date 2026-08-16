using System.Text.Json;
using Pivotask.Core.Model;
using Pivotask.Core.Serialization;
using Xunit;

namespace Pivotask.Core.Tests;

/// <summary>
/// The format's three promises, each of which exists because this file lives in git: a save that
/// changed nothing changes no bytes; a field we don't understand survives us; and a file from the
/// future is refused rather than half-read.
/// </summary>
public class BoardCodecTests
{
    private static Board Sample()
    {
        var board = new Board();
        board.Labels.Add(new Label { Id = "design", Name = "design", Color = "#0e7490" });
        var group = new TaskGroup { Id = "g1", Name = "Inbox" };
        var task = new TaskItem
        {
            Id = "t1",
            Title = "Rewrite the onboarding empty states",
            Notes = "Keep it short.",
            Due = new DateOnly(2026, 7, 30),
            Important = true,
        };
        task.Labels.Add("design");
        task.Steps.Add(new TaskStep { Id = "s1", Text = "Shortlist three", Done = true });
        group.Tasks.Add(task);
        board.Groups.Add(group);
        return board;
    }

    [Fact]
    public void A_board_round_trips_through_json()
    {
        var read = BoardCodec.Read(BoardCodec.Write(Sample()));
        var task = read.Groups[0].Tasks[0];

        Assert.Equal("Inbox", read.Groups[0].Name);
        Assert.Equal("Rewrite the onboarding empty states", task.Title);
        Assert.Equal(new DateOnly(2026, 7, 30), task.Due);
        Assert.True(task.Important);
        Assert.Equal(["design"], task.Labels);
        Assert.True(task.Steps[0].Done);
    }

    [Fact]
    public void Writing_the_same_board_twice_produces_the_same_bytes()
    {
        // The whole point of a deterministic writer: a save that changed nothing must not show up in
        // `git diff`, or the file stops being reviewable.
        string once = BoardCodec.Write(Sample());
        string twice = BoardCodec.Write(BoardCodec.Read(once));

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// The reading a board was left in travels with the board.
    ///
    /// <para>A view state, but <em>this board's</em> view state — a board of twelve wide columns and a
    /// board of three checklists want different readings, and answering that once per board beats
    /// answering it once per person for every board they will ever open. Same reason
    /// <c>collapsed</c> is in the file.</para>
    /// </summary>
    [Fact]
    public void The_reading_a_board_was_left_in_travels_with_the_board()
    {
        var board = Sample();
        board.View = BoardLayout.Board;

        string json = BoardCodec.Write(board);
        Assert.Contains("\"view\": \"board\"", json, StringComparison.Ordinal);
        Assert.Equal(BoardLayout.Board, BoardCodec.Read(json).View);

        board.View = BoardLayout.List;
        Assert.Equal(BoardLayout.List, BoardCodec.Read(BoardCodec.Write(board)).View);
    }

    /// <summary>
    /// A board nobody has switched has no <c>view</c> key at all — so opening one, in a build that
    /// understands the key, is not a diff. "Nobody has said" and "somebody chose the default" are
    /// different answers, and only the first may be overridden by the reader's own preference.
    /// </summary>
    [Fact]
    public void A_board_nobody_has_switched_gains_no_view_key()
    {
        string json = BoardCodec.Write(Sample());

        Assert.DoesNotContain("\"view\"", json, StringComparison.Ordinal);
        Assert.Null(BoardCodec.Read(json).View);
    }

    /// <summary>
    /// A word this build doesn't know in that one field reads as "nobody has said", not as an error. The
    /// file is hand-edited, and a typo in the field that picks between two views must not cost you the
    /// board.
    /// </summary>
    [Fact]
    public void A_view_this_build_does_not_know_costs_that_field_and_nothing_else()
    {
        const string json = """
        {
          "version": 1,
          "view": "kanban",
          "labels": [],
          "groups": [ { "id": "g1", "name": "Inbox", "collapsed": false, "foldDone": false, "tasks": [] } ]
        }
        """;

        var board = BoardCodec.Read(json);

        Assert.Null(board.View);
        Assert.Equal("Inbox", board.Groups[0].Name); // …and the rest of the file is still there
    }

    [Fact]
    public void The_file_ends_with_a_newline_and_uses_lf()
    {
        string json = BoardCodec.Write(Sample());

        Assert.EndsWith("\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', json);
    }

    [Fact]
    public void A_field_we_do_not_understand_survives_a_round_trip()
    {
        // A file written by a newer Pivotask, opened and saved by this one, must not lose data.
        const string json = """
        {
          "version": 1,
          "labels": [],
          "groups": [
            {
              "id": "g1",
              "name": "Inbox",
              "collapsed": false,
              "foldDone": false,
              "swimlane": "left",
              "tasks": [
                { "id": "t1", "title": "x", "notes": "", "due": null, "important": false, "done": false,
                  "labels": [], "steps": [], "assignee": { "name": "Nadia" } }
              ]
            }
          ],
          "theme": "dusk"
        }
        """;

        string written = BoardCodec.Write(BoardCodec.Read(json));

        Assert.Contains("\"swimlane\"", written, StringComparison.Ordinal);
        Assert.Contains("\"Nadia\"", written, StringComparison.Ordinal);
        Assert.Contains("\"theme\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_field_lands_in_the_same_place_every_time()
    {
        // Ordered on write, or the one thing that made the file otherwise-deterministic would churn.
        const string json = """
        { "version": 1, "groups": [], "labels": [], "zebra": 1, "alpha": 2, "mid": 3 }
        """;

        var board = BoardCodec.Read(json);
        Assert.Equal(BoardCodec.Write(board), BoardCodec.Write(BoardCodec.Read(BoardCodec.Write(board))));
        Assert.True(
            BoardCodec.Write(board).IndexOf("\"alpha\"", StringComparison.Ordinal)
            < BoardCodec.Write(board).IndexOf("\"zebra\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_board_from_the_future_is_refused_rather_than_half_read()
    {
        var thrown = Assert.Throws<BoardFormatException>(
            () => BoardCodec.Read("""{ "version": 99, "groups": [], "labels": [] }"""));

        Assert.Contains("newer Pivotask", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_json_is_a_clean_error_not_a_crash()
    {
        Assert.Throws<BoardFormatException>(() => BoardCodec.Read("{ not json"));
        Assert.Throws<BoardFormatException>(() => BoardCodec.Read("[1, 2, 3]"));
    }

    [Fact]
    public void A_missing_due_date_is_null_not_an_empty_string()
    {
        var board = BoardCodec.Read("""
        { "version": 1, "labels": [], "groups": [ { "id": "g", "name": "n", "tasks": [
          { "id": "t", "title": "x" } ] } ] }
        """);

        Assert.Null(board.Groups[0].Tasks[0].Due);
        Assert.Contains("\"due\": null", BoardCodec.Write(board), StringComparison.Ordinal);
    }

    [Fact]
    public void A_due_date_is_written_as_a_plain_iso_day()
    {
        // No time, no offset. The moment a time is stored, "whose Thursday?" arrives with it.
        var board = Sample();
        Assert.Contains("\"due\": \"2026-07-30\"", BoardCodec.Write(board), StringComparison.Ordinal);
    }

    [Fact]
    public void The_starter_board_is_the_same_bytes_every_time()
    {
        // Ids are random in general; the starter's are seeded, so "new board" doesn't churn in git.
        Assert.Equal(BoardCodec.Write(BoardDocument.Starter()), BoardCodec.Write(BoardDocument.Starter()));
    }

    /// <summary>
    /// A new board starts as three empty groups naming a <em>state of work</em>, which is what a board is
    /// for. They were "Inbox / This week / Done", which mixed two ideas — two of those said <em>when</em>
    /// and one said <em>whether</em> — so a task in flight had no column that fit, and renaming them was
    /// the first thing anyone did with a new board.
    /// </summary>
    [Fact]
    public void A_new_board_starts_as_three_empty_states_of_work()
    {
        var board = BoardDocument.Starter();

        Assert.Equal(["To do", "In progress", "Done"], board.Groups.Select(g => g.Name));

        // Empty, not seeded with samples: a starter board is somewhere to put the first real task, not
        // something to clear out first.
        Assert.All(board.Groups, g => Assert.Empty(g.Tasks));
    }

    [Fact]
    public void It_is_json_anything_can_read()
    {
        // The ".json" tail is a promise, not decoration.
        using var parsed = JsonDocument.Parse(BoardCodec.Write(Sample()));
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
    }
}
