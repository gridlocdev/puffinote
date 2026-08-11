using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Pivotask.Core.Serialization;
using Xunit;

namespace Pivotask.Core.Tests;

/// <summary>
/// Taking the version on disk is a change like any other, so it goes in the history: a reload after a pull
/// is exactly the change someone most wants to be able to take back.
/// </summary>
public class BoardReloadTests
{
    private static string Json(params string[] groupNames)
    {
        var board = new Board();
        foreach (var name in groupNames)
        {
            board.Groups.Add(new TaskGroup { Id = name.ToLowerInvariant(), Name = name });
        }

        return BoardCodec.Write(board);
    }

    [Fact]
    public void Reload_replaces_the_board_and_can_be_undone()
    {
        var document = new BoardDocument(BoardCodec.Read(Json("Mine")));
        document.Execute(BoardCommands.AddGroup("Also Mine"));
        Assert.Equal(["Mine", "Also Mine"], document.Board.Groups.Select(g => g.Name));

        document.Reload(Json("Theirs"));
        Assert.Equal(["Theirs"], document.Board.Groups.Select(g => g.Name));
        Assert.False(document.IsDirty); // the board is the file now

        Assert.True(document.Undo());
        Assert.Equal(["Mine", "Also Mine"], document.Board.Groups.Select(g => g.Name));
        Assert.True(document.IsDirty); // and it differs from the file again, which it does

        Assert.True(document.Redo());
        Assert.Equal(["Theirs"], document.Board.Groups.Select(g => g.Name));
    }

    [Fact]
    public void Reload_leaves_itself_as_the_only_step_to_undo()
    {
        // A board command holds references into the object graph, and a reload rebuilds every object in it
        // from text — so an older command would revert against something that is no longer on the board and
        // silently do nothing. The reload is the one step back it can honestly offer.
        var document = new BoardDocument(BoardCodec.Read(Json("Mine")));
        document.Execute(BoardCommands.AddGroup("Also Mine"));

        document.Reload(Json("Theirs"));
        Assert.True(document.Undo()); // the reload
        Assert.False(document.CanUndo);
        Assert.Equal(["Mine", "Also Mine"], document.Board.Groups.Select(g => g.Name));
    }

    [Fact]
    public void Reload_rewrites_the_board_in_place_so_open_references_still_point_at_it()
    {
        // The views hold the Board object, not the document — a reload that swapped it for a new instance
        // would leave them repainting the old one.
        var document = new BoardDocument(BoardCodec.Read(Json("Mine")));
        var board = document.Board;

        document.Reload(Json("Theirs"));

        Assert.Same(board, document.Board);
        Assert.Equal(["Theirs"], board.Groups.Select(g => g.Name));
    }

    [Fact]
    public void Load_still_clears_the_history_because_it_is_a_different_board()
    {
        var document = new BoardDocument(BoardCodec.Read(Json("Mine")));
        document.Execute(BoardCommands.AddGroup("Also Mine"));

        document.Load(Json("Elsewhere"), path: null);

        Assert.False(document.CanUndo);
    }
}
