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
}
