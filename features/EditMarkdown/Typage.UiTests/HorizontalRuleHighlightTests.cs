using Avalonia.Headless.XUnit;
using Puffinote.Text.Document;
using Typage.Control.Controls;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// A horizontal rule has no real text to put a visible caret on, so when the caret is on the rule line in
/// WYSIWYG (non-source) view the whole row is highlighted in the selection colour. These cover the decision
/// that drives that draw (the fill itself can't be pixel-tested headlessly).
/// </summary>
public class HorizontalRuleHighlightTests
{
    private static MarkdownEditor Editor(out int ruleLine)
    {
        // line 0: "before", 1: blank, 2: "---" (the rule), 3: blank, 4: "after"
        ruleLine = 2;
        return new MarkdownEditor { Text = "before\n\n---\n\nafter" };
    }

    private static void PutCaretOn(MarkdownEditor e, int line) =>
        e.Buffer.Selection = Selection.At(e.Buffer.Document.GetLineStart(line));

    [AvaloniaFact]
    public void CaretOnTheRule_HighlightsTheRuleLine()
    {
        var e = Editor(out var rule);
        PutCaretOn(e, rule);
        Assert.True(e.ShouldHighlightRuleLine(rule));
    }

    [AvaloniaFact]
    public void CaretElsewhere_DoesNotHighlight()
    {
        var e = Editor(out var rule);
        PutCaretOn(e, 0); // caret on "before"
        Assert.False(e.ShouldHighlightRuleLine(rule));
    }

    [AvaloniaFact]
    public void ANonRuleLine_IsNeverHighlighted()
    {
        var e = Editor(out _);
        PutCaretOn(e, 0);
        Assert.False(e.ShouldHighlightRuleLine(0)); // "before" is not a rule
    }

    [AvaloniaFact]
    public void ARangeSelection_DoesNotHighlight()
    {
        var e = Editor(out var rule);
        var start = e.Buffer.Document.GetLineStart(rule);
        e.Buffer.Selection = new Selection(start, start + 3); // the "---" selected as a range
        Assert.False(e.ShouldHighlightRuleLine(rule));
    }

    [AvaloniaFact]
    public void RawSourceView_DoesNotHighlight()
    {
        var e = Editor(out var rule);
        PutCaretOn(e, rule);
        e.ShowRawSource = true; // raw view shows the literal --- text; usual caret applies
        Assert.False(e.ShouldHighlightRuleLine(rule));
    }
}
