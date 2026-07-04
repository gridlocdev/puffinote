using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The rich live-preview decorations (PRD §7.4): concealed line-prefix markers render as synthetic UI
/// (bullets, checkboxes, a blockquote bar, a horizontal rule, a computed ordered-list number) that keep
/// the content's hanging indent. WYSIWYG conceals markers regardless of the caret.
/// </summary>
public class DecorationTests
{
    private static MarkdownEditor CaretOffLine0(string text)
    {
        var editor = new MarkdownEditor { Text = text };
        editor.Buffer.Selection = Selection.At(editor.Buffer.Document.Length); // caret on the last line
        return editor;
    }

    private static DecorationKind? DecorationOn(MarkdownEditor editor, int line) =>
        editor.BuildVisualLineForTest(line).Runs
            .Where(r => r.Kind == VisualRunKind.Decoration)
            .Select(r => (DecorationKind?)r.Decoration)
            .FirstOrDefault();

    [AvaloniaFact]
    public void UnorderedListMarker_RendersABullet_WithHangingIndent()
    {
        var editor = CaretOffLine0("- item\nx");
        Assert.Equal(DecorationKind.Bullet, DecorationOn(editor, 0));
        var v = editor.BuildVisualLineForTest(0);
        Assert.True(v.ColumnToX(2) > 0); // "item" is indented past the bullet gutter
        Assert.Contains(v.Runs, r => r.Kind == VisualRunKind.Text && r.Text == "item");
    }

    private static string? OrderedText(MarkdownEditor editor, int line) =>
        editor.BuildVisualLineForTest(line).Runs
            .Where(r => r.Decoration == DecorationKind.OrderedNumber)
            .Select(r => r.Text)
            .FirstOrDefault();

    [AvaloniaFact]
    public void OrderedListNumber_RendersAsAComputedOrdinal()
    {
        var editor = CaretOffLine0("1. item\nx");
        Assert.Equal(DecorationKind.OrderedNumber, DecorationOn(editor, 0));
        Assert.Equal("1. ", OrderedText(editor, 0));
        var v = editor.BuildVisualLineForTest(0);
        Assert.True(v.ColumnToX(3) > 0); // "item" hangs past the number gutter (marker is "1. " = cols 0..3)
    }

    [AvaloniaFact]
    public void OrderedNumbers_ComputeFromStructure_IgnoringLiteralDigits()
    {
        // All "1." in source, but they render 1·2·3 by position.
        var editor = CaretOffLine0("1. a\n1. b\n1. c\nx");
        Assert.Equal("1. ", OrderedText(editor, 0));
        Assert.Equal("2. ", OrderedText(editor, 1));
        Assert.Equal("3. ", OrderedText(editor, 2));
    }

    [AvaloniaFact]
    public void NestedOrderedList_RestartsAndResumes()
    {
        //  1. a
        //  2. b
        //      1. x   (nested restarts)
        //      2. y
        //  3. c        (parent resumes)
        var editor = CaretOffLine0("1. a\n2. b\n    1. x\n    2. y\n3. c\nz");
        Assert.Equal("1. ", OrderedText(editor, 0));
        Assert.Equal("2. ", OrderedText(editor, 1));
        Assert.Equal("1. ", OrderedText(editor, 2)); // nested restarts at 1
        Assert.Equal("2. ", OrderedText(editor, 3));
        Assert.Equal("3. ", OrderedText(editor, 4)); // parent resumes at 3
    }

    [AvaloniaFact]
    public void BlankLine_StartsAFreshOrderedList()
    {
        var editor = CaretOffLine0("1. a\n2. b\n\n1. c\nz");
        Assert.Equal("2. ", OrderedText(editor, 1));
        Assert.Equal("1. ", OrderedText(editor, 3)); // after the blank line, numbering resets
    }

    [AvaloniaFact]
    public void OrderedNumber_ShowsLiteralDigits_InRawSourceMode()
    {
        var editor = CaretOffLine0("5. item\nx");
        editor.ShowRawSource = true;
        var v = editor.BuildVisualLineForTest(0);
        Assert.DoesNotContain(v.Runs, r => r.Decoration == DecorationKind.OrderedNumber);
        Assert.Contains(v.Runs, r => r.Kind == VisualRunKind.Text && r.Text.Contains("5.")); // raw digits shown
    }

    [AvaloniaFact]
    public void TaskMarker_RendersACheckbox_AndNoBullet()
    {
        Assert.Equal(DecorationKind.CheckboxUnchecked, DecorationOn(CaretOffLine0("- [ ] todo\nx"), 0));
        Assert.Equal(DecorationKind.CheckboxChecked, DecorationOn(CaretOffLine0("- [x] done\nx"), 0));
    }

    private static double CheckboxWidth(MarkdownEditor e) =>
        e.BuildVisualLineForTest(0).Runs
            .First(r => r.Decoration is DecorationKind.CheckboxChecked or DecorationKind.CheckboxUnchecked)
            .Width;

    [AvaloniaFact]
    public void CheckboxGutter_IsEqualForCheckedAndUnchecked_AndLeavesAGapBeforeTheText()
    {
        var unticked = CheckboxWidth(new MarkdownEditor { Text = "- [ ] todo" });
        var ticked = CheckboxWidth(new MarkdownEditor { Text = "- [x] done" });
        Assert.Equal(unticked, ticked, precision: 6); // text indents identically whether ticked or not
        // The gutter is wider than the drawn box (~BaseFontSize*0.95), i.e. there is a real gap before the text.
        Assert.True(unticked > MarkdownTheme.Dark.BaseFontSize, "the checkbox gutter should leave a gap before the text");
    }

    [AvaloniaFact]
    public void ClickingTheCheckboxGlyph_TogglesTheTask()
    {
        var editor = CaretOffLine0("- [ ] todo\nx");
        var box = editor.BuildVisualLineForTest(0).Runs.First(r => r.Kind == VisualRunKind.Decoration);
        Assert.True(editor.ToggleTaskClickForTest(0, box.X + box.Width / 2));
        Assert.StartsWith("- [x] todo", editor.Text);
    }

    [AvaloniaFact]
    public void Blockquote_RendersABar_WithHangingIndent()
    {
        var editor = CaretOffLine0("> quote\nx");
        Assert.Equal(DecorationKind.QuoteBar, DecorationOn(editor, 0));
        Assert.True(editor.BuildVisualLineForTest(0).ColumnToX(2) > 0);
    }

    [AvaloniaFact]
    public void HorizontalRule_RendersARuleAndCollapsesToZeroWidth()
    {
        var editor = CaretOffLine0("---\nx");
        Assert.Equal(DecorationKind.HorizontalRule, DecorationOn(editor, 0));
        Assert.Equal(0, editor.BuildVisualLineForTest(0).Width); // the "---" itself takes no width
    }

    [AvaloniaFact]
    public void OnTheCaretLine_DecorationStillShows_ButRawSourceModeRestoresMarkers()
    {
        var editor = new MarkdownEditor { Text = "- item" };
        editor.Buffer.Selection = Selection.At(2); // caret on the list line
        Assert.Equal(DecorationKind.Bullet, DecorationOn(editor, 0)); // WYSIWYG: the bullet shows even here

        editor.ShowRawSource = true;
        Assert.Null(DecorationOn(editor, 0)); // raw "- " shown, no bullet
    }
}
