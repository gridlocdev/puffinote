using System.Linq;
using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The rich live-preview decorations (PRD §7.4): concealed line-prefix markers render as synthetic UI
/// (bullets, checkboxes, a blockquote bar, a horizontal rule) that keeps the content's hanging indent,
/// while ordered-list numbers stay visible. Drives the real editor with the caret off the line so the
/// markers conceal.
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
    public void OrderedListNumber_StaysVisible()
    {
        var editor = CaretOffLine0("1. item\nx");
        Assert.Null(DecorationOn(editor, 0)); // no bullet
        var v = editor.BuildVisualLineForTest(0);
        Assert.Contains(v.Runs, r => r.Kind == VisualRunKind.Text && r.Text.Contains("1.")); // the number isn't hidden
        Assert.DoesNotContain(v.Runs, r => r.Kind == VisualRunKind.Concealed);
    }

    [AvaloniaFact]
    public void TaskMarker_RendersACheckbox_AndNoBullet()
    {
        Assert.Equal(DecorationKind.CheckboxUnchecked, DecorationOn(CaretOffLine0("- [ ] todo\nx"), 0));
        Assert.Equal(DecorationKind.CheckboxChecked, DecorationOn(CaretOffLine0("- [x] done\nx"), 0));
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
