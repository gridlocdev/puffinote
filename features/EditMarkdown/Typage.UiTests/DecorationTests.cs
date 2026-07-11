using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Engine.Document;
using Typage.Engine.Markdown;
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

    [AvaloniaFact]
    public void TheBulletGutter_PadsTheDotOnBothSides()
    {
        // The gutter used to be the measured width of the source "- " — a couple of pixels wider than the dot
        // itself — which left the bullet crowded against both the margin and its own text.
        var theme = MarkdownTheme.Default;
        var editor = CaretOffLine0("- item\nx");
        var v = editor.BuildVisualLineForTest(0);
        var bullet = v.Runs.First(r => r.Decoration == DecorationKind.Bullet);

        Assert.Equal(theme.BulletGutter, bullet.Width, 3);
        Assert.Equal(theme.BulletGutter, v.ColumnToX(2), 3); // "item" starts past the whole gutter

        // Space on each side of the dot, the gap to the text being the wider of the two.
        Assert.True(theme.BulletLeftPad > 0);
        Assert.True(theme.BulletGap > theme.BulletLeftPad);
        Assert.True(theme.BulletGutter > theme.BulletSize * 2, "the dot should be padded, not filling its gutter");
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
    public void AMultiLineItem_KeepsTheNumberingRunning()
    {
        // "wrapped…" is a lazy continuation: the layout flows it into item 1, so those two source lines are
        // one item. The items below it therefore go on counting — they don't restart a fresh list.
        var editor = CaretOffLine0("1. a\nwrapped onto a second source line\n1. b\n1. c\nz");
        Assert.Equal("1. ", OrderedText(editor, 0));
        Assert.Equal("2. ", OrderedText(editor, 2));
        Assert.Equal("3. ", OrderedText(editor, 3));
    }

    [AvaloniaFact]
    public void AParagraphAfterABlankLine_StillStartsAFreshList()
    {
        // The counterpart: this paragraph continues nothing (the blank closed the list above it), so the item
        // below it opens a new list at 1.
        var editor = CaretOffLine0("1. a\n2. b\n\nA new paragraph.\n1. c\nz");
        Assert.Equal("1. ", OrderedText(editor, 4));
    }

    [AvaloniaFact]
    public void ABlockBetweenItems_EndsTheList()
    {
        // Only *prose* continues an item. A line that opens a block of its own ends the list, so what follows
        // is a new one — even with no blank line between.
        Assert.Equal("1. ", OrderedText(CaretOffLine0("1. a\n## Next\n1. b\nz"), 2));
        Assert.Equal("1. ", OrderedText(CaretOffLine0("1. a\n---\n1. b\nz"), 2));
        Assert.Equal("1. ", OrderedText(CaretOffLine0("1. a\n- bullet\n1. b\nz"), 2));
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
    public void BulletedTask_RendersACheckbox_AndNoBullet()
    {
        // The "- " of a bulleted task is the bullet the checkbox sits on — part of how the checkbox is
        // spelled, not a second marker — so the box is drawn alone. (Contrast the numbered case below.)
        Assert.Equal([DecorationKind.Checkbox], DecorationsOn(CaretOffLine0("- [ ] todo\nx"), 0));
        Assert.Equal([DecorationKind.Checkbox], DecorationsOn(CaretOffLine0("- [x] done\nx"), 0));

        // One decoration kind, six states: the box carries which one it is in (PRD §8.3.2).
        Assert.Equal(TaskState.NotStarted, BoxState(CaretOffLine0("- [ ] todo\nx"), 0));
        Assert.Equal(TaskState.Done, BoxState(CaretOffLine0("- [x] done\nx"), 0));
    }

    [AvaloniaFact]
    public void NumberedTask_KeepsItsNumber_AlongsideTheCheckbox()
    {
        // The asymmetry with a bullet: a number says *which* item of the list this is, which the checkbox
        // can't express, so it survives. It used to vanish, leaving a bare checkbox orphaned from its list.
        var editor = CaretOffLine0("1. [ ] step\nx");
        Assert.Contains(DecorationKind.OrderedNumber, DecorationsOn(editor, 0));
        Assert.Equal("1. ", OrderedText(editor, 0));
    }

    [AvaloniaFact]
    public void NumberedTask_DrawsTheCheckboxToTheLeftOfTheNumber()
    {
        // The box leads and the number follows — "☐ 1. step" — even though the source spells it "1. [ ] step".
        var editor = CaretOffLine0("1. [x] step\nx");
        Assert.Equal([DecorationKind.Checkbox, DecorationKind.OrderedNumber], DecorationsOn(editor, 0));

        var runs = editor.BuildVisualLineForTest(0).Runs;
        var box = runs.First(r => r.Decoration == DecorationKind.Checkbox);
        var number = runs.First(r => r.Decoration == DecorationKind.OrderedNumber);
        Assert.Equal(0, box.X); // the box starts the line
        Assert.Equal(box.X + box.Width, number.X, precision: 6); // the number sits immediately after it
        Assert.True(editor.BuildVisualLineForTest(0).ColumnToX(7) >= number.X + number.Width); // "step" hangs past both
    }

    // ----- GFM task-list strictness: a checkbox is only ever the *first* thing in a list item's content -----

    [AvaloniaFact]
    public void ASecondCheckboxOnTheSameLine_IsLiteralText()
    {
        // Only the leading marker is a live box; a later one is prose. There is no way to get two live boxes
        // on one list line — that needs two items.
        foreach (var src in new[] { "1. [ ] buy milk [ ] eggs\nx", "- [ ] buy milk [ ] eggs\nx" })
        {
            var editor = CaretOffLine0(src);
            Assert.Single(DecorationsOn(editor, 0), d => d is DecorationKind.Checkbox);
            Assert.Contains(editor.BuildVisualLineForTest(0).Runs, r => r.Kind == VisualRunKind.Text && r.Text.Contains("[ ] eggs"));
        }
    }

    [AvaloniaFact]
    public void ACheckboxInPlainProse_IsLiteralText()
    {
        Assert.Empty(DecorationsOn(CaretOffLine0("plain [ ] prose\nx"), 0));
    }

    [AvaloniaFact]
    public void ACheckboxNotDirectlyAfterTheListMarker_IsLiteralText()
    {
        // "1. - [ ] x" is a number followed by prose that merely looks like a checkbox: the box doesn't sit in
        // the item's leading position, so it stays literal and only the number is drawn.
        var editor = CaretOffLine0("1. - [ ] x\nx");
        Assert.Equal([DecorationKind.OrderedNumber], DecorationsOn(editor, 0));

        // Likewise "- - [ ] x": the outer bullet is drawn, the inner "- [ ] x" is text.
        var nested = CaretOffLine0("- - [ ] x\nx");
        Assert.Equal([DecorationKind.Bullet], DecorationsOn(nested, 0));
    }

    [AvaloniaFact]
    public void AMissingSpaceAroundTheBox_MeansItIsNotACheckbox()
    {
        // GFM needs a space after the list marker and after the closing bracket.
        Assert.Empty(DecorationsOn(CaretOffLine0("1.[ ] task\nx"), 0));                        // no space after "1."
        Assert.Equal([DecorationKind.OrderedNumber], DecorationsOn(CaretOffLine0("1. [ ]task\nx"), 0)); // none after "]"
        Assert.Equal([DecorationKind.Bullet], DecorationsOn(CaretOffLine0("- [ ]task\nx"), 0));
    }

    [AvaloniaFact]
    public void ANumberAfterACheckbox_RendersTheBoxToTheLeftOfIt()
    {
        // "- [ ] 1. x": the box is the item's leading content, so it's live; the "1." is just text beside it.
        var editor = CaretOffLine0("- [ ] 1. x\nx");
        Assert.Equal([DecorationKind.Checkbox], DecorationsOn(editor, 0));
        Assert.Contains(editor.BuildVisualLineForTest(0).Runs, r => r.Kind == VisualRunKind.Text && r.Text.Contains("1. x"));
    }

    [AvaloniaFact]
    public void NumberedTasks_StillNumberSequentially()
    {
        var editor = CaretOffLine0("1. [ ] one\n1. [x] two\n1. [ ] three\n\nx");
        Assert.Equal("1. ", OrderedText(editor, 0));
        Assert.Equal("2. ", OrderedText(editor, 1));
        Assert.Equal("3. ", OrderedText(editor, 2));
    }

    private static DecorationKind[] DecorationsOn(MarkdownEditor editor, int line) =>
        [.. editor.BuildVisualLineForTest(line).Runs
            .Where(r => r.Kind == VisualRunKind.Decoration)
            .Select(r => r.Decoration)];

    /// <summary>The checkbox decoration run on <paramref name="line"/> — a line has at most one live box.</summary>
    private static VisualRun Box(MarkdownEditor e, int line) =>
        e.BuildVisualLineForTest(line).Runs.First(r => r.Decoration == DecorationKind.Checkbox);

    /// <summary>Which of the six states that box is in: the run carries it as its text (the box character).</summary>
    private static TaskState BoxState(MarkdownEditor e, int line) => TaskStates.FromMark(Box(e, line).Text[0]);

    private static double CheckboxWidth(MarkdownEditor e) => Box(e, 0).Width;

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
        // Target the checkbox specifically — it now sits after the bullet, so "the first decoration" is the
        // bullet, and this also pins that the glyph stays clickable wherever the layout puts it.
        var editor = CaretOffLine0("- [ ] todo\nx");
        var box = Box(editor, 0);
        Assert.True(editor.ToggleTaskClickForTest(0, box.X + box.Width / 2));
        Assert.StartsWith("- [x] todo", editor.Text);
    }

    [AvaloniaFact]
    public void ClickingTheNumberOfANumberedTask_DoesNotToggleIt()
    {
        // The number is not the checkbox: clicking it places the caret rather than flipping the box.
        var editor = CaretOffLine0("1. [ ] step\nx");
        var number = editor.BuildVisualLineForTest(0).Runs.First(r => r.Decoration == DecorationKind.OrderedNumber);
        Assert.False(editor.ToggleTaskClickForTest(0, number.X + number.Width / 2));
        Assert.StartsWith("1. [ ] step", editor.Text);
    }

    [AvaloniaFact]
    public void ClickingTheCheckboxOfANumberedTask_TogglesIt()
    {
        var editor = CaretOffLine0("1. [ ] step\nx");
        var box = Box(editor, 0);
        Assert.True(editor.ToggleTaskClickForTest(0, box.X + box.Width / 2));
        Assert.StartsWith("1. [x] step", editor.Text);
    }

    [AvaloniaFact]
    public void Blockquote_RendersABar_WithHangingIndent()
    {
        var editor = CaretOffLine0("> quote\nx");
        Assert.Equal(DecorationKind.QuoteBar, DecorationOn(editor, 0));
        Assert.True(editor.BuildVisualLineForTest(0).ColumnToX(2) > 0);
    }

    [AvaloniaFact]
    public void ANestedBlockquote_IndentsAStepPerLevel_AndClearsEveryBar()
    {
        // The gutter used to be the measured width of "> " / ">> " — a couple of pixels either way — so nesting
        // barely showed, and a ">>" quote's text sat on top of the second bar drawn to its left.
        var theme = MarkdownTheme.Default;
        var editor = CaretOffLine0("> one\n>> two\n>>> three\nx");

        double Gutter(int line) => editor.BuildVisualLineForTest(line).Runs
            .First(r => r.Decoration == DecorationKind.QuoteBar).Width;

        Assert.Equal(theme.QuoteGutter(1), Gutter(0), 3);
        Assert.Equal(theme.QuoteGutter(2), Gutter(1), 3);
        Assert.Equal(theme.QuoteGutter(3), Gutter(2), 3);

        // Each level steps by exactly one bar-plus-gap, the same step the bars themselves are drawn on…
        Assert.Equal(theme.QuoteBarStep, Gutter(1) - Gutter(0), 3);
        Assert.Equal(theme.QuoteBarStep, Gutter(2) - Gutter(1), 3);

        // …so the text always starts past the rightmost bar of its own depth.
        for (var depth = 1; depth <= 3; depth++)
        {
            var lastBarRight = ((depth - 1) * theme.QuoteBarStep) + theme.QuoteBarWidth;
            Assert.True(Gutter(depth - 1) > lastBarRight,
                $"a depth-{depth} quote's text ({Gutter(depth - 1)}) overlaps its innermost bar (ends at {lastBarRight})");
        }
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
