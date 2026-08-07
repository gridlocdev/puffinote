using Avalonia;
using Avalonia.Headless;
using Pivotask.Desktop;
using Pivotask.UiTests;

// One-time registration of the Avalonia headless platform for the whole UI test assembly.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Pivotask.UiTests;

public static class TestAppBuilder
{
    // Pure headless drawing (no Skia/GPU). These tests drive the board through its own API and never
    // capture a rendered frame, so the null backend is correct here — and it avoids the display the
    // Skia path needs, which a headless container doesn't have.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
