using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Puffinote.Components.Commands;
using Puffinote.Desktop.Panes;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// An edit made outside Puffinote — a <c>git pull</c>, another app, a synced folder — used to refresh the
/// file tree and the search index and stop there. The open tabs went on showing bytes that no longer
/// existed and would write them back over the new ones on the next save. Proposal 0004 Phase 4 asks for
/// this, and it is not board-specific: every pane type loads from a path and can go stale the same way.
/// </summary>
public sealed class ExternalEditReloadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("puffinote-external").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives the test is not a test failure.
        }
    }

    /// <summary>A pane that really reads its file, so a reload is observable as content rather than a call count.</summary>
    private sealed class ReadingPane : IEditorPane
    {
        public ReadingPane(string path) => Path = path;

        public Control View { get; } = new Border();
        public void Focus() => View.Focus();
        public string Title => System.IO.Path.GetFileName(Path ?? string.Empty);
        public string? Path { get; private set; }
        public string Text { get; private set; } = string.Empty;
        public int Loads { get; private set; }

        public bool IsDirty
        {
            get;
            set
            {
                field = value;
                DirtyChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? DirtyChanged;

        public Exception? LoadThrows { get; init; }

        public Task LoadAsync(string path)
        {
            if (LoadThrows is { } ex)
            {
                throw ex;
            }

            Text = File.ReadAllText(path);
            Path = path;
            Loads++;
            IsDirty = false;
            return Task.CompletedTask;
        }

        public Task SaveAsync()
        {
            File.WriteAllText(Path!, Text);
            IsDirty = false;
            return Task.CompletedTask;
        }

        /// <summary>Stand in for typing: the pane's text diverges from the file and the tab goes dirty.</summary>
        public void Edit(string text)
        {
            Text = text;
            IsDirty = true;
        }

        public void Rebind(string newPath) => Path = newPath;
        public IReadOnlyList<AppCommand> Commands => [];
        public IReadOnlyList<OutlineItem> GetOutline() => [];
        public void Dispose() { }
    }

    private string Write(string name, string text)
    {
        var path = System.IO.Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>Rewrite a file the way something outside Puffinote would, with a stamp that can't collide.</summary>
    private static void WriteExternally(string path, string text)
    {
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));
    }

    private (TabHost Host, ReadingPane Pane, string Path) Open(string text)
    {
        var path = Write("note.md", text);
        var host = new TabHost();
        var pane = new ReadingPane(path);
        host.OpenForTest(pane, path);
        pane.LoadAsync(path).Wait();
        return (host, pane, path);
    }

    [AvaloniaFact]
    public async Task A_clean_tab_reloads_when_the_file_changes_underneath_it()
    {
        var (host, pane, path) = Open("before");
        WriteExternally(path, "after the pull");

        await host.RefreshFromDiskAsync();

        Assert.Equal("after the pull", pane.Text);
        Assert.Empty(host.ExternallyChangedTabs);
    }

    /// <summary>A file nobody touched is not re-read: the pass is a stamp comparison, not a reload-everything.</summary>
    [AvaloniaFact]
    public async Task An_untouched_file_is_not_reloaded()
    {
        var (host, pane, _) = Open("steady");
        int loads = pane.Loads;

        await host.RefreshFromDiskAsync();

        Assert.Equal(loads, pane.Loads);
    }

    /// <summary>
    /// The one case where reloading would be the wrong answer: unsaved edits. Losing them to resolve a
    /// conflict the user hasn't been told about is worse than showing stale content for a moment.
    /// </summary>
    [AvaloniaFact]
    public async Task A_dirty_tab_is_flagged_rather_than_reloaded()
    {
        var (host, pane, path) = Open("before");
        pane.Edit("my unsaved work");
        WriteExternally(path, "somebody else's change");

        await host.RefreshFromDiskAsync();

        Assert.Equal("my unsaved work", pane.Text); // the edits are still there
        var flagged = Assert.Single(host.ExternallyChangedTabs);
        Assert.True(flagged.ExternallyChanged);
    }

    /// <summary>Reloading on request is what resolves the conflict — the edits go, and the flag with them.</summary>
    [AvaloniaFact]
    public async Task Reloading_a_flagged_tab_takes_the_file_and_clears_the_flag()
    {
        var (host, pane, path) = Open("before");
        pane.Edit("my unsaved work");
        WriteExternally(path, "somebody else's change");
        await host.RefreshFromDiskAsync();

        Assert.True(await host.ReloadAsync(host.Tabs[0]));

        Assert.Equal("somebody else's change", pane.Text);
        Assert.Empty(host.ExternallyChangedTabs);
        Assert.False(pane.IsDirty);
    }

    /// <summary>
    /// Our own save moves the baseline. Without that, the write Puffinote just made would come back on the
    /// next watcher tick as somebody else's change and reload the document out from under the caret.
    /// </summary>
    [AvaloniaFact]
    public async Task Our_own_save_is_not_mistaken_for_an_external_change()
    {
        var (host, pane, _) = Open("before");
        pane.Edit("mine");
        await pane.SaveAsync();
        int loads = pane.Loads;

        await host.RefreshFromDiskAsync();

        Assert.Equal(loads, pane.Loads);
        Assert.Empty(host.ExternallyChangedTabs);
    }

    /// <summary>
    /// A file that disappeared is left alone. A delete made through the tree already closes its tabs; a path
    /// that vanished from outside is far more often a rename or an atomic write caught mid-flight than an
    /// instruction to throw the open document away.
    /// </summary>
    [AvaloniaFact]
    public async Task A_vanished_file_leaves_its_tab_open_and_untouched()
    {
        var (host, pane, path) = Open("before");
        File.Delete(path);

        await host.RefreshFromDiskAsync();

        Assert.Single(host.Tabs);
        Assert.Equal("before", pane.Text);
    }

    /// <summary>
    /// A pulled file that will not parse must not take the pass down with it. A board with a syntax error,
    /// a truncated .ods — the panes raise their own format exceptions, not IO ones, and this runs
    /// fire-and-forget from the watcher, so one bad file silently skipped every tab after it and left the
    /// exception unobserved.
    /// </summary>
    [AvaloniaFact]
    public async Task A_file_that_will_not_parse_does_not_stop_the_other_tabs_reloading()
    {
        var host = new TabHost();
        var bad = Write("bad.md", "fine for now");
        var good = Write("good.md", "fine for now");
        var paneA = new ReadingPane(bad) { LoadThrows = new InvalidDataException("not a board") };
        var paneB = new ReadingPane(good);
        host.OpenForTest(paneA, bad);
        host.OpenForTest(paneB, good);
        await paneB.LoadAsync(good);

        WriteExternally(bad, "garbage");
        WriteExternally(good, "pulled");

        await host.RefreshFromDiskAsync(); // must not throw

        Assert.Equal("pulled", paneB.Text); // the tab after the bad one still reloaded
    }

    /// <summary>Every pane type goes through the same pass — the shell knows paths, not document formats.</summary>
    [AvaloniaFact]
    public async Task Every_open_tab_is_reconciled_not_only_the_active_one()
    {
        var host = new TabHost();
        var first = Write("one.md", "one");
        var second = Write("two.md", "two");
        var paneA = new ReadingPane(first);
        var paneB = new ReadingPane(second);
        host.OpenForTest(paneA, first);
        host.OpenForTest(paneB, second);
        await paneA.LoadAsync(first);
        await paneB.LoadAsync(second);

        WriteExternally(first, "one, edited");
        WriteExternally(second, "two, edited");
        await host.RefreshFromDiskAsync();

        Assert.Equal("one, edited", paneA.Text);
        Assert.Equal("two, edited", paneB.Text); // the background tab too, not just the one in front
    }
}
