using System.Linq;
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
}
