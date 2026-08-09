using Avalonia.Headless.XUnit;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Desktop.Panes;
using Typage.Control.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>Phase 3: each pane exposes an editor catalog for the merged Command Palette.</summary>
public sealed class PaneCommandsTests
{
    [AvaloniaFact]
    public void Markdown_pane_shows_the_formatting_toolbar_by_default_and_carries_all_three_states()
    {
        var pane = new MarkdownPane();
        var view = Assert.IsType<MarkdownEditorView>(pane.View);

        Assert.Equal(CommandBarVisibility.Visible, pane.CommandBarState); // the full bar by default
        Assert.Equal(CommandBarVisibility.Visible, view.CommandBarVisibility);

        // The pane's state is the surface's state — it reads and writes straight through, so each of the
        // three reaches the editor rather than only the two a boolean could express.
        pane.CommandBarState = CommandBarVisibility.Collapsed;
        Assert.Equal(CommandBarVisibility.Collapsed, view.CommandBarVisibility);
        pane.CommandBarState = CommandBarVisibility.Hidden;
        Assert.Equal(CommandBarVisibility.Hidden, view.CommandBarVisibility);
        pane.CommandBarState = CommandBarVisibility.Visible;
        Assert.Equal(CommandBarVisibility.Visible, view.CommandBarVisibility);
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
