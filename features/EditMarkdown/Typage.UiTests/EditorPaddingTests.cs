using Avalonia;
using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// <see cref="MarkdownEditor.HorizontalPadding"/> insets the text column from both edges. Because the
/// wrap width is the viewport minus that inset, widening the padding narrows the text column and makes a
/// long paragraph wrap onto more rows. Null falls back to the theme's own padding.
/// </summary>
public class EditorPaddingTests
{
    private static MarkdownEditor Arranged(string text, double width)
    {
        var e = new MarkdownEditor { Text = text };
        e.Measure(new Size(width, 400));
        e.Arrange(new Rect(0, 0, width, 400));
        return e;
    }

    [AvaloniaFact]
    public void WiderPadding_WrapsALongParagraphOntoMoreRows()
    {
        var paragraph = string.Join(' ', System.Linq.Enumerable.Repeat("word", 40));

        var narrowPadding = Arranged(paragraph, 300);
        narrowPadding.HorizontalPadding = 0;
        var fewRows = narrowPadding.TotalRowsForTest();

        var widePadding = Arranged(paragraph, 300);
        widePadding.HorizontalPadding = 120;
        var manyRows = widePadding.TotalRowsForTest();

        Assert.True(manyRows > fewRows, $"expected more rows with wider padding, got {manyRows} vs {fewRows}");
    }

    [AvaloniaFact]
    public void NullPadding_FallsBackToTheThemeDefault()
    {
        var e = Arranged("hello", 300);
        e.HorizontalPadding = 40;
        e.HorizontalPadding = null; // back to the theme's PaddingLeft — no exception, still lays out
        Assert.Equal(1, e.TotalRowsForTest());
    }

    [AvaloniaFact]
    public void VerticalPadding_DoesNotAffectWrapping()
    {
        var paragraph = string.Join(' ', System.Linq.Enumerable.Repeat("word", 40));
        var e = Arranged(paragraph, 300);
        var rows = e.TotalRowsForTest();
        e.VerticalPadding = 120; // top/bottom inset — rows (wrapping) are unchanged
        Assert.Equal(rows, e.TotalRowsForTest());
    }

    [AvaloniaFact]
    public void VerticalPadding_GrowsTheScrollExtentHeight()
    {
        var e = Arranged("one line", 300);
        var before = ((Avalonia.Controls.Primitives.IScrollable)e).Extent.Height;
        e.VerticalPadding = 100;
        var after = ((Avalonia.Controls.Primitives.IScrollable)e).Extent.Height;
        // The extent adds the inset at both the top and the bottom, so it grows by 2 x the increase.
        Assert.True(after > before, $"expected a taller extent, got {after} vs {before}");
    }
}
