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
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
