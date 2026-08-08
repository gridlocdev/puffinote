using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Plaintain.Control.Controls;
using Puffinote.Components.Input;
using Puffinote.Text.Document;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// The caret blinks, at the family's one rate (<see cref="CaretBlink"/>). Plaintain paints its own caret
/// rather than hosting a <c>TextBox</c>, so nothing blinks it for free — and a steady bar in a log full of
/// steady vertical rules is exactly the caret you can't find.
///
/// <para>The blink's phase is stepped directly here rather than waited on: a headless test has no wall
/// clock worth blocking a suite for, and the timer's only job is to call the step these tests call.</para>
/// </summary>
public class CaretBlinkTests
{
    private static (PlainTextEditor Editor, Window Window) Open(string text, int caret = 0)
    {
        var editor = new PlainTextEditor { Text = text };
        var window = new Window { Width = 400, Height = 300, Content = editor };
        window.Show();
        window.UpdateLayout();
        editor.Focus();
        editor.Buffer.Selection = Selection.At(caret);
        return (editor, window);
    }

    [AvaloniaFact]
    public void Focus_starts_the_blink_and_losing_it_stops_it()
    {
        // The regression this whole file is about was a caret that never blinked, and every other assertion
        // here would look identical if the timer had simply never been started — they step the phase by hand.
        // The headless dispatcher won't run a DispatcherTimer, so this asserts the timer is *running* rather
        // than waiting to catch a tick it will never deliver.
        var editor = new PlainTextEditor { Text = "hello" };
        var other = new Button { Content = "elsewhere" };
        var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { editor, other } } };
        window.Show();
        window.UpdateLayout();
        Assert.False(editor.IsCaretBlinkingForTest); // nothing ticking behind an unfocused surface

        editor.Focus();
        Assert.True(editor.IsCaretBlinkingForTest);

        other.Focus();
        Assert.False(editor.IsCaretBlinkingForTest);
    }

    [AvaloniaFact]
    public void Leaving_the_visual_tree_stops_the_blink()
    {
        // Closing a tab detaches a focused surface without ever raising LostFocus; the timer has to stop
        // anyway, or a closed document keeps waking the dispatcher to repaint a caret nobody can see.
        var (editor, window) = Open("hello");
        Assert.True(editor.IsCaretBlinkingForTest);

        window.Content = new Button { Content = "instead" };
        window.UpdateLayout();

        Assert.False(editor.IsCaretBlinkingForTest);
    }

    [AvaloniaFact]
    public void The_caret_goes_dark_and_comes_back()
    {
        var (editor, _) = Open("hello");
        Assert.True(editor.IsCaretLitForTest); // lit the moment it is focused — not on a coin flip

        editor.AdvanceCaretBlinkForTest();
        Assert.False(editor.IsCaretLitForTest);

        editor.AdvanceCaretBlinkForTest();
        Assert.True(editor.IsCaretLitForTest);
    }

    [AvaloniaFact]
    public void Typing_relights_a_dark_caret()
    {
        // The point of restarting the phase on input: a caret that blinked through a keystroke could be
        // dark at the instant the character lands, hiding the one thing the blink exists to point at.
        var (editor, window) = Open("hello world", caret: 5);
        editor.AdvanceCaretBlinkForTest();
        Assert.False(editor.IsCaretLitForTest);

        window.KeyTextInput(",");

        Assert.Equal("hello, world", editor.Text);
        Assert.True(editor.IsCaretLitForTest);
    }

    [AvaloniaFact]
    public void Moving_the_caret_relights_it()
    {
        var (editor, window) = Open("hello", caret: 0);
        editor.AdvanceCaretBlinkForTest();
        Assert.False(editor.IsCaretLitForTest);

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);

        Assert.Equal(1, editor.Buffer.Selection.Caret);
        Assert.True(editor.IsCaretLitForTest);
    }

    [AvaloniaFact]
    public void Losing_focus_leaves_the_caret_lit_for_when_focus_returns()
    {
        // An unfocused surface draws no caret at all, so its blink phase is invisible — but it must not be
        // left dark, or the caret would be missing for up to a full phase after focus came back.
        // Both controls are in the tree from the start: moving the editor would detach it, which stops the
        // blink by a different door and would let this pass without the focus path working at all.
        var editor = new PlainTextEditor { Text = "hello" };
        var other = new Button { Content = "elsewhere" };
        var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { editor, other } } };
        window.Show();
        window.UpdateLayout();
        editor.Focus();

        editor.AdvanceCaretBlinkForTest();
        Assert.False(editor.IsCaretLitForTest);

        other.Focus();

        Assert.False(editor.IsFocused);
        Assert.True(editor.IsCaretLitForTest);
    }

    [AvaloniaFact]
    public void The_app_puts_its_TextBoxes_on_the_same_rate_as_the_surface()
    {
        // Plaintain's find bar and go-to-line field are ordinary TextBoxes, open right over the document.
        var box = new TextBox();
        var window = new Window { Width = 300, Height = 120, Content = box };
        window.Show();
        window.UpdateLayout();

        Assert.Equal(CaretBlink.Interval, box.CaretBlinkInterval);

        // From the app's styles, not from Avalonia's default happening to match today.
        Assert.Equal(BindingPriority.Style, box.GetDiagnostic(TextBox.CaretBlinkIntervalProperty).Priority);
    }
}
