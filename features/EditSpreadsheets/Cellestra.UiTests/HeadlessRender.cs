using Avalonia.Controls;
using Avalonia.Headless;
using Xunit;

namespace Cellestra.UiTests;

internal static class HeadlessRender
{
    /// <summary>
    /// Run the real render pass and assert it produced a frame. Under null drawing
    /// (<see cref="TestAppBuilder.NullDrawingVariable"/>, the devcontainer default) there are no real
    /// frames, so this still runs <c>Render</c> — surfacing any render-pass exception — but skips the
    /// not-null assertion, keeping the render-capture smoke tests green in a display-less sandbox while
    /// CI / a real desktop still assert a real frame.
    /// </summary>
    public static void AssertRenders(this TopLevel window)
    {
        var frame = window.CaptureRenderedFrame();
        if (!TestAppBuilder.UseNullDrawing)
        {
            Assert.NotNull(frame);
        }
    }
}
