using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pivotask.Control.Views;
using Pivotask.Core;
using Plaintain.Control.Views;
using Puffinote.Components.Overlays;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The sidebar header's "New" menu (PRD §7.2): one plus button offering everything a notebook can hold,
/// where there used to be a New Note button — which made the one type it wrote the only type you could
/// reach without knowing a chord or hunting through a menu.
///
/// <para>What's worth pinning is the part that isn't cosmetic: each document command writes a file its own
/// editor can reopen (a board is <em>seeded</em>, not left empty), and "New File…" takes any extension
/// <em>except</em> the ones those commands own — refusing them by name rather than quietly producing a file
/// that fails to open.</para>
/// </summary>
public sealed class NewMenuTests
{
    /// <summary>The overlay currently showing something (the shell's own; panes nest their own inside).</summary>
    private static OverlayHost Overlay(MainWindow window) =>
        window.GetVisualDescendants().OfType<OverlayHost>().Single(h => h.IsOpen);

    private static void ClickOk(OverlayHost overlay) =>
        overlay.GetLogicalDescendants().OfType<Button>().First(b => b.IsDefault)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Run <paramref name="header"/> off the header's New menu and answer its dialog with <paramref name="name"/>.</summary>
    private static void CreateViaMenu(MainWindow window, string header, string name)
    {
        window.SidebarForTest.InvokeNewMenuItemForTest(header);
        Dispatcher.UIThread.RunJobs();

        var overlay = Overlay(window);
        overlay.GetLogicalDescendants().OfType<TextBox>().First().Text = name;
        ClickOk(overlay);
        Dispatcher.UIThread.RunJobs(); // the create-then-open chain resumes on the dialog's continuation
    }

    private static MainWindow OpenNotebook(DirectoryInfo dir)
    {
        var window = new MainWindow(new NotebookService());
        window.Show();
        window.OpenNotebook(dir.FullName);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void The_header_offers_one_New_button_listing_everything_a_notebook_can_hold()
    {
        var window = new MainWindow(new NotebookService());

        Assert.Equal(
            ["New Folder…", "New Note…", "New Spreadsheet…", "New Taskboard…", "New File…"],
            window.SidebarForTest.NewMenuHeadersForTest);

        // Every entry is a shell command too, so the palette and the File menu can't fall behind the menu.
        var ids = window.ShellCommandsForTest.Select(c => c.Id).ToList();
        Assert.Contains("shell.newTaskboard", ids);
        Assert.Contains("shell.newFile", ids);
    }

    [AvaloniaFact]
    public async Task A_new_taskboard_is_seeded_so_Pivotask_can_reopen_it()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-new-board");
        try
        {
            var window = OpenNotebook(dir);
            CreateViaMenu(window, "New Taskboard…", "work");

            // The extension is the command's, not the user's — they typed a base name only.
            var path = Path.Combine(dir.FullName, "work.pivotask");
            Assert.True(File.Exists(path));

            // Seeded with Pivotask's own starter board: an empty file here is a parse error, not a blank board.
            var reopened = BoardDocument.Open(path);
            Assert.Equal(
                BoardDocument.Starter().Groups.Select(g => g.Name),
                reopened.Board.Groups.Select(g => g.Name));

            // ...and it opened in a board pane rather than as text or an error.
            Assert.Equal(path, window.ActiveTabPathForTest);
            Assert.IsType<TaskBoardView>(window.ActiveEditorViewForTest);

            await window.IndexRebuildForTest; // let the background reindex finish before the folder goes
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task A_new_file_takes_whatever_extension_was_typed()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-new-file");
        try
        {
            var window = OpenNotebook(dir);
            CreateViaMenu(window, "New File…", "scratch.log");

            var path = Path.Combine(dir.FullName, "scratch.log");
            Assert.True(File.Exists(path));
            Assert.Empty(File.ReadAllBytes(path)); // a type the shell knows nothing about is not seeded

            // Plaintain opens it, which is the point of letting the extension through.
            Assert.IsType<PlainTextEditorView>(window.ActiveEditorViewForTest);

            await window.IndexRebuildForTest;
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task A_new_file_may_have_no_extension_at_all()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-new-bare");
        try
        {
            var window = OpenNotebook(dir);
            CreateViaMenu(window, "New File…", "LICENSE");

            Assert.True(File.Exists(Path.Combine(dir.FullName, "LICENSE")));

            await window.IndexRebuildForTest;
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaTheory]
    [InlineData("budget.ods")]
    [InlineData("todo.md")]
    [InlineData("work.pivotask")]
    [InlineData("work.pivotask.json")] // the name boards used to have is still spoken for
    public void A_new_file_refuses_an_extension_that_has_its_own_command(string name)
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-new-reserved");
        try
        {
            var window = OpenNotebook(dir);
            CreateViaMenu(window, "New File…", name);

            // Nothing was written: an empty .ods / .pivotask is a file its own editor then can't open —
            // exactly the trap the seeded commands exist to avoid.
            Assert.False(File.Exists(Path.Combine(dir.FullName, name)));

            // And the refusal says so rather than failing quietly.
            var shown = Overlay(window).GetLogicalDescendants().OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty)
                .ToList();
            Assert.Contains(shown, t => t.Contains("reserved", StringComparison.Ordinal));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task The_new_menu_creates_inside_the_selected_folder()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-new-target");
        try
        {
            var sub = Directory.CreateDirectory(Path.Combine(dir.FullName, "sub"));
            var window = OpenNotebook(dir);
            window.SidebarForTest.RevealInTree(sub.FullName);
            Dispatcher.UIThread.RunJobs();

            CreateViaMenu(window, "New File…", "notes.txt");

            Assert.True(File.Exists(Path.Combine(sub.FullName, "notes.txt")));

            await window.IndexRebuildForTest;
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
