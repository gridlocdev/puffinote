using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The sidebar's pointer affordances (PRD §7.2.1): the header's collapse button, the handle that replaces
/// the sidebar once it's hidden, and the two-finger touchpad swipe.
/// </summary>
public sealed class SidebarToggleTests
{
    /// <summary>Per-event horizontal travel; four of these clear the swipe threshold with room to spare.</summary>
    private const double Notch = 1.5;

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

    /// <summary>
    /// The toggle is one button as far as the eye is concerned, so it must not move when the sidebar it
    /// hides goes away. It used to: the header centred it in a row sized by the notebook's name, while the
    /// handle pinned it to the top under a margin of its own, and the difference showed as a hop on every
    /// collapse. Measured in window coordinates, which is where a hop would be seen.
    /// </summary>
    [AvaloniaFact]
    public void The_toggle_button_stays_put_when_the_sidebar_collapses()
    {
        var window = new MainWindow(new NotebookService()) { Width = 1000, Height = 700 };
        window.Show();
        Settle(window);

        var expandedTop = TopInWindow(window, window.SidebarForTest.CollapseButtonForTest);

        Click(window.SidebarForTest.CollapseButtonForTest);
        Settle(window);
        Assert.True(window.IsSidebarCollapsedForTest);

        var collapsedTop = TopInWindow(window, window.SidebarHandleButtonForTest);
        Assert.Equal(expandedTop, collapsedTop, precision: 3);
    }

    private static double TopInWindow(Window window, Visual control) =>
        control.TranslatePoint(new Point(0, 0), window)!.Value.Y;

    private static void Settle(Window window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_two_finger_swipe_right_collapses_the_sidebar()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();

        SwipeOverEditor(window, Notch);
        Assert.True(window.IsSidebarCollapsedForTest);
    }

    [AvaloniaFact]
    public void A_two_finger_swipe_left_opens_the_sidebar_again()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();
        window.InvokeShellForTest("shell.toggleSidebar");
        Assert.True(window.IsSidebarCollapsedForTest);

        SwipeOverEditor(window, -Notch);
        Assert.False(window.IsSidebarCollapsedForTest);
    }

    /// <summary>A vertical scroll — even a wobbly one with real sideways drift — must never move the sidebar.</summary>
    [AvaloniaFact]
    public void Scrolling_vertically_leaves_the_sidebar_alone()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();

        for (int i = 0; i < 12; i++)
        {
            window.MouseWheel(new Point(600, 400), new Vector(Notch, -3 * Notch));
        }

        Assert.False(window.IsSidebarCollapsedForTest);
    }

    /// <summary>A deliberate two-finger swipe over the editor area, delivered the way a trackpad does.</summary>
    private static void SwipeOverEditor(MainWindow window, double perEvent)
    {
        for (int i = 0; i < 4; i++)
        {
            window.MouseWheel(new Point(600, 400), new Vector(perEvent, 0));
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
