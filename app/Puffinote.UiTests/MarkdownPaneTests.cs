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
    public async Task Opening_a_missing_file_keeps_the_currently_open_document()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-openfail");
        try
        {
            var good = Path.Combine(dir.FullName, "good.md");
            File.WriteAllText(good, "# good");

            var window = new MainWindow(new NotebookService());
            window.Show();
            await window.OpenFileAsync(good);
            Assert.Contains("good.md", window.Title);

            // A .md that doesn't exist → load throws → the good document must stay open.
            await window.OpenFileAsync(Path.Combine(dir.FullName, "missing.md"));
            Assert.Contains("good.md", window.Title);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void Factory_creates_a_markdown_pane_for_md_and_declines_unsupported_types()
    {
        Assert.IsType<MarkdownPane>(EditorPaneFactory.Create("/x/note.MD"));
        Assert.Null(EditorPaneFactory.Create("/x/photo.png")); // no editor for images yet (Phase 3)
        Assert.False(EditorPaneFactory.CanOpen("/x/photo.png"));
    }
}
