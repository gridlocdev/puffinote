using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Puffinote.Components.Shell;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The shell mounts the family's <see cref="DocumentTitleBar"/> so the document's name is centred rather
/// than left to the platform's native title bar. The bar's own layout is covered where the control lives;
/// what matters here is that this shell mounts one and keeps it fed — and that it carries the <em>document's
/// name alone</em>, while <see cref="Avalonia.Controls.Window.Title"/> keeps the app-name suffix the Window
/// menu and app switcher need.
/// </summary>
public class DocumentTitleBarTests
{
    [AvaloniaFact]
    public void The_shell_mounts_the_shared_title_bar_and_names_the_document_in_it()
    {
        var window = TestShell.NewWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var bar = Assert.Single(window.GetVisualDescendants().OfType<DocumentTitleBar>());
        Assert.Equal("Untitled", bar.DocumentName);

        // The window title says which app it is; the title bar says which document, and nothing else.
        Assert.Contains("Typage", window.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("Typage", bar.DocumentName, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void The_title_bar_shows_the_edited_dot_once_the_document_is_dirty()
    {
        var window = TestShell.NewWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var bar = window.GetVisualDescendants().OfType<DocumentTitleBar>().Single();
        Assert.False(bar.IsDirty);

        window.EditorView.Editor.Buffer.Insert("an unsaved edit");
        Dispatcher.UIThread.RunJobs();

        Assert.True(bar.IsDirty);
    }
}
