using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Plaintain.Desktop.Services;
using Plaintain.Desktop.Views;
using Puffinote.Components.Shell;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// The shell mounts the family's <see cref="DocumentTitleBar"/> so the document's name is centred rather
/// than left to the platform's native title bar. The bar's own layout is covered where the control lives;
/// what matters here is that this shell mounts one and keeps it fed.
/// </summary>
public class DocumentTitleBarTests
{
    /// <summary>A window on a scratch settings file, so the suite never rewrites the profile of whoever ran it.</summary>
    private static MainWindow NewWindow() =>
        new(new SettingsService(Path.Combine(Path.GetTempPath(), $"plaintain-uitests-{Guid.NewGuid():N}.json")));

    [AvaloniaFact]
    public void The_shell_mounts_the_shared_title_bar_and_names_the_document_in_it()
    {
        var window = NewWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var bar = Assert.Single(window.GetVisualDescendants().OfType<DocumentTitleBar>());
        Assert.Equal("Untitled", bar.DocumentName);
        Assert.False(bar.IsDirty);
    }

    [AvaloniaFact]
    public void Opening_a_file_names_it_in_the_title_bar()
    {
        var path = Path.Combine(Path.GetTempPath(), $"plaintain-titlebar-{Guid.NewGuid():N}.log");
        File.WriteAllText(path, "a line of log");
        try
        {
            var window = NewWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.OpenPath(path);
            Dispatcher.UIThread.RunJobs();

            // The file's name, not its path — the title bar names the document, it does not locate it.
            var bar = window.GetVisualDescendants().OfType<DocumentTitleBar>().Single();
            Assert.Equal(Path.GetFileName(path), bar.DocumentName);
            Assert.False(bar.IsDirty);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
