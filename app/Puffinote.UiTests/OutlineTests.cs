using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Services;
using Cellestra.Editor;
using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Sidebar;
using Puffinote.Desktop.Views;
using Typage.Control.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>Phase 4D: the document-outline panel (PRD §7.2.3).</summary>
public sealed class OutlineTests
{
    [AvaloniaFact]
    public async Task Markdown_outline_lists_headings_with_levels_and_reveal_moves_the_caret()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-outline-md");
        try
        {
            var path = Path.Combine(dir.FullName, "note.md");
            File.WriteAllText(path, "# Title\n\n## Section\n\nbody\n\n# Second\n");

            var pane = new MarkdownPane();
            await pane.LoadAsync(path);

            var outline = pane.GetOutline();
            Assert.Equal(["Title", "Section", "Second"], outline.Select(o => o.Title));
            Assert.Equal([1, 2, 1], outline.Select(o => o.Level));

            outline[2].Reveal(); // jump to "# Second" (0-based line 6)
            Assert.Equal(6, ((MarkdownEditorView)pane.View).CaretPosition.Line);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void Markdown_outline_is_empty_when_there_are_no_headings()
    {
        var pane = new MarkdownPane();
        ((MarkdownEditorView)pane.View).LoadText("just prose, no headings\n");
        Assert.Empty(pane.GetOutline());
    }

    [AvaloniaFact]
    public async Task Sheet_outline_lists_worksheets_and_reveal_activates_the_sheet()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-outline-ods");
        try
        {
            var path = Path.Combine(dir.FullName, "book.ods");
            File.WriteAllBytes(path, WorkbookSession.NewBlank().SerializeToBytes());

            var pane = Assert.IsType<SheetPane>(EditorPaneFactory.Create(path));
            await pane.LoadAsync(path);
            var view = (SpreadsheetEditorView)pane.View;
            view.Session.AddSheet(); // a second sheet; AddSheet activates it

            var outline = pane.GetOutline();
            Assert.Equal(2, outline.Count);

            var first = view.Session.Workbook.Worksheets[0];
            outline[0].Reveal(); // selecting the first outline entry re-activates the first sheet
            Assert.Same(first, view.Session.ActiveSheet);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void Outline_panel_is_registered_and_activatable()
    {
        var sidebar = new SidebarView();
        var window = new Window { Content = sidebar };
        window.Show();

        sidebar.Activate("outline");
        Assert.Equal("outline", sidebar.ActivePanelId);

        sidebar.SetOutline([new OutlineItem("H1", 1, () => { })]);
        Assert.Single(sidebar.OutlineItemsForTest);
    }

    [AvaloniaFact]
    public async Task Activating_the_outline_panel_populates_it_from_the_active_note()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-outline-e2e");
        try
        {
            var path = Path.Combine(dir.FullName, "doc.md");
            File.WriteAllText(path, "# Alpha\n\n## Beta\n");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(path);

            window.SidebarForTest.Activate("outline"); // PanelActivated → the shell refreshes the outline
            Assert.Equal(["Alpha", "Beta"], window.SidebarForTest.OutlineItemsForTest.Select(o => o.Title));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
