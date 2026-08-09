using Avalonia.Headless.XUnit;
using Puffinote.Components.Core.Documents;
using Xunit;

namespace Typage.UiTests;

/// <summary>Crash-recovery snapshots (PRD §9.4): the sidecar roundtrip and the dirty recovery load.</summary>
public class RecoveryTests
{
    private static string TempDir()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"typage-rec-{System.Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Snapshot_RoundTripsAndClears()
    {
        var dir = TempDir();
        try
        {
            var recovery = new RecoveryService("Typage", dir);
            Assert.Null(recovery.TryLoad());

            recovery.Save("/notes/todo.md", "unsaved body");
            var loaded = recovery.TryLoad();
            Assert.NotNull(loaded);
            Assert.Equal("/notes/todo.md", loaded!.Path);
            Assert.Equal("unsaved body", loaded.Text);

            recovery.Clear();
            Assert.Null(recovery.TryLoad());
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Snapshot_KeepsNullPathForUntitled()
    {
        var dir = TempDir();
        try
        {
            var recovery = new RecoveryService("Typage", dir);
            recovery.Save(null, "draft");
            Assert.Null(recovery.TryLoad()!.Path);
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void LoadRecovered_LeavesBufferDirty()
    {
        var view = new Typage.Control.Views.MarkdownEditorView();
        view.LoadRecovered("recovered text");
        Assert.Equal("recovered text", view.Text);
        Assert.True(view.IsDirty); // recovered work is unsaved until the user saves
    }
}
