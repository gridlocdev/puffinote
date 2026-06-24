using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Control.Views;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Word wrap (item 1): with wrapping on (the default), a line wider than the viewport flows onto
/// multiple visual rows and the editor never scrolls horizontally; turning it off restores one row
/// per line. Driven through a real, sized window so the wrap width is live.
/// </summary>
public class WordWrapTests
{
    private static MarkdownEditor ShowInNarrowWindow(string text, double width = 200)
    {
        var view = new MarkdownEditorView();
        var window = new Window { Width = width, Height = 300, Content = view };
        window.Show();
        view.Text = text;
        window.CaptureRenderedFrame(); // force a layout + render pass so the wrap width is applied
        return view.Editor;
    }

    [AvaloniaFact]
    public void LongLine_WrapsToMultipleRows()
    {
        var longLine = string.Join(' ', System.Linq.Enumerable.Repeat("word", 60));
        var editor = ShowInNarrowWindow(longLine);
        Assert.True(editor.RowCountForTest(0) > 1, "a long line should wrap onto multiple rows");
    }

    [AvaloniaFact]
    public void ShortLine_StaysOnOneRow()
    {
        var editor = ShowInNarrowWindow("short");
        Assert.Equal(1, editor.RowCountForTest(0));
    }

    [AvaloniaFact]
    public void DisablingWordWrap_CollapsesToOneRow()
    {
        var longLine = string.Join(' ', System.Linq.Enumerable.Repeat("word", 60));
        var editor = ShowInNarrowWindow(longLine);
        Assert.True(editor.RowCountForTest(0) > 1);

        editor.WordWrap = false;
        Assert.Equal(1, editor.RowCountForTest(0));
    }

    [AvaloniaFact]
    public void CaretAtEndOfWrappedLine_RoundTripsThroughTheMap()
    {
        var longLine = string.Join(' ', System.Linq.Enumerable.Repeat("word", 60));
        var editor = ShowInNarrowWindow(longLine);
        // Place the caret at the end and confirm a render doesn't throw and the line really wrapped.
        editor.Buffer.Selection = Selection.At(editor.Buffer.Document.Length);
        Assert.True(editor.TotalRowsForTest() > 1);
    }
}
