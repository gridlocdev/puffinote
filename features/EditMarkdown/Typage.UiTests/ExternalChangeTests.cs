using Typage.Desktop.Services;
using Typage.Desktop.Views;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The external-change classifier (PRD §7.1): the disk baseline recorded on open/save lets the
/// shell tell its own atomic saves apart from a real external edit, and route a real edit to a
/// silent reload (clean) or a three-way conflict prompt (dirty). The watcher and dialog can't run
/// headlessly; this covers the decision they delegate to.
/// </summary>
public class ExternalChangeTests
{
    private static string WriteTemp(string content)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"typage-{System.Guid.NewGuid():N}.md");
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    [AvaloniaFact]
    public void SameTextAsDisk_IsNotAChange()
    {
        var path = WriteTemp("hello");
        try
        {
            var window = new MainWindow(new SettingsService());
            window.OpenPath(path);
            Assert.Equal(MainWindow.DiskChange.None, window.ClassifyDiskChange("hello"));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void DifferentText_WhenClean_ReloadsSilently()
    {
        var path = WriteTemp("hello");
        try
        {
            var window = new MainWindow(new SettingsService());
            window.OpenPath(path);
            Assert.False(window.EditorView.IsDirty);
            Assert.Equal(MainWindow.DiskChange.ReloadClean, window.ClassifyDiskChange("changed on disk"));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void DifferentText_WhenDirty_IsAConflict()
    {
        var path = WriteTemp("hello");
        try
        {
            var window = new MainWindow(new SettingsService());
            window.OpenPath(path);
            window.EditorView.Editor.Buffer.Insert(" local edit"); // make the buffer dirty
            Assert.True(window.EditorView.IsDirty);
            Assert.Equal(MainWindow.DiskChange.Conflict, window.ClassifyDiskChange("changed on disk"));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}
