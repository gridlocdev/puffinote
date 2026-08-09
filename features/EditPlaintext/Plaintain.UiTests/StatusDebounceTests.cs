using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Plaintain.Desktop.Services;
using Plaintain.Desktop.Views;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// The status bar's counts scan the whole document, and <c>Stats</c> caches its answer only until the
/// next change — so asking for them on every keystroke meant a full pass over the file per keystroke.
/// Measured on the benchmark harness at <b>494 ms and 392 MB for one character typed into a 50 MB log</b>,
/// against 23 ms for the edit itself. Typing was 21× the cost of the edit, in the editor whose entire
/// design brief is files that size.
/// </summary>
public class StatusDebounceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"plaintain-status-{Guid.NewGuid():N}");

    public StatusDebounceTests() => Directory.CreateDirectory(_dir);

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
        var window = TestShell.NewWindow(new SettingsService(Path.Combine(_dir, $"s-{Guid.NewGuid():N}.json")));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>
    /// The caret's position is cheap and must stay immediate — it is the part of the status bar someone is
    /// actually watching while they type.
    /// </summary>
    [AvaloniaFact]
    public void The_caret_position_still_updates_on_every_keystroke()
    {
        var window = Open();
        var buffer = window.EditorView.Editor.Buffer;

        buffer.Insert("hello");
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("Col 6", window.StatusPositionForTest, StringComparison.Ordinal);
    }

    /// <summary>
    /// The counts settle once the typing pauses. Asserting through the flush rather than by waiting keeps
    /// the test honest about what it is testing: that the scan happens, not when a timer fires.
    /// </summary>
    [AvaloniaFact]
    public void The_counts_settle_after_the_typing_pauses()
    {
        var window = Open();
        var buffer = window.EditorView.Editor.Buffer;

        buffer.Insert("one\ntwo\nthree");
        Dispatcher.UIThread.RunJobs();
        window.FlushStatusForTest();

        Assert.Contains("3 lines", window.StatusCountsForTest, StringComparison.Ordinal);
        Assert.Contains("13 chars", window.StatusCountsForTest, StringComparison.Ordinal);
    }

    /// <summary>
    /// Opening a file is not typing: there is no burst to wait out, and the counts must be right the moment
    /// the document appears rather than a fifth of a second later.
    /// </summary>
    [AvaloniaFact]
    public void Opening_a_file_reports_its_counts_immediately()
    {
        var path = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(path, "one\ntwo\n");
        var window = Open();

        window.OpenPath(path);

        Assert.Contains("lines", window.StatusCountsForTest, StringComparison.Ordinal);
        Assert.Contains("8 chars", window.StatusCountsForTest, StringComparison.Ordinal);
    }
}
