using Avalonia.Headless.XUnit;
using Puffinote.Text.Document;
using Typage.Control.Controls;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The offset↔visual map exercised through the real editor (PRD §7.4): clicks and caret placement
/// resolve to correct document offsets even on lines whose markers are concealed, and a click never
/// lands strictly inside a hidden marker (it is atomic).
/// </summary>
public class OffsetMapTests
{
    private static MarkdownEditor At(string text, int caret)
    {
        var editor = new MarkdownEditor { Text = text };
        editor.Buffer.Selection = Selection.At(caret);
        return editor;
    }

    [AvaloniaFact]
    public void ClickOnAConcealedLine_NeverLandsInsideAHiddenMarker()
    {
        var editor = At("**bold** word\nx", caret: 14); // caret on line 1 → line 0 concealed
        var v = editor.BuildVisualLineForTest(0);
        var lineStart = editor.Buffer.Document.GetLineStart(0);

        // Sweep across the laid-out width; every resolved offset must be a valid (non-interior) stop.
        for (var x = -4d; x <= v.Width + 4; x += 0.5)
        {
            var col = editor.OffsetAtForTest(0, x) - lineStart;
            Assert.False(v.IsInsideConcealed(col));
        }
    }

    [AvaloniaFact]
    public void CaretX_RoundTripsThroughTheMap_OnAConcealedLine()
    {
        var editor = At("# Heading here\nbody", caret: 17); // caret on line 1
        var v = editor.BuildVisualLineForTest(0);
        var lineStart = editor.Buffer.Document.GetLineStart(0);

        // Clicking at the x of content column 2 ("Heading") resolves back to column 2.
        var x = v.ColumnToX(2);
        Assert.Equal(lineStart + 2, editor.OffsetAtForTest(0, x));
    }

    [AvaloniaFact]
    public void HiddenMarkers_DoNotWidenTheLine()
    {
        // The same visible text with and without surrounding markers lays out to the same width
        // once the markers are concealed (they collapse to zero).
        var withMarkers = At("**bold**\nx", caret: 9).BuildVisualLineForTest(0);   // caret away → concealed
        var plain = At("bold\nx", caret: 6).BuildVisualLineForTest(0);
        Assert.Equal(plain.Width, withMarkers.Width, precision: 3);
    }
}
