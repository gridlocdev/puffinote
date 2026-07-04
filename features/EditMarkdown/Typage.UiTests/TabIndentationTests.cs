using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The surface treats tabs as indentation everywhere spaces are: tab-nested items render indented and
/// reorder as subtrees, and Tab/Shift+Tab preserve whichever indent character the text already uses.
/// </summary>
public class TabIndentationTests
{
    private static MarkdownEditor At(string text, int line = 0, int col = 0)
    {
        var e = new MarkdownEditor { Text = text };
        e.Buffer.Selection = Selection.At(e.Buffer.Document.GetLineStart(line) + col);
        return e;
    }

    private static double TextX(MarkdownEditor e, int line, string contains) =>
        e.BuildVisualLineForTest(line).Runs.First(r => r.Kind == VisualRunKind.Text && r.Text.Contains(contains)).X;

    [AvaloniaFact]
    public void TabNestedItem_RendersIndentedPastItsParent()
    {
        var e = new MarkdownEditor { Text = "- a\n\t- b" };
        Assert.True(TextX(e, 1, "b") > TextX(e, 0, "a"), "a tab-nested item should hang to the right");
    }

    [AvaloniaFact]
    public void TabNestedCheckbox_MovesAsASubtree()
    {
        var e = At("- [ ] a\n\t- [ ] a1\n- [ ] b"); // caret on the parent, child nested with a tab
        e.MoveLinesForTest(1);
        Assert.Equal("- [ ] b\n- [ ] a\n\t- [ ] a1", e.Text);
    }

    [AvaloniaFact]
    public void MixedIndentSiblings_ReorderTogether()
    {
        // a1 is nested with a tab, a2 with four spaces — both sit at the same visual depth, so they
        // are siblings and swap. (Counting raw characters would treat a2 as a deeper child of a1.)
        var e = At("- a\n\t- a1\n    - a2", line: 1); // caret on the tab-nested first child
        e.MoveLinesForTest(1);
        Assert.Equal("- a\n    - a2\n\t- a1", e.Text);
    }

    [AvaloniaFact]
    public void MoveDown_DoesNotLandBetweenAParentAndItsTabNestedChild()
    {
        // s's child s1 is nested with tabs (fewer characters than s's four spaces). Moving x down must
        // carry s1 along with s, never slot x between them.
        var e = At("- top\n    - x\n    - s\n\t\t- s1", line: 1); // caret on x
        e.MoveLinesForTest(1);
        Assert.Equal("- top\n    - s\n\t\t- s1\n    - x", e.Text);
    }

    [AvaloniaFact]
    public void Outdent_RemovesALeadingTab()
    {
        var e = At("\t- [ ] x");
        e.TabForTest(shift: true);
        Assert.Equal("- [ ] x", e.Text);
    }

    [AvaloniaFact]
    public void Indent_OnATabIndentedLine_AddsATab()
    {
        var e = At("\t- x");
        e.TabForTest(shift: false);
        Assert.Equal("\t\t- x", e.Text);
    }

    [AvaloniaFact]
    public void Indent_OnASpaceContext_AddsSpaces()
    {
        var e = At("- x");
        e.TabForTest(shift: false);
        Assert.Equal("    - x", e.Text); // default IndentWidth spaces — no tab context
    }

    [AvaloniaFact]
    public void Indent_NestingUnderATabParent_UsesATab()
    {
        var e = At("\t- parent\n- child", line: 1);
        e.TabForTest(shift: false);
        Assert.Equal("\t- parent\n\t- child", e.Text); // matches the parent's tab style
    }
}
