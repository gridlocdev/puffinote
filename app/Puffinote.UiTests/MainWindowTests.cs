using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Services;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Typage.Control.Views;
using Xunit;

namespace Puffinote.UiTests;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public async Task Toggle_editor_toolbar_command_shows_and_hides_the_note_toolbar()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-toolbar");
        try
        {
            var md = Path.Combine(dir.FullName, "note.md");
            File.WriteAllText(md, "# hi");

            var window = new MainWindow(new NotebookService());
            window.Show(); // renders the note toolbar under null drawing
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(md);

            var view = Assert.IsType<MarkdownEditorView>(window.ActiveEditorViewForTest);
            Assert.True(view.ShowCommandBar); // shown by default

            window.InvokeShellForTest("shell.toggleEditorToolbar");
            Assert.False(view.ShowCommandBar);

            window.InvokeShellForTest("shell.toggleEditorToolbar");
            Assert.True(view.ShowCommandBar);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Opening_a_sheet_does_not_warn_about_superseded_palette_or_quick_open_shortcuts()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-conflict");
        try
        {
            var ods = Path.Combine(dir.FullName, "book.ods");
            File.WriteAllBytes(ods, WorkbookSession.NewBlank().SerializeToBytes());

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(ods);

            // Cellestra binds ⌘K (its palette) and ⌘O (open) — both reserved by Puffinote — but the shell
            // supersedes them (merged palette / quick-open), so they're not real conflicts: the sheet opens
            // and no "shortcuts are overridden" warning modal appears.
            Assert.Contains("book.ods", window.Title);
            Assert.False(window.IsOverlayOpenForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void Window_shows_and_opening_a_notebook_updates_the_title()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-uitest");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "notes.md"), "# hello");
            Directory.CreateDirectory(Path.Combine(dir.FullName, "sub"));

            var window = new MainWindow(new NotebookService());
            window.Show(); // exercises the full layout/render pass under null drawing

            Assert.Equal("Puffinote", window.Title);

            window.OpenNotebook(dir.FullName);

            Assert.Equal($"Puffinote — {dir.Name}", window.Title);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
