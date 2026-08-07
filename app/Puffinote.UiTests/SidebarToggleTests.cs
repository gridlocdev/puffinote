using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The sidebar's pointer affordances (PRD §7.2.1): the header's collapse button and the handle that
/// replaces the sidebar once it's hidden.
/// </summary>
public sealed class SidebarToggleTests
{
    [AvaloniaFact]
    public void The_header_button_hides_the_sidebar_and_the_handle_brings_it_back()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();

        Assert.False(window.IsSidebarCollapsedForTest);
        Assert.False(window.IsSidebarHandleVisibleForTest);

        // The affordance a user actually clicks, not the command behind it.
        Click(window.SidebarForTest.CollapseButtonForTest);
        Assert.True(window.IsSidebarCollapsedForTest);

        // With the sidebar gone, its header button went with it — the handle is the way back.
        Assert.True(window.IsSidebarHandleVisibleForTest);
        Click(window.SidebarHandleButtonForTest);
        Assert.False(window.IsSidebarCollapsedForTest);
        Assert.False(window.IsSidebarHandleVisibleForTest);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
