using System.Globalization;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Cellestra.Core.Editing;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Services;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Something outside Cellestra writes to the open workbook. The document lock already warns about another
/// <em>editor</em> holding the file, but a lock is advisory and most of what rewrites an `.ods` never takes
/// one — a <c>git pull</c>, a sync client, a script. Without this the next save wrote the workbook as it
/// was when it was opened over whatever had appeared.
/// <para>
/// The prompt itself is out of reach of a headless suite (it waits on an answer nobody is there to give),
/// so what is covered here is the decision it delegates to — the part with rules in it.
/// </para>
/// </summary>
public class ExternalChangeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cellestra-external-{Guid.NewGuid():N}");

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

    private MainWindow NewWindow()
    {
        var window = new MainWindow(new SettingsService(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.json")));
        window.Show();
        return window;
    }

    /// <summary>A window over a workbook already on disk, opened through the ordinary open path.</summary>
    private async Task<(MainWindow Window, string Path)> OpenSavedWorkbookAsync()
    {
        var window = NewWindow();
        var path = Path.Combine(_dir, $"book-{Guid.NewGuid():N}.ods");
        File.WriteAllBytes(path, window.Editor.Session.SerializeToBytes());

        await window.OpenPathAsync(path);
        Dispatcher.UIThread.RunJobs();
        return (window, path);
    }

    /// <summary>Rewrite the file the way another tool would, with a stamp that cannot be mistaken for ours.</summary>
    private static void WriteExternally(string path, byte[] bytes)
    {
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));
    }

    private static void TypeInto(MainWindow window, int row, int column, string text)
    {
        IAppEnvironment env = window.Editor;
        env.Session.Execute(EditCommands.SetInput(
            env.Session.ActiveSheet.Name, row, column, text, CultureInfo.InvariantCulture));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Opening_a_workbook_starts_watching_it()
    {
        var (window, path) = await OpenSavedWorkbookAsync();

        Assert.Equal(path, window.WatchedPathForTest);
    }

    /// <summary>A workbook that has never been saved has no file to be changed underneath it.</summary>
    [AvaloniaFact]
    public void A_new_workbook_watches_nothing()
    {
        var window = NewWindow();

        Assert.Null(window.WatchedPathForTest);
        Assert.False(window.NoteExternalChangeForTest()); // must not throw either
    }

    /// <summary>
    /// Unsaved edits are never thrown away to settle a conflict nobody has been asked about. The workbook
    /// is left exactly as it is and the conflict is recorded for the save to raise.
    /// </summary>
    [AvaloniaFact]
    public async Task Unsaved_edits_are_kept_and_the_conflict_is_recorded()
    {
        var (window, path) = await OpenSavedWorkbookAsync();
        TypeInto(window, 0, 0, "mine");

        // Somebody else's version of the same workbook, with a different cell in it.
        var other = NewWindow();
        TypeInto(other, 0, 0, "theirs");
        WriteExternally(path, other.Editor.Session.SerializeToBytes());

        Assert.True(window.NoteExternalChangeForTest());
        Assert.True(window.DiskChangedUnderEditsForTest);
        Assert.Equal("mine", window.Editor.Session.ActiveSheet.GetValue(0, 0).ToString()); // untouched
        Assert.True(window.Editor.Session.IsModified);
    }

    /// <summary>
    /// The reason the flag exists: auto-save must not answer "whose version wins?" on a timer with nobody
    /// watching. The workbook stays dirty and the file keeps the other tool's bytes until a ⌘S.
    /// </summary>
    [AvaloniaFact]
    public async Task Auto_save_stands_down_while_a_conflict_is_unresolved()
    {
        var (window, path) = await OpenSavedWorkbookAsync();
        TypeInto(window, 0, 0, "mine");

        var other = NewWindow();
        TypeInto(other, 0, 0, "theirs");
        var theirBytes = other.Editor.Session.SerializeToBytes();
        WriteExternally(path, theirBytes);
        window.NoteExternalChangeForTest();

        await window.AutoSaveNowForTest();

        Assert.True(window.Editor.Session.IsModified);          // the edits are still unsaved...
        Assert.Equal(theirBytes.Length, new FileInfo(path).Length); // ...and the file is still theirs
    }

    /// <summary>
    /// Our own save moves the file's stamp. Without adopting it, the watcher would report Cellestra's write
    /// back to Cellestra as somebody else's change — and for a workbook, "reload" means a full deserialize
    /// and recalc, on every save.
    /// </summary>
    [AvaloniaFact]
    public async Task Our_own_save_is_not_mistaken_for_somebody_else_s()
    {
        var (window, _) = await OpenSavedWorkbookAsync();
        TypeInto(window, 0, 0, "mine");
        await window.SaveWorkbookAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.NoteExternalChangeForTest());
        Assert.False(window.DiskChangedUnderEditsForTest);
        Assert.Equal("mine", window.Editor.Session.ActiveSheet.GetValue(0, 0).ToString());
    }

    /// <summary>A touch that leaves the bytes alone is not a change.</summary>
    [AvaloniaFact]
    public async Task An_event_with_no_actual_change_does_nothing()
    {
        var (window, _) = await OpenSavedWorkbookAsync();
        TypeInto(window, 0, 0, "mine");

        Assert.False(window.NoteExternalChangeForTest());
        Assert.False(window.DiskChangedUnderEditsForTest);
    }

    /// <summary>
    /// A writer that touches the file repeatedly — a sync client, a script that writes twice — must not
    /// record the conflict again and again; one recorded conflict covers everything that has landed since.
    /// </summary>
    [AvaloniaFact]
    public async Task Repeat_events_do_not_re_record_the_same_conflict()
    {
        var (window, path) = await OpenSavedWorkbookAsync();
        TypeInto(window, 0, 0, "mine");

        var other = NewWindow();
        TypeInto(other, 0, 0, "theirs");
        WriteExternally(path, other.Editor.Session.SerializeToBytes());

        Assert.True(window.NoteExternalChangeForTest());
        Assert.False(window.NoteExternalChangeForTest()); // already recorded; nothing new to say
    }
}
