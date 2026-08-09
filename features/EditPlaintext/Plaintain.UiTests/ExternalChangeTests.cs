using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Plaintain.Desktop.Services;
using Plaintain.Desktop.Views;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// Something outside Plaintain writes to the open file. A plaintext editor is the app in this family most
/// likely to have a file open that something else is actively writing — a log, a config a tool regenerates,
/// a file that arrives with a <c>git pull</c> — and it was the one that never looked, so its next save
/// wrote the bytes from before over whatever had appeared.
/// </summary>
public class ExternalChangeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"plaintain-external-{Guid.NewGuid():N}");

    public ExternalChangeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }

        GC.SuppressFinalize(this);
    }

    private MainWindow Open()
    {
        var settings = new SettingsService(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.json"));
        var window = TestShell.NewWindow(settings);
        window.Show();
        return window;
    }

    private string WriteFile(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>Write the way another program would, with a stamp that cannot be mistaken for ours.</summary>
    private static void WriteExternally(string path, string text)
    {
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));
    }

    private static void SetText(MainWindow window, string text)
    {
        var buffer = window.EditorView.Editor.Buffer;
        buffer.ReplaceRange(0, buffer.Document.Length, text);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Opening_a_file_starts_watching_it()
    {
        var path = WriteFile("server.log", "line one\n");
        var window = Open();

        window.OpenPath(path);

        Assert.Equal(path, window.WatchedPathForTest);
    }

    /// <summary>With nothing unsaved, showing what is actually in the file is the only useful behaviour.</summary>
    [AvaloniaFact]
    public void A_clean_document_picks_up_the_new_bytes()
    {
        var path = WriteFile("server.log", "line one\n");
        var window = Open();
        window.OpenPath(path);

        WriteExternally(path, "line one\nline two\n");
        window.NoteExternalChangeForTest();

        Assert.Equal("line one\nline two\n", window.EditorView.Text);
        Assert.False(window.EditorView.IsDirty);
        Assert.False(window.DiskChangedUnderEditsForTest);
    }

    /// <summary>
    /// Unsaved edits are never thrown away to resolve a conflict the user has not been asked about. The
    /// buffer is left exactly as it is and the conflict is recorded for the save to raise.
    /// </summary>
    [AvaloniaFact]
    public void Unsaved_edits_are_kept_and_the_conflict_is_recorded()
    {
        var path = WriteFile("notes.txt", "original\n");
        var window = Open();
        window.OpenPath(path);
        SetText(window, "my unsaved work");

        WriteExternally(path, "somebody else's version\n");
        bool wantsPrompt = window.NoteExternalChangeForTest();
        Dispatcher.UIThread.RunJobs();

        Assert.True(wantsPrompt);                                  // the user is about to be asked
        Assert.Equal("my unsaved work", window.EditorView.Text);   // and nothing was decided for them
        Assert.True(window.DiskChangedUnderEditsForTest);
    }

    /// <summary>
    /// The reason the flag exists: auto-save must not answer "whose version wins?" on a timer with nobody
    /// watching. The document stays dirty and the file on disk keeps the other tool's bytes until a ⌘S.
    /// </summary>
    [AvaloniaFact]
    public void Auto_save_stands_down_while_a_conflict_is_unresolved()
    {
        var path = WriteFile("notes.txt", "original\n");
        var window = Open();
        window.OpenPath(path);
        SetText(window, "my unsaved work");
        WriteExternally(path, "somebody else's version\n");
        window.NoteExternalChangeForTest();
        Dispatcher.UIThread.RunJobs();

        window.AutoSaveNowForTest().GetAwaiter().GetResult();

        Assert.Equal("somebody else's version\n", File.ReadAllText(path)); // not overwritten
        Assert.True(window.EditorView.IsDirty);                            // and the edits are still here
    }

    /// <summary>
    /// Our own save moves the file's stamp. Without adopting it, the watcher would report Plaintain's write
    /// back to Plaintain as somebody else's change and reload the document out from under the caret.
    /// </summary>
    [AvaloniaFact]
    public void Our_own_save_is_not_mistaken_for_somebody_else_s()
    {
        var path = WriteFile("notes.txt", "original\n");
        var window = Open();
        window.OpenPath(path);
        SetText(window, "mine");
        window.SaveForTest(path);

        window.NoteExternalChangeForTest();

        Assert.Equal("mine", window.EditorView.Text);
        Assert.False(window.DiskChangedUnderEditsForTest);
    }

    /// <summary>A touch that leaves the bytes alone is not a change; nothing reloads and nothing is asked.</summary>
    [AvaloniaFact]
    public void An_event_with_no_actual_change_does_nothing()
    {
        var path = WriteFile("notes.txt", "original\n");
        var window = Open();
        window.OpenPath(path);
        SetText(window, "my unsaved work");

        Assert.False(window.NoteExternalChangeForTest()); // nothing to ask about

        Assert.Equal("my unsaved work", window.EditorView.Text);
        Assert.False(window.DiskChangedUnderEditsForTest);
    }

    /// <summary>An untitled buffer has no file to be changed underneath it, and watches nothing.</summary>
    [AvaloniaFact]
    public void An_untitled_buffer_watches_nothing()
    {
        var window = Open();

        Assert.Null(window.WatchedPathForTest);
        window.NoteExternalChangeForTest(); // must not throw
    }
}
