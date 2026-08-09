using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Puffinote.Desktop.Sidebar;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The mouse map for the sidebar's list panels — Outline, Recent, Search. One click goes: every row in
/// them is a destination and nothing else, so a second click only ever delayed the trip. This is the same
/// rule the file tree already follows (<see cref="FileTreeClickTests"/>), and these tests exist because the
/// three panels share one control: a change here silently changes all three.
/// </summary>
public sealed class SidebarListClickTests
{
    private static (KeyboardListView List, Window Window) Open(params string[] rows)
    {
        var list = new KeyboardListView(new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text }, supportsRecycling: true));
        var window = new Window { Width = 300, Height = 400, Content = list };
        window.Show();
        list.SetItems(rows);
        Settle(window);
        return (list, window);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static ListBoxItem Row(Window window, string text) =>
        window.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Single(i => i.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));

    private static void Click(Window window, Visual target, int clicks = 1)
    {
        var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        for (var i = 0; i < clicks; i++)
        {
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
        }

        Settle(window);
    }

    [AvaloniaFact]
    public void A_single_click_activates_the_row_it_landed_on()
    {
        var (list, window) = Open("first", "second", "third");
        var activated = new List<object>();
        list.Activated += item => activated.Add(item);

        Click(window, Row(window, "second"));

        Assert.Equal(["second"], activated);        // one click, not two
        Assert.Equal("second", list.SelectedItem);  // and it selected the row it navigated to
    }

    [AvaloniaFact]
    public void A_double_click_still_activates_only_once()
    {
        // Habit will produce plenty of double-clicks. The second click of a pair raises DoubleTapped rather
        // than Tapped, and nothing listens for it — so the row opens once instead of twice.
        var (list, window) = Open("first", "second");
        var opens = 0;
        list.Activated += _ => opens++;

        Click(window, Row(window, "first"), clicks: 2);

        Assert.Equal(1, opens);
    }

    [AvaloniaFact]
    public void A_click_below_the_last_row_activates_nothing()
    {
        // SetItems selects the first row, so a tap that reaches the ListBox but not a row would otherwise
        // navigate somewhere on a click that landed on nothing.
        var (list, window) = Open("first", "second");
        var opens = 0;
        list.Activated += _ => opens++;

        var lastRow = Row(window, "second");
        var below = lastRow.TranslatePoint(new Point(lastRow.Bounds.Width / 2, lastRow.Bounds.Height + 60), window)!.Value;
        window.MouseDown(below, MouseButton.Left);
        window.MouseUp(below, MouseButton.Left);
        Settle(window);

        Assert.Equal(0, opens);
    }

    [AvaloniaFact]
    public void Enter_still_activates_the_keyboard_selection()
    {
        // The keyboard keeps the two steps separate — move without going, then go — which is what a
        // one-click list gives up. It must keep working.
        var (list, window) = Open("first", "second", "third");
        object? activated = null;
        list.Activated += item => activated = item;

        list.FocusList();
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Null(activated); // moved, didn't go

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal("second", activated);
    }
}
