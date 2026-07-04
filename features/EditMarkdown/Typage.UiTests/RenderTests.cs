using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Typage.Control.Views;
using Xunit;

namespace Typage.UiTests;

public class RenderTests
{
    [AvaloniaFact]
    public void RendersWideContent_WithoutInvalidatingDuringRenderPass()
    {
        var view = new MarkdownEditorView();
        var window = new Window { Width = 300, Height = 200, Content = view };
        window.Show();

        // Wide + tall content so the horizontal extent must grow during render — the path that used
        // to call RaiseScrollInvalidated synchronously and throw "Visual was invalidated during the
        // render pass" (the crash when pasting a large snippet).
        view.Text = string.Join("\n", Enumerable.Range(0, 40).Select(_ => new string('x', 400)));

        window.CaptureRenderedFrame(); // forces a render pass; must not throw

        Assert.True(view.Editor.RawMeasureCount > 0); // confirms Render actually executed
    }

    [AvaloniaFact]
    public void BlockVisual_IsCached_AcrossCallsUntilTheLayoutChanges()
    {
        var editor = new Typage.Control.Controls.MarkdownEditor { Text = "# Title\n\nsome **bold** body text" };
        editor.BuildVisualLineForTest(2); // first build measures the text
        var afterFirst = editor.RawMeasureCount;
        Assert.True(afterFirst > 0);

        editor.BuildVisualLineForTest(2); // a second look with no edit must reuse the cached visual
        Assert.Equal(afterFirst, editor.RawMeasureCount); // no fresh measuring

        editor.Text = "# Title\n\nsome **bold** body text changed"; // an edit invalidates the cache
        editor.BuildVisualLineForTest(2);
        Assert.True(editor.RawMeasureCount > afterFirst); // rebuilt (measured again)
    }

    [AvaloniaFact]
    public void RendersRichConstructs_WithoutThrowing()
    {
        var view = new MarkdownEditorView();
        var window = new Window { Width = 400, Height = 320, Content = view };
        window.Show();

        // Every concealment + decoration path on one screen (caret on line 0, the rest concealed):
        // heading, bullets, checkboxes, a quote bar, a rule, a code band, and inline bold/link.
        view.Text = "# Heading\n\n- bullet\n- [ ] task\n- [x] done\n\n> quote\n\n---\n\n```\ncode\n```\n\n**bold** and [link](http://x.com)\n";

        window.CaptureRenderedFrame(); // exercises DrawDecoration (ellipse/rect/line) + the code band

        Assert.True(view.Editor.RawMeasureCount > 0);
    }

    [AvaloniaFact]
    public void CaretAtEndOfDocEndingInNewline_RendersWithoutLandingOnACollapsedBlock()
    {
        var view = new MarkdownEditorView();
        var window = new Window { Width = 300, Height = 200, Content = view };
        window.Show();

        // The trailing "\n" makes the last line a collapsed blank separator. Ctrl/Cmd+End leaves the caret
        // on it; the render must still execute (the caret is drawn in the resolved content block, not lost
        // in the skipped zero-row block).
        view.Text = "Hello\n";
        view.Editor.Buffer.Selection = Typage.Engine.Document.Selection.At(view.Editor.Buffer.Document.Length);

        window.CaptureRenderedFrame(); // must not throw

        Assert.True(view.Editor.RawMeasureCount > 0);
    }
}
