using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Control.Views;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The caret-format state the toolbar reads to light up its tools (like the spreadsheet's bar): bold/italic/
/// code/strike/highlight from the caret's inline tokens, and list/quote/heading from its line prefix.
/// </summary>
public class CaretFormatStateTests
{
    private static MarkdownEditor At(string text, int caret)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(caret);
        return e;
    }

    [AvaloniaFact]
    public void Inline_formats_light_only_when_the_caret_is_inside_them()
    {
        Assert.True(At("**bold** rest", 4).IsBoldActive);   // inside "bold"
        Assert.False(At("**bold** rest", 11).IsBoldActive); // inside "rest"
        Assert.True(At("*it* x", 2).IsItalicActive);
        Assert.True(At("~~st~~ x", 3).IsStrikethroughActive);
        Assert.True(At("==hi== x", 3).IsHighlightActive);
        Assert.True(At("`c` x", 1).IsInlineCodeActive);

        // Plain text lights nothing.
        var plain = At("just text", 4);
        Assert.False(plain.IsBoldActive);
        Assert.False(plain.IsItalicActive);
        Assert.False(plain.IsInlineCodeActive);
    }

    [AvaloniaFact]
    public void Bold_italic_layer_independently()
    {
        // In bold-italic (***w***), both light at once.
        var e = At("***w*** x", 4);
        Assert.True(e.IsBoldActive);
        Assert.True(e.IsItalicActive);
    }

    [AvaloniaFact]
    public void Line_level_states_reflect_the_caret_line()
    {
        Assert.Equal(1, At("# Title", 3).CaretHeadingLevel);
        Assert.Equal(3, At("### Deep", 5).CaretHeadingLevel);
        Assert.Equal(0, At("plain", 2).CaretHeadingLevel);

        Assert.True(At("- [ ] task", 8).IsTaskActive);
        Assert.True(At("- item", 3).IsBulletListActive);
        Assert.True(At("1. item", 4).IsNumberedListActive);
        Assert.True(At("> quote", 3).IsQuoteActive);
        Assert.False(At("plain", 2).IsQuoteActive);
    }

    [AvaloniaFact]
    public void A_toggle_command_reports_its_active_state_from_the_caret()
    {
        var view = new MarkdownEditorView { Text = "**bold**" };
        view.Editor.Buffer.Selection = Selection.At(3); // inside the bold text

        var bold = view.EditingCommands.Single(c => c.Id == "format.bold");
        Assert.True(bold.IsToggleActive);

        var italic = view.EditingCommands.Single(c => c.Id == "format.italic");
        Assert.False(italic.IsToggleActive);
    }
}
