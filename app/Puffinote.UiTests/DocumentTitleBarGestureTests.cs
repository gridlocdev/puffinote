using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Puffinote.Components.Shell;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The window gestures the family's title bar owes its users. On macOS this strip <b>is</b> the window's
/// title bar (the client area is extended under it), so every click habit people bring from the system's
/// own bar has to land here: drag to move, double-click to zoom, double-click again to go back.
/// </summary>
/// <remarks>
/// The pointer events are constructed rather than driven through <c>window.MouseDown</c> because a click
/// <em>count</em> is the thing under test, and headless's mouse device derives that from the timing and
/// spacing of events it has seen — including ones from whichever test ran before this one. Constructing
/// them states the count outright, so "single click" and "double click" mean the same thing on every
/// machine. The window move itself belongs to the window manager, and headless has none: what a test can
/// hold the bar to is whether it asked for one.
/// </remarks>
public sealed class DocumentTitleBarGestureTests
{
    private readonly Pointer _pointer = new(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);

    /// <summary>A point in the middle of the bar — over the name, where people actually grab it.</summary>
    private static readonly Point Middle = new(400, DocumentTitleBar.BarHeight / 2);

    [AvaloniaFact]
    public void Dragging_the_bar_moves_the_window()
    {
        var ui = Mounted();

        Press(ui, Middle);
        Move(ui, Middle + new Vector(30, 12));

        Assert.Equal(1, ui.Bar.MoveDragsBegunForTest);
    }

    [AvaloniaFact]
    public void The_first_click_of_a_double_click_does_not_run_away_with_the_window()
    {
        var ui = Mounted();

        // The regression this guards: beginning the move on the press hands the mouse to the platform's
        // modal move loop, which swallows the second click — so the double-click below would never zoom.
        Press(ui, Middle);
        Release(ui, Middle);
        Press(ui, Middle, clickCount: 2);

        Assert.Equal(0, ui.Bar.MoveDragsBegunForTest);
        Assert.Equal(WindowState.Maximized, ui.Window.WindowState);
    }

    [AvaloniaFact]
    public void A_press_that_barely_stirs_is_a_click_not_a_drag()
    {
        var ui = Mounted();

        Press(ui, Middle);
        Move(ui, Middle + new Vector(2, 1)); // inside the threshold: the hand of someone clicking
        Release(ui, Middle + new Vector(2, 1));

        Assert.Equal(0, ui.Bar.MoveDragsBegunForTest);
    }

    [AvaloniaFact]
    public void Double_clicking_maximises_the_window()
    {
        var ui = Mounted();

        Press(ui, Middle, clickCount: 2);

        Assert.Equal(WindowState.Maximized, ui.Window.WindowState);
    }

    [AvaloniaFact]
    public void Double_clicking_a_maximised_window_puts_it_back()
    {
        var ui = Mounted();
        ui.Window.WindowState = WindowState.Maximized;

        Press(ui, Middle, clickCount: 2);

        // Normal, not a size of our own choosing: the platform restores the bounds the window had before
        // it was maximised, which is the "back to where it was" half of the gesture.
        Assert.Equal(WindowState.Normal, ui.Window.WindowState);
    }

    [AvaloniaFact]
    public void Clicking_on_and_on_keeps_toggling_the_way_a_native_bar_does()
    {
        var ui = Mounted();

        Press(ui, Middle, clickCount: 2);
        Assert.Equal(WindowState.Maximized, ui.Window.WindowState);

        // Click three of a burst is the start of the next pair, not an action of its own …
        Press(ui, Middle, clickCount: 3);
        Assert.Equal(WindowState.Maximized, ui.Window.WindowState);

        // … and click four completes it.
        Press(ui, Middle, clickCount: 4);
        Assert.Equal(WindowState.Normal, ui.Window.WindowState);
    }

    [AvaloniaFact]
    public void A_window_that_cannot_be_resized_does_not_zoom()
    {
        var ui = Mounted();
        ui.Window.CanResize = false;

        Press(ui, Middle, clickCount: 2);

        // Its zoom button is disabled on every desktop, and its title bar ignores a double-click to match.
        Assert.Equal(WindowState.Normal, ui.Window.WindowState);
    }

    [AvaloniaFact]
    public void A_full_screen_window_is_neither_zoomed_nor_dragged()
    {
        var ui = Mounted();
        ui.Window.WindowState = WindowState.FullScreen;

        Press(ui, Middle);
        Move(ui, Middle + new Vector(30, 12));
        Press(ui, Middle, clickCount: 2);

        // Full screen is the window manager's mode, left through its own affordances — and a full-screen
        // window has nowhere to be moved to.
        Assert.Equal(WindowState.FullScreen, ui.Window.WindowState);
        Assert.Equal(0, ui.Bar.MoveDragsBegunForTest);
    }

    [AvaloniaFact]
    public void The_right_button_is_not_a_window_move()
    {
        var ui = Mounted();

        ui.Bar.RaiseEvent(new PointerPressedEventArgs(
            ui.Bar,
            _pointer,
            ui.Window,
            Middle,
            0,
            new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed),
            KeyModifiers.None));
        Move(ui, Middle + new Vector(30, 12));

        Assert.Equal(0, ui.Bar.MoveDragsBegunForTest);
    }

    private static (Window Window, DocumentTitleBar Bar) Mounted()
    {
        var bar = new DocumentTitleBar { DocumentName = "Budget.ods" };
        var window = new Window { Width = 800, Height = 300, Content = bar };
        window.Show();
        window.UpdateLayout();
        return (window, bar);
    }

    private void Press((Window Window, DocumentTitleBar Bar) ui, Point at, int clickCount = 1) =>
        ui.Bar.RaiseEvent(new PointerPressedEventArgs(
            ui.Bar,
            _pointer,
            ui.Window,
            at,
            0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            clickCount));

    private void Move((Window Window, DocumentTitleBar Bar) ui, Point to) =>
        ui.Bar.RaiseEvent(new PointerEventArgs(
            InputElement.PointerMovedEvent,
            ui.Bar,
            _pointer,
            ui.Window,
            to,
            0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            KeyModifiers.None));

    private void Release((Window Window, DocumentTitleBar Bar) ui, Point at) =>
        ui.Bar.RaiseEvent(new PointerReleasedEventArgs(
            ui.Bar,
            _pointer,
            ui.Window,
            at,
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None,
            MouseButton.Left));
}
