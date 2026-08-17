using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Puffinote.Desktop;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// How a path reaches the shell from outside it: the command line, and — the case this covers — the
/// desktop handing the app something while it runs. Finder's "Open With" is the second kind on macOS, not
/// the first: a bundled app is launched with an Apple Event and an empty argv, so a claim wired only to
/// <c>args[0]</c> restores the remembered notebook and drops what was actually asked for.
///
/// <para>The shell's document is a <em>notebook</em>, which is why a folder and a file both count here
/// while the four editors take only files: a file names one document inside a notebook, and answering it
/// means opening both.</para>
/// </summary>
public class OpenFromDesktopTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("puffinote-openwith");
    private readonly string? _priorState = AppState.PathOverrideForTest;

    public OpenFromDesktopTests() =>
        // Never the developer's own app-state file: opening a notebook here would rewrite what their next
        // real launch restores.
        AppState.PathOverrideForTest = Path.Combine(_dir.FullName, "app-state.json");

    public void Dispose()
    {
        AppState.PathOverrideForTest = _priorState;
        try
        {
            _dir.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }

        GC.SuppressFinalize(this);
    }

    private static MainWindow Open()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();
        return window;
    }

    private string Notebook(string name)
    {
        var path = Path.Combine(_dir.FullName, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Note(string notebook, string name, string text)
    {
        var path = Path.Combine(notebook, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>
    /// Run the arrival to completion, failing rather than deadlocking if it never resolves. Its
    /// continuations need the UI thread, so blocking on one that is still waiting on a dialog takes down
    /// the whole run instead of failing this one case.
    /// </summary>
    private static void Settle(Task work)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.True(work.IsCompleted, "the open never finished — something is still waiting on a dialog");
        work.GetAwaiter().GetResult(); // surface an exception rather than swallowing it
    }

    // ---- The arrival ------------------------------------------------------

    [AvaloniaFact]
    public void A_folder_is_opened_as_the_notebook()
    {
        var notebook = Notebook("work");
        var window = Open();

        Settle(window.OpenFromDesktopAsync(notebook));

        Assert.Equal(Path.GetFullPath(notebook), window.OpenNotebookPathForTest);
    }

    /// <summary>
    /// The bug, in the shell's shape: a note handed over used to leave the window on whatever notebook it
    /// had restored, with the note itself dropped. It should open the notebook that holds it, and the note.
    /// </summary>
    [AvaloniaFact]
    public void A_file_opens_the_notebook_that_holds_it_and_the_document()
    {
        var notebook = Notebook("work");
        var note = Note(notebook, "todo.md", "# Todo\n");
        var window = Open();

        Settle(window.OpenFromDesktopAsync(note));

        Assert.Equal(Path.GetFullPath(notebook), window.OpenNotebookPathForTest);
        Assert.Contains(Path.GetFullPath(note), window.OpenTabPathsForTest, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A note inside the notebook already open opens as a tab and nothing else moves. Switching notebooks
    /// under someone because they double-clicked one file would be a far bigger answer than the question.
    /// </summary>
    [AvaloniaFact]
    public void A_file_inside_the_open_notebook_does_not_change_the_notebook()
    {
        var notebook = Notebook("work");
        var folder = Path.Combine(notebook, "chapters");
        Directory.CreateDirectory(folder);
        var note = Note(folder, "one.md", "# One\n");

        var window = Open();
        window.OpenNotebook(notebook);
        Dispatcher.UIThread.RunJobs();

        Settle(window.OpenFromDesktopAsync(note));

        // The notebook is still the root, not the note's own "chapters" folder.
        Assert.Equal(Path.GetFullPath(notebook), window.OpenNotebookPathForTest);
        Assert.Contains(Path.GetFullPath(note), window.OpenTabPathsForTest, StringComparer.OrdinalIgnoreCase);
    }

    // ---- Which candidate counts -------------------------------------------

    [AvaloniaFact]
    public void The_first_candidate_is_the_one_opened()
    {
        var first = Notebook("first");
        var second = Notebook("second");

        Assert.Equal(first, App.PathToOpen([first, second]));
    }

    [AvaloniaFact]
    public void A_folder_counts_here_even_though_it_never_does_for_an_editor()
    {
        var notebook = Notebook("work");
        var note = Note(notebook, "todo.md", "# Todo\n");

        Assert.Equal(notebook, App.PathToOpen([notebook]));
        Assert.Equal(note, App.PathToOpen([note]));
    }

    [AvaloniaFact]
    public void Nothing_to_open_falls_back_to_the_remembered_notebook()
    {
        Assert.Null(App.PathToOpen(null));                                       // no argv at all
        Assert.Null(App.PathToOpen([]));
        Assert.Null(App.PathToOpen([null]));                                     // no local path
        Assert.Null(App.PathToOpen([Path.Combine(_dir.FullName, "gone")]));      // a stale path
    }
}
