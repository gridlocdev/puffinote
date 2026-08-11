using Avalonia.Headless.XUnit;
using Pivotask.Core;
using Pivotask.Core.Model;
using Pivotask.Core.Serialization;
using Puffinote.Desktop.Panes;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The fourth pane type, and the routing rule that has to survive Plaintain's text fallback:
/// A board written before the rename is a <c>.pivotask.json</c> — a <b>suffix</b>, where <c>Path.GetExtension</c>
/// sees only <c>.json</c> — which
/// is on Plaintain's fast list. Proposal 0004 §7 records this as the one real interaction between the
/// two proposals; this is the regression test it asks for.
/// </summary>
public sealed class TaskBoardPaneTests : IDisposable
{
    private readonly DirectoryInfo _scratch = Directory.CreateTempSubdirectory("puffinote-board");

    public void Dispose() => _scratch.Delete(recursive: true);

    private string WriteBoard(string name = "work.pivotask")
    {
        var board = new Board();
        var inbox = new TaskGroup { Id = "g1", Name = "Inbox" };
        inbox.Tasks.Add(new TaskItem { Id = "t1", Title = "Chase invoice 2231" });
        board.Groups.Add(inbox);
        board.Groups.Add(new TaskGroup { Id = "g2", Name = "This week" });

        string path = Path.Combine(_scratch.FullName, name);
        File.WriteAllText(path, BoardCodec.Write(board));
        return path;
    }

    [AvaloniaFact]
    public void A_board_opens_in_a_board_pane_not_a_text_one()
    {
        // The whole point: a legacy ".pivotask.json" ends in ".json", so extension matching would send it to
        // Plaintain and the file would open as a wall of JSON.
        Assert.IsType<TaskBoardPane>(EditorPaneFactory.Create(WriteBoard()));
    }

    [AvaloniaFact]
    public void An_ordinary_json_file_still_goes_to_the_text_pane()
    {
        // The suffix rule must not swallow every .json on the machine.
        string path = Path.Combine(_scratch.FullName, "package.json");
        File.WriteAllText(path, "{ }");

        Assert.IsType<PlainTextPane>(EditorPaneFactory.Create(path));
    }

    [AvaloniaFact]
    public void The_suffix_match_ignores_case_the_way_a_filesystem_does()
    {
        Assert.True(EditorPaneFactory.CanOpen("/x/Work.PivoTask.JSON"));
    }

    [AvaloniaFact]
    public async Task Loading_reads_the_board_and_the_pane_reports_its_name()
    {
        string path = WriteBoard();
        using var pane = EditorPaneFactory.Create(path)!;
        await pane.LoadAsync(path);

        Assert.Equal("work.pivotask", pane.Title);
        Assert.False(pane.IsDirty);
    }

    [AvaloniaFact]
    public async Task The_outline_is_the_board_s_groups()
    {
        // Unlike Plaintain, which contributes nothing permanently, a board has real structure to lend.
        string path = WriteBoard();
        using var pane = EditorPaneFactory.Create(path)!;
        await pane.LoadAsync(path);

        Assert.Equal(["Inbox", "This week"], pane.GetOutline().Select(item => item.Title));
    }

    [AvaloniaFact]
    public async Task Saving_writes_through_the_document_so_the_bytes_stay_deterministic()
    {
        string path = WriteBoard();
        using var pane = EditorPaneFactory.Create(path)!;
        await pane.LoadAsync(path);
        string before = File.ReadAllText(path);

        await pane.SaveAsync();

        Assert.Equal(before, File.ReadAllText(path)); // a save that changed nothing changed no bytes
    }

    [AvaloniaFact]
    public async Task An_edit_makes_the_pane_dirty_and_a_save_makes_it_clean_again()
    {
        string path = WriteBoard();
        using var pane = EditorPaneFactory.Create(path)!;
        await pane.LoadAsync(path);

        var view = Assert.IsType<Pivotask.Control.Views.TaskBoardView>(pane.View);
        view.Document.Execute(Pivotask.Core.Editing.BoardCommands.AddGroup("Later"));
        Assert.True(pane.IsDirty);

        await pane.SaveAsync();
        Assert.False(pane.IsDirty);
        Assert.Equal(3, BoardDocument.Open(path).Board.Groups.Count);
    }

    [AvaloniaFact]
    public void The_pane_publishes_Pivotask_s_own_catalog()
    {
        using var pane = EditorPaneFactory.Create(WriteBoard())!;

        Assert.Contains(pane.Commands, c => c.Id == "task.add");
        Assert.Contains(pane.Commands, c => c.Id == "filter.dueSoon");
    }
}
