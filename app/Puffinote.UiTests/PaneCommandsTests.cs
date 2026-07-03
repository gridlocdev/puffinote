using Avalonia.Headless.XUnit;
using Puffinote.Desktop.Panes;
using Typage.Control.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>Phase 3: each pane exposes an editor catalog for the merged Command Palette.</summary>
public sealed class PaneCommandsTests
{
    [AvaloniaFact]
    public void Markdown_pane_shows_the_formatting_toolbar_by_default_and_can_toggle_it()
    {
        var pane = new MarkdownPane();
        var view = Assert.IsType<MarkdownEditorView>(pane.View);

        Assert.True(pane.ToolbarVisible); // shown by default
        Assert.True(view.ShowCommandBar);

        pane.ToolbarVisible = false;
        Assert.False(view.ShowCommandBar);
        pane.ToolbarVisible = true;
        Assert.True(view.ShowCommandBar);
    }

    [AvaloniaFact]
    public void Markdown_pane_exposes_typages_full_editing_catalog()
    {
        var pane = new MarkdownPane();
        var ids = pane.Commands.Select(c => c.Id).ToHashSet();

        // Not just undo/redo any more — the embeddable control now publishes Typage's editing/formatting
        // catalog, so the merged palette shows the note's own commands.
        Assert.Contains("edit.undo", ids);
        Assert.Contains("edit.redo", ids);
        Assert.Contains("format.bold", ids);
        Assert.Contains("format.italic", ids);
        Assert.Contains("format.h1", ids);
        Assert.Contains("format.bulletList", ids);
        Assert.Contains("insert.codeBlock", ids);
        Assert.True(pane.Commands.Count >= 20, $"expected the full editing catalog, got {pane.Commands.Count}");
    }

    [AvaloniaFact]
    public void Sheet_pane_exposes_cellestras_full_catalog()
    {
        var pane = new SheetPane();
        Assert.NotEmpty(pane.Commands); // the SpreadsheetEditorView self-hosts its AppCommands
    }
}
