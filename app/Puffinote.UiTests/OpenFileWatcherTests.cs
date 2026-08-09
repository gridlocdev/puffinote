using Avalonia.Headless.XUnit;
using Puffinote.Components.Core.Documents;
using Puffinote.Components.Shell;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The two pieces every app in the family needs to notice somebody else writing to its open document.
/// They live in <c>shared/</c> because four apps were about to grow four copies of the same fiddly
/// filesystem plumbing, and the fiddly parts (watch the <em>directory</em>, filtered to one name; never
/// let a watch failure break editing) are exactly the parts that get copied slightly wrong.
/// </summary>
public sealed class OpenFileWatcherTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("puffinote-watcher").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a test failure.
        }
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [AvaloniaFact]
    public void A_stamp_moves_when_the_file_is_rewritten()
    {
        var path = Write("note.md", "before");
        var first = FileStamp.For(path);

        File.WriteAllText(path, "after, and longer");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));

        Assert.NotNull(first);
        Assert.NotEqual(first, FileStamp.For(path));
    }

    [AvaloniaFact]
    public void A_stamp_is_null_for_a_file_that_is_not_there()
    {
        Assert.Null(FileStamp.For(Path.Combine(_dir, "never-written.md")));
        Assert.Null(FileStamp.For(null));
        Assert.Null(FileStamp.For(string.Empty));
    }

    /// <summary>
    /// Watching an untitled buffer means watching nothing — not watching the working directory, and not
    /// throwing on a null path either.
    /// </summary>
    [AvaloniaFact]
    public void Watching_nothing_is_allowed_and_watches_nothing()
    {
        int fired = 0;
        using var watcher = new OpenFileWatcher(() => fired++);

        watcher.Watch(null);

        Assert.Null(watcher.Path);
        Assert.Equal(0, fired);
    }

    [AvaloniaFact]
    public void Watching_a_file_reports_a_change_on_the_ui_thread()
    {
        var path = Write("note.md", "before");
        int fired = 0;
        using var watcher = new OpenFileWatcher(() => fired++);

        watcher.Watch(path);
        Assert.Equal(path, watcher.Path);

        watcher.RaiseForTest();
        Assert.Equal(1, fired);
    }

    /// <summary>
    /// A filesystem event already in flight must not reach a window that has been torn down — the check
    /// is on the UI thread, where Dispose runs, so there is no window between the two.
    /// </summary>
    [AvaloniaFact]
    public void A_disposed_watcher_reports_nothing()
    {
        var path = Write("note.md", "before");
        int fired = 0;
        var watcher = new OpenFileWatcher(() => fired++);
        watcher.Watch(path);

        watcher.Dispose();
        watcher.RaiseForTest();

        Assert.Equal(0, fired);
    }

    /// <summary>An unwatchable path costs the app this feature and nothing else.</summary>
    [AvaloniaFact]
    public void A_path_that_cannot_be_watched_does_not_throw()
    {
        using var watcher = new OpenFileWatcher(() => { });

        watcher.Watch(Path.Combine(_dir, "no-such-folder", "note.md"));

        Assert.Equal(Path.Combine(_dir, "no-such-folder", "note.md"), watcher.Path);
    }
}
