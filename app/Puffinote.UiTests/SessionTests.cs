using Avalonia.Headless.XUnit;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

public sealed class SessionTests
{
    [AvaloniaFact]
    public async Task Recent_files_persist_across_reopening_the_notebook()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-session-ui");
        try
        {
            var note = Path.Combine(dir.FullName, "notes.md");
            File.WriteAllText(note, "# hello");

            // Open the notebook, open a note — recording it in the recent list writes the session file.
            var first = new MainWindow(new NotebookService());
            first.Show();
            first.OpenNotebook(dir.FullName);
            await first.OpenFileAsync(note);
            Assert.Single(first.RecentForTest);

            // A brand-new window over the same notebook restores the recent list from .puffinote/session.json.
            var second = new MainWindow(new NotebookService());
            second.Show();
            second.OpenNotebook(dir.FullName);

            Assert.Single(second.RecentForTest);
            Assert.Equal(Path.GetFullPath(note), second.RecentForTest[0]);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void A_recent_entry_whose_file_is_gone_is_dropped_on_reopen()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-session-stale");
        try
        {
            // Seed a session that references a file which no longer exists in the notebook.
            SessionStore.Save(dir.FullName, new NotebookSession
            {
                Recent = new[] { Path.Combine(dir.FullName, "deleted.md") },
            });

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);

            Assert.Empty(window.RecentForTest); // the stale path is filtered out on load
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
