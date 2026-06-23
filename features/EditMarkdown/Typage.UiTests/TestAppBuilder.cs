using Avalonia;
using Avalonia.Headless;
using Typage.Desktop;
using Typage.UiTests;

// One-time registration of the Avalonia headless platform for the whole UI test assembly.
// [AvaloniaFact]/[AvaloniaTheory] tests run on this app.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Typage.UiTests;

public static class TestAppBuilder
{
    // Pure headless drawing (no Skia/GPU). These tests drive the editor through its public API and
    // never capture a rendered frame, so the null drawing backend is correct here — and it avoids the
    // GPU/display the Skia path needs (which a headless CI host lacks). A separate render-capture
    // suite would flip UseHeadlessDrawing to false and add .UseSkia().
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
