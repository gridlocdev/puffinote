using Avalonia.Headless.XUnit;
using Puffinote.Text.Document;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Full-WYSIWYG concealment (PRD §7.4, revised): markers are truly hidden (zero-width) <b>always</b> in
/// normal mode — the caret and selections never reveal raw Markdown — and raw-source mode reveals
/// everything. These drive the real editor and inspect the laid-out <see cref="VisualLine"/> it builds.
/// </summary>
public class ConcealmentTests
{
    private static MarkdownEditor At(string text, int caret)
    {
        var editor = new MarkdownEditor { Text = text };
        editor.Buffer.Selection = Selection.At(caret);
        return editor;
    }

    /// <summary>True when column <paramref name="col"/> of <paramref name="line"/> falls in a concealed run.</summary>
    private static bool Concealed(MarkdownEditor editor, int line, int col) =>
        editor.BuildVisualLineForTest(line).Runs.Any(r =>
            r.Kind == VisualRunKind.Concealed && col >= r.Col && col < r.Col + r.Len);

    private static bool ShowsText(MarkdownEditor editor, int line, string text) =>
        editor.BuildVisualLineForTest(line).Runs.Any(r => r.Kind == VisualRunKind.Text && r.Text.Contains(text));

    [AvaloniaFact]
    public void HeadingMarker_StaysHidden_EvenWithTheCaretOnTheLine()
    {
        var editor = At("# Title\nbody", caret: 9); // caret on line 1 ("body")
        Assert.True(Concealed(editor, 0, 0));  // the "# " is hidden
        Assert.True(ShowsText(editor, 0, "Title"));

        editor.Buffer.Selection = Selection.At(2); // move caret onto the heading line
        Assert.True(Concealed(editor, 0, 0));      // still hidden — WYSIWYG never reveals the raw marker
    }

    [AvaloniaFact]
    public void HeadingContent_StartsAtXZero_WhenMarkerHidden()
    {
        var editor = At("# Title\nbody", caret: 9);
        var v = editor.BuildVisualLineForTest(0);
        // "Title" begins at column 2; with "# " concealed it lays out at x = 0.
        Assert.Equal(0, v.ColumnToX(2));
    }

    [AvaloniaFact]
    public void Emphasis_StaysHidden_EvenWithTheCaretInside()
    {
        // "**a** and **b**": node 1 = [0,5], node 2 = [10,15].
        var editor = At("**a** and **b**\nx", caret: 16); // caret on line 1
        Assert.True(Concealed(editor, 0, 0));   // first  ** hidden
        Assert.True(Concealed(editor, 0, 10));  // second ** hidden

        editor.Buffer.Selection = Selection.At(2); // caret inside the first bold
        Assert.True(Concealed(editor, 0, 0));   // still hidden — the caret never reveals raw markup
        Assert.True(Concealed(editor, 0, 10));  // and the other node stays hidden too
    }

    [AvaloniaFact]
    public void Link_ShowsTextOnly_WhenCaretAway()
    {
        var editor = At("[home](http://x.com)\nx", caret: 21); // caret on line 1
        Assert.True(ShowsText(editor, 0, "home"));
        Assert.True(Concealed(editor, 0, 0));   // the opening '['
        Assert.True(Concealed(editor, 0, 10));  // somewhere in "](http://x.com)"
    }

    [AvaloniaFact]
    public void Selection_DoesNotReveal_InWysiwygMode()
    {
        var editor = At("**a** and **b**\nx", caret: 16);
        editor.Buffer.Selection = new Selection(11, 14); // overlaps the second bold node
        Assert.True(Concealed(editor, 0, 10));           // still hidden — selecting doesn't reveal raw markup
    }

    [AvaloniaFact]
    public void RawSourceMode_RevealsEverything()
    {
        var editor = At("# Title\n**b**", caret: 12); // caret on line 1, away from the heading
        Assert.True(Concealed(editor, 0, 0));
        editor.ShowRawSource = true;
        Assert.False(Concealed(editor, 0, 0));  // heading marker
        Assert.False(Concealed(editor, 1, 0));  // bold markers
    }

    [AvaloniaFact]
    public void HardBreakAndPlainText_AreNeverConcealed()
    {
        var editor = At("plain text only\nx", caret: 16);
        var v = editor.BuildVisualLineForTest(0);
        Assert.DoesNotContain(v.Runs, r => r.Kind == VisualRunKind.Concealed);
    }
}
