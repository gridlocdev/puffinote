using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Sidebar;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// Which panels take the keyboard when you switch to them. The two that are typed into — Search and Filter —
/// put the caret in their box, because arriving at one and then having to click the field is a click with no
/// other purpose and the panel does nothing until it happens. The three you read do not, because activating
/// Files is something the shell also does on the user's behalf, and taking focus out of the editor for that
/// would land the next keystroke in the sidebar instead of in the sentence being written.
/// </summary>
public sealed class SidebarPanelFocusTests : IDisposable
{
    private readonly DirectoryInfo _notebook = Directory.CreateTempSubdirectory("puffinote-panel-focus");

    public void Dispose() => _notebook.Delete(recursive: true);

    private (SidebarView Sidebar, Window Window) Open()
    {
        File.WriteAllText(System.IO.Path.Combine(_notebook.FullName, "Inbox.md"), "");

        var sidebar = new SidebarView();
        var window = new Window { Width = 320, Height = 500, Content = sidebar };
        window.Show();
        sidebar.SetRoot(new NotebookNode(_notebook.FullName, isDirectory: true));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (sidebar, window);
    }

    private static TextBox? FocusedBox(Window window) =>
        window.FocusManager?.GetFocusedElement() as TextBox;

    [AvaloniaTheory]
    [InlineData("search", "Search the notebook…")]
    [InlineData("filter", "Filter by name…")]
    public void Switching_to_a_panel_you_type_into_puts_the_caret_in_its_box(string id, string placeholder)
    {
        var (sidebar, window) = Open();

        sidebar.Activate(id);
        Dispatcher.UIThread.RunJobs();

        var box = FocusedBox(window);
        Assert.NotNull(box);
        Assert.Equal(placeholder, box.PlaceholderText);
    }

    [AvaloniaTheory]
    [InlineData("outline")]
    [InlineData("recent")]
    public void Switching_to_a_panel_you_read_leaves_the_keyboard_alone(string id)
    {
        var (sidebar, window) = Open();

        sidebar.Activate(id);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(FocusedBox(window));
    }

    [AvaloniaFact]
    public void Revealing_a_document_activates_Files_without_stealing_focus()
    {
        // The shell calls this when the active tab changes — while the user is typing in the editor. A
        // sidebar that grabbed the keyboard here would eat the next keystroke out of their document.
        var (sidebar, window) = Open();
        sidebar.Activate("filter");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(FocusedBox(window)); // the filter box has it to begin with

        sidebar.RevealInTree(System.IO.Path.Combine(_notebook.FullName, "Inbox.md"));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("files", sidebar.ActivePanelId);
        Assert.Null(FocusedBox(window));
    }

    [AvaloniaFact]
    public void Pressing_the_same_panel_again_returns_the_caret_to_its_box()
    {
        // Click into the results, then reach for the same switcher button to type a new query — the one
        // gesture that means "back to the box" must not be the one that does nothing.
        var (sidebar, window) = Open();
        sidebar.Activate("filter");
        Dispatcher.UIThread.RunJobs();

        sidebar.CollapseButtonForTest.Focus(); // the keyboard leaves the box, the panel stays put
        Dispatcher.UIThread.RunJobs();
        Assert.Null(FocusedBox(window));
        Assert.Equal("filter", sidebar.ActivePanelId);

        sidebar.Activate("filter");
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(FocusedBox(window));
    }
}
