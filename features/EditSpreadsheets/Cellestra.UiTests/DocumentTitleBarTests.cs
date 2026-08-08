using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cellestra.Desktop.Views;
using Puffinote.Components.Shell;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// PRD §6.1's title bar, finally the app's own rather than the system's: the file name centred in a strip
/// unified with the window, carrying the "edited" dot and the brief "Saved" badge. The bar's layout is
/// covered where the shared control lives; what matters here is that this window mounts one and keeps it
/// saying the same things <see cref="MainWindow.ComposeTitle"/> puts in the window title.
/// </summary>
public class DocumentTitleBarTests
{
    private static (MainWindow Window, DocumentTitleBar Bar) Open()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, window.GetVisualDescendants().OfType<DocumentTitleBar>().Single());
    }

    [AvaloniaFact]
    public void The_window_mounts_the_shared_title_bar_and_names_the_document_in_it()
    {
        var (window, bar) = Open();

        Assert.Equal("Untitled", bar.DocumentName);

        // This app's title has never carried an app-name suffix (#15/#16), so the two agree exactly here.
        Assert.Equal(window.Title, bar.DocumentName);
    }

    [AvaloniaFact]
    public void Saving_flashes_the_badge_in_the_bar_and_it_clears_with_the_title()
    {
        var (window, bar) = Open();

        window.Editor.MarkSavedNow(); // what every save path ends with
        Dispatcher.UIThread.RunJobs();

        Assert.EndsWith("— Saved", window.Title, StringComparison.Ordinal);
        Assert.Equal("Saved", bar.Badge);

        window.ExpireSavedBadgeForTest();

        Assert.DoesNotContain("Saved", window.Title, StringComparison.Ordinal);
        Assert.Null(bar.Badge);
    }
}
