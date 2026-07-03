using Avalonia;
using Avalonia.Headless;
using Puffinote.Desktop;
using Puffinote.UiTests;

// One-time registration of the Avalonia headless platform for the whole UI test assembly.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Puffinote.UiTests;

public static class TestAppBuilder
{
    // Pure headless drawing (no Skia/GPU). These tests drive the shell through its public API and never
    // capture a rendered frame, so null drawing is correct here — and it avoids the GPU/display the Skia
    // path needs (which a headless CI host lacks). Mirrors Typage.UiTests.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
