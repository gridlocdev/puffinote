using Avalonia.Headless.XUnit;
using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Typage.Control.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>Config inheritance reaches the live editor (PRD §7.5): the resolved settings hit the control.</summary>
public sealed class ConfigInheritanceTests
{
    [AvaloniaFact]
    public void Constructing_a_note_pane_applies_the_resolved_config_to_the_editor()
    {
        var pane = new MarkdownPane(new NoteConfig { TabWidth = 8, EditorPadding = 30 });

        var view = Assert.IsType<MarkdownEditorView>(pane.View);
        Assert.Equal(8, view.Editor.IndentWidth);
        Assert.Equal(30, view.Editor.HorizontalPadding);
    }

    [AvaloniaFact]
    public async Task A_notebooks_committed_tab_width_override_flows_through_to_an_opened_note()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-config-ui");
        try
        {
            // A committed per-notebook override in .puffinote/settings.json (durable, not git-ignored).
            var stateDir = Directory.CreateDirectory(Path.Combine(dir.FullName, NotebookNode.StateDirectoryName));
            File.WriteAllText(Path.Combine(stateDir.FullName, "settings.json"), """{ "TabWidth": 8 }""");
            var note = Path.Combine(dir.FullName, "notes.md");
            File.WriteAllText(note, "# hello");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);   // resolves the notebook override into the tab host
            await window.OpenFileAsync(note); // the note pane inherits it

            var view = Assert.IsType<MarkdownEditorView>(window.ActiveEditorViewForTest);
            Assert.Equal(8, view.Editor.IndentWidth);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
