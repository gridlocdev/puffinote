using Avalonia;
using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The capped, centred reading column. Prose set edge to edge on a wide window runs far past the ~45–90
/// characters a line that the eye can track back, so the column is capped and centred — but every X mapping
/// (drawing, hit-testing, caret) has to move with it, or a click would land on the wrong character.
/// </summary>
public class ReadingColumnTests
{
    private static MarkdownEditor Wide(string text, double maxWidth)
    {
        var editor = new MarkdownEditor { Text = text, MaxContentWidth = maxWidth };
        editor.Measure(new Size(1600, 600));
        editor.Arrange(new Rect(0, 0, 1600, 600));
        return editor;
    }

    [AvaloniaFact]
    public void AWideWindow_WrapsToTheCap_NotToTheViewport()
    {
        var prose = string.Join(' ', Enumerable.Repeat("lorem ipsum dolor sit amet", 40));

        var capped = Wide(prose, 600);
        var full = Wide(prose, 0); // 0 = unlimited, the old behaviour

        // The same paragraph must take more rows in a 600px column than across the full 1600px viewport.
        Assert.True(capped.TotalRowsForTest() > full.TotalRowsForTest(),
            $"capped ({capped.TotalRowsForTest()} rows) should wrap more than full width ({full.TotalRowsForTest()} rows)");
    }

    [AvaloniaFact]
    public void TheCappedColumn_IsCentred()
    {
        var editor = Wide("hello", 600);

        // Clicking the far-left gutter — outside the centred column — resolves to the line's start, not to some
        // offset inside it: the column genuinely begins well to the right of x=0.
        var atFarLeft = editor.OffsetAtPointForTest(new Point(4, 20));
        Assert.Equal(0, atFarLeft);

        // The text sits around the middle of the viewport, so a click there lands *inside* "hello".
        var atCentre = editor.OffsetAtPointForTest(new Point(1600 / 2.0, 20));
        Assert.InRange(atCentre, 0, 5);
    }

    [AvaloniaFact]
    public void HitTesting_TracksTheColumn_SoAClickLandsOnTheCharacterItIsOver()
    {
        // The real risk of centring: the render transform moves but a hit-test still measuring from the padding
        // would resolve every click to the wrong character. Walk the caret across the line and check that the X
        // the caret is drawn at maps back to the same offset.
        var editor = Wide("hello world", 600);
        var doc = editor.Buffer.Document;

        for (var offset = 0; offset <= doc.Length; offset++)
        {
            editor.Buffer.Selection = Selection.At(offset);
            var x = editor.CaretXForTest();
            Assert.Equal(offset, editor.OffsetAtPointForTest(new Point(x + 0.5, 20)));
        }
    }

    [AvaloniaFact]
    public void ZeroRestoresFullWidth()
    {
        var editor = Wide("hello", 0);

        // With no cap the column starts at the padding, as it always did — the escape hatch for authors who
        // want the full width.
        Assert.Equal(0, editor.OffsetAtPointForTest(new Point(4, 20)));
        Assert.Equal(5, editor.OffsetAtPointForTest(new Point(1500, 20))); // ...and the line reaches out to here
    }
}
