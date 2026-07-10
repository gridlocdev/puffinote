using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using Puffinote.Desktop;
using Puffinote.Desktop.Shell;
using Puffinote.UiTests;
using Xunit;

// One-time registration of the Avalonia headless platform for the whole UI test assembly.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

// All [AvaloniaFact]s share one global Avalonia app + dispatcher, so run them serially rather than letting
// xUnit parallelize test classes across threads — that keeps the shell/overlay/Tokens state race-free.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Puffinote.UiTests;

public static class TestAppBuilder
{
    // Any test that opens a notebook records it as the "last notebook" in per-user app state. Redirect that
    // store to a throwaway temp file for the whole assembly so tests never read or clobber the real user's.
    [ModuleInitializer]
    internal static void RedirectAppState() =>
        AppState.PathOverrideForTest = Path.Combine(Path.GetTempPath(), "puffinote-uitests", "app-state.json");

    // Pure headless drawing (no Skia/GPU). These tests drive the shell through its public API and never
    // capture a rendered frame, so null drawing is correct here — and it avoids the GPU/display the Skia
    // path needs (which a headless CI host lacks). Mirrors Typage.UiTests.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
