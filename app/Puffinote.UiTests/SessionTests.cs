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
    public async Task Open_tabs_and_their_order_and_the_active_tab_persist_across_reopening_the_notebook()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-tabs-ui");
        try
        {
            var first = Path.Combine(dir.FullName, "first.md");
            var second = Path.Combine(dir.FullName, "second.md");
            File.WriteAllText(first, "# first");
            File.WriteAllText(second, "# second");

            // Open both notes (first, then second) — second is now the active tab — writing the session file.
            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(first);
            await window.OpenFileAsync(second);
            Assert.Equal([first, second], window.OpenTabPathsForTest);
            Assert.Equal(second, window.ActiveTabPathForTest);

            // Now make the *first* tab the active one, so the restore has a non-default tab to re-focus.
            await window.OpenFileAsync(first);
            Assert.Equal(first, window.ActiveTabPathForTest);

            // A brand-new window over the same notebook reopens both tabs in order, with first still active.
            var reopened = new MainWindow(new NotebookService());
            reopened.Show();
            reopened.OpenNotebook(dir.FullName);
            await reopened.RestoreOpenTabsForTest;

            Assert.Equal([first, second], reopened.OpenTabPathsForTest);
            Assert.Equal(first, reopened.ActiveTabPathForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task A_saved_tab_whose_file_is_gone_is_skipped_on_reopen()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-tabs-stale");
        try
        {
            var live = Path.Combine(dir.FullName, "live.md");
            File.WriteAllText(live, "# live");

            // Seed a session listing one existing file and one that no longer exists.
            SessionStore.Save(dir.FullName, new NotebookSession
            {
                OpenTabs = [Path.Combine(dir.FullName, "deleted.md"), live],
                ActiveTab = live,
            });

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.RestoreOpenTabsForTest;

            Assert.Equal([live], window.OpenTabPathsForTest); // the missing file is silently skipped
            Assert.Equal(live, window.ActiveTabPathForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void Opening_a_notebook_records_it_as_the_last_notebook_and_a_launch_reopens_it()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-last-open");
        var stateFile = Path.Combine(dir.FullName, "app-state.json");
        var prior = AppState.PathOverrideForTest;
        AppState.PathOverrideForTest = stateFile;
        try
        {
            // Opening a notebook remembers it in the per-user app state…
            var first = new MainWindow(new NotebookService());
            first.Show();
            first.OpenNotebook(dir.FullName);
            Assert.Equal(dir.FullName, AppState.Load().LastNotebookPath);

            // …so a fresh launch with no CLI path reopens that same notebook.
            var second = new MainWindow(new NotebookService());
            second.RestoreLastNotebook();
            second.Show();

            Assert.Equal(Path.GetFullPath(dir.FullName), second.OpenNotebookPathForTest);
            Assert.False(second.IsOverlayOpenForTest); // it still exists — no prompt
        }
        finally
        {
            AppState.PathOverrideForTest = prior;
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void A_launch_whose_last_notebook_has_moved_prompts_to_open_one()
    {
        var stateDir = Directory.CreateTempSubdirectory("puffinote-last-gone");
        var missing = Directory.CreateTempSubdirectory("puffinote-notebook-moved");
        var prior = AppState.PathOverrideForTest;
        AppState.PathOverrideForTest = Path.Combine(stateDir.FullName, "app-state.json");
        try
        {
            // Remember a notebook, then delete it out from under the app (moved / trashed).
            new AppState { LastNotebookPath = missing.FullName }.Save();
            missing.Delete(recursive: true);

            var window = new MainWindow(new NotebookService());
            window.RestoreLastNotebook(); // subscribes the prompt to the window's Opened
            window.Show();                // …which fires here

            Assert.Null(window.OpenNotebookPathForTest); // nothing opened
            Assert.True(window.IsOverlayOpenForTest);    // the "notebook moved" modal is up
        }
        finally
        {
            AppState.PathOverrideForTest = prior;
            stateDir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void A_launch_with_no_remembered_notebook_starts_on_the_empty_shell()
    {
        var stateDir = Directory.CreateTempSubdirectory("puffinote-first-run");
        var prior = AppState.PathOverrideForTest;
        AppState.PathOverrideForTest = Path.Combine(stateDir.FullName, "app-state.json");
        try
        {
            var window = new MainWindow(new NotebookService());
            window.RestoreLastNotebook();
            window.Show();

            Assert.Null(window.OpenNotebookPathForTest);
            Assert.False(window.IsOverlayOpenForTest); // nothing to restore, nothing to prompt
        }
        finally
        {
            AppState.PathOverrideForTest = prior;
            stateDir.Delete(recursive: true);
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
                Recent = [Path.Combine(dir.FullName, "deleted.md")],
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
