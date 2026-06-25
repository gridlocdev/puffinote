using System;
using Avalonia;
using Avalonia.Headless;
using Cellestra.Desktop;
using Cellestra.UiTests;

// One-time registration of the Avalonia headless platform for the whole UI
// test assembly. [AvaloniaFact]/[AvaloniaTheory] tests run on this app.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Cellestra.UiTests;

public static class TestAppBuilder
{
    /// <summary>
    /// Environment variable name that opts the headless tests into Avalonia's <b>null</b> drawing
    /// backend. Set it (to any non-empty value) when running where there is no display/GPU — the real
    /// Skia render path hangs there. Leave it unset on CI / a real desktop so the render-capture smoke
    /// tests get a real frame. See <see cref="UseNullDrawing"/>.
    /// </summary>
    public const string NullDrawingVariable = "CELLESTRA_HEADLESS_NULL_DRAWING";

    /// <summary>
    /// Whether the tests are running with null drawing (no real frames). True when
    /// <see cref="NullDrawingVariable"/> is set; render-capture tests skip themselves in this mode
    /// (see <c>SkiaFactAttribute</c>).
    /// </summary>
    public static bool UseNullDrawing { get; } =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(NullDrawingVariable));

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            // Real Skia (UseHeadlessDrawing=false) runs the actual render path so
            // CaptureRenderedFrame() returns a real frame — but it requires a working display/GPU and
            // hangs in a headless sandbox without one. There, set CELLESTRA_HEADLESS_NULL_DRAWING to
            // switch to null drawing (fast, no hang); render-capture tests skip themselves under it.
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = UseNullDrawing });
}
