using Avalonia.Headless.XUnit;
using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Typage.Control.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>Phase 1's vertical slice: open → edit → save one .md through the embedded Typage editor.</summary>
public sealed class MarkdownPaneTests
{
    [AvaloniaFact]
    public async Task Open_edit_save_round_trips_through_the_embedded_editor()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-pane");
        try
        {
            var path = Path.Combine(dir.FullName, "note.md");
            File.WriteAllText(path, "# Hello");

            var pane = EditorPaneFactory.Create(path);
            Assert.NotNull(pane);

            await pane!.LoadAsync(path);
            Assert.False(pane.IsDirty);           // a freshly opened doc is clean
            Assert.Equal("note.md", pane.Title);

            // Simulate unsaved edits in the embedded Typage surface. (LoadRecovered loads text left
            // marked dirty — the crash-recovery path — which is the editor's public way to reach a dirty
            // state without driving focus/keystrokes headlessly. The setter `Text` resets the baseline.)
            var editor = Assert.IsType<MarkdownEditorView>(pane.View);
            editor.LoadRecovered("# Hello\n\nEdited in Puffinote.");
            Assert.True(pane.IsDirty);

            await pane.SaveAsync();

            Assert.False(pane.IsDirty);                             // save clears dirty (MarkSaved)
            Assert.Equal(editor.Text, File.ReadAllText(path));       // editor text round-tripped to disk
            Assert.Contains("Edited in Puffinote.", File.ReadAllText(path));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Opening_a_file_from_the_shell_mounts_a_pane_and_titles_it()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-open");
        try
        {
            var path = Path.Combine(dir.FullName, "readme.md");
            File.WriteAllText(path, "hi");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);

            await window.OpenFileAsync(path);

            Assert.Contains("readme.md", window.Title);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void Factory_declines_non_markdown_files()
    {
        Assert.Null(EditorPaneFactory.Create("/x/budget.ods"));
        Assert.False(EditorPaneFactory.CanOpen("/x/photo.png"));
        Assert.True(EditorPaneFactory.CanOpen("/x/note.MD"));
    }
}
