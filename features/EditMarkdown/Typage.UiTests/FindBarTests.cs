using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Typage.Control.Views;
using Typage.Desktop.Views;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

public class FindBarTests
{
    private static FindBar Open(MarkdownEditorView view, bool replace = false)
    {
        var bar = new FindBar(view, Brushes.Transparent, Brushes.Transparent);
        bar.Show(replace);
        return bar;
    }

    [AvaloniaFact]
    public void Matches_RefreshWhenDocumentEditedWhileOpen()
    {
        var view = new MarkdownEditorView { Text = "foo bar foo" };
        var bar = Open(view);
        bar.SetQuery("foo");
        Assert.Equal(2, bar.Matches.Count);
        Assert.Equal(0, bar.Matches[0].Start);

        // Edit the document underneath the open find bar.
        view.Editor.Buffer.Selection = new Selection(0, 0);
        view.Editor.Buffer.Insert("AB"); // everything shifts +2 → "ABfoo bar foo"

        // The bar must have re-found against the new text, not kept stale offsets.
        Assert.Equal(2, bar.Matches.Count);
        Assert.Equal(2, bar.Matches[0].Start);
        Assert.Equal(10, bar.Matches[1].Start);
    }

    [AvaloniaFact]
    public void ReplaceAll_IsASingleUndoStep()
    {
        var view = new MarkdownEditorView { Text = "a a a" };
        var bar = Open(view, replace: true);
        bar.SetQuery("a");
        bar.SetReplacement("bb");
        bar.ReplaceAllForTest();
        Assert.Equal("bb bb bb", view.Text);

        view.Undo(); // one undo reverts the whole Replace All
        Assert.Equal("a a a", view.Text);
    }

    [AvaloniaFact]
    public void ReplaceAll_LeavesUntouchedTextOutsideTheMatchSpan()
    {
        var view = new MarkdownEditorView { Text = "keep [x] middle [x] keep" };
        var bar = Open(view, replace: true);
        bar.SetQuery("[x]");
        bar.SetReplacement("Y");
        bar.ReplaceAllForTest();
        Assert.Equal("keep Y middle Y keep", view.Text);
    }
}
