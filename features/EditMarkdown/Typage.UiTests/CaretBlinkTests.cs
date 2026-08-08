using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Puffinote.Components.Input;
using Puffinote.Text.Document;
using Typage.Control.Controls;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The caret blinks, at the family's one rate (<see cref="CaretBlink"/>). Typage paints its own caret
/// rather than hosting a <c>TextBox</c>, so nothing blinks it for free — and a steady bar in a live-preview
/// document full of rules, table borders and quote bars is a caret you have to hunt for.
///
/// <para>The blink's phase is stepped directly here rather than waited on: a headless test has no wall
/// clock worth blocking a suite for, and the timer's only job is to call the step these tests call.</para>
/// </summary>
public class CaretBlinkTests
{
    private static (MarkdownEditor Editor, Window Window) Open(string text, int caret = 0)
    {
        var editor = new MarkdownEditor { Text = text };
        var window = new Window { Width = 600, Height = 400, Content = editor };
        window.Show();
        window.UpdateLayout();
        editor.Focus();
        editor.Buffer.Selection = Selection.At(caret);
        return (editor, window);
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
        var editor = new MarkdownEditor { Text = "hello" };
        var other = new Button { Content = "elsewhere" };
        var window = new Window { Width = 600, Height = 400, Content = new StackPanel { Children = { editor, other } } };
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
        // Typage's find bar and link dialog are ordinary TextBoxes. They sit inches from the editing
        // surface, so a different blink rate there is visible as a stutter between the two.
        var box = new TextBox();
        var window = new Window { Width = 300, Height = 120, Content = box };
        window.Show();
        window.UpdateLayout();

        Assert.Equal(CaretBlink.Interval, box.CaretBlinkInterval);

        // From the app's styles, not from Avalonia's default happening to match today.
        Assert.Equal(BindingPriority.Style, box.GetDiagnostic(TextBox.CaretBlinkIntervalProperty).Priority);
    }
}
