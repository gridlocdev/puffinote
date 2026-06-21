using Avalonia;

namespace Cellestra.Desktop;

internal static class Program
{
    // Avalonia configuration; the entry point. Keep initialization here minimal
    // and free of any code that touches a window before the app is built.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
