using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Puffinote.Components.Overlays;
using Typage.Control.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The link prompt opens with the caret already in its first field — the display text — whether it is
/// inserting a link or editing one, so the reader can just start typing.
/// </summary>
public class LinkDialogFocusTests
{
    [AvaloniaTheory]
    [InlineData("")]                 // nothing under the caret: an empty label to fill in
    [InlineData("Anthropic")]        // a selection supplied the label
    public void Inserting_a_link_focuses_the_display_text_field(string suggestedText)
    {
        AssertDisplayTextIsFocused(new LinkDialog(suggestedText), expectedText: suggestedText);
    }

    [AvaloniaFact]
    public void Editing_a_link_focuses_the_display_text_field()
    {
        AssertDisplayTextIsFocused(
            new LinkDialog("Anthropic", "https://anthropic.com", editing: true),
            expectedText: "Anthropic");
    }

    private static void AssertDisplayTextIsFocused(LinkDialog dialog, string expectedText)
    {
        var window = TestShell.NewWindow();
        window.Show();

        var host = window.GetVisualDescendants().OfType<OverlayHost>().First();
        _ = host.ShowModalAsync(dialog);
        Dispatcher.UIThread.RunJobs(); // settle focus into the dialog

        var display = dialog.GetVisualDescendants().OfType<TextBox>().First();
        Assert.Equal("Display text", display.PlaceholderText);
        Assert.Same(display, window.FocusManager!.GetFocusedElement());

        // The suggested label survives: the caret sits after it rather than selecting it for replacement.
        Assert.Equal(expectedText, display.Text ?? string.Empty);
        Assert.Equal(expectedText.Length, display.CaretIndex);

        // What focus is actually for: typing straight away appends to the label instead of going nowhere.
        window.KeyTextInput("!");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(expectedText + "!", display.Text);
    }
}
