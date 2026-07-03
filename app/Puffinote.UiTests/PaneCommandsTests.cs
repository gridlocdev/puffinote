using Avalonia.Headless.XUnit;
using Puffinote.Desktop.Panes;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>Phase 3: each pane exposes an editor catalog for the merged Command Palette.</summary>
public sealed class PaneCommandsTests
{
    [AvaloniaFact]
    public void Markdown_pane_exposes_undo_and_redo()
    {
        var pane = new MarkdownPane();
        Assert.Contains(pane.Commands, c => c.Id == "md.undo");
        Assert.Contains(pane.Commands, c => c.Id == "md.redo");
    }

    [AvaloniaFact]
    public void Sheet_pane_exposes_cellestras_full_catalog()
    {
        var pane = new SheetPane();
        Assert.NotEmpty(pane.Commands); // the SpreadsheetEditorView self-hosts its AppCommands
    }
}
