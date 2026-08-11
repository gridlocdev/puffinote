using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pivotask.Desktop.Views;
using Puffinote.Components.Shell;
using Xunit;

namespace Pivotask.UiTests;

/// <summary>
/// The shell mounts the family's <see cref="DocumentTitleBar"/> so the board's name is centred rather than
/// left to the platform's native title bar. The bar's own layout is covered where the control lives; what
/// matters here is that this shell mounts one and keeps it fed.
/// </summary>
public class DocumentTitleBarTests
{
    [AvaloniaFact]
    public void The_shell_mounts_the_shared_title_bar_and_names_the_board_in_it()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var bar = Assert.Single(window.GetVisualDescendants().OfType<DocumentTitleBar>());
        Assert.Equal("Untitled board", bar.DocumentName);

        // Mirrors the document rather than a fixed expectation: a never-saved board already reads dirty
        // (BoardDocument compares its serialised text against an empty saved baseline), which is exactly
        // what the window title has always shown. The bar's job is to agree with it, not to second-guess it.
        Assert.Equal(window.BoardView.Document.IsDirty, bar.IsDirty);
    }

    [AvaloniaFact]
    public void Opening_a_board_names_it_in_the_title_bar()
    {
        // A board is a ".pivotask" — the extension is its own, so the title bar shows the name whole.
        var path = Path.Combine(Path.GetTempPath(), $"pivotask-titlebar-{Guid.NewGuid():N}.pivotask");
        File.WriteAllText(path, """{"version":1,"groups":[]}""");
        try
        {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.OpenPath(path);
            Dispatcher.UIThread.RunJobs();

            var bar = window.GetVisualDescendants().OfType<DocumentTitleBar>().Single();
            Assert.Equal(Path.GetFileName(path), bar.DocumentName);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
