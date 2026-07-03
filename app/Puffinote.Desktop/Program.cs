using Avalonia;

namespace Puffinote.Desktop;

internal static class Program
{
    // Avalonia configuration and entry point. Keep this minimal and free of any code that
    // touches a window before the app is built.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
