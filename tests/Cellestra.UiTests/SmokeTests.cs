using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class SmokeTests
{
    [AvaloniaFact]
    public void MainWindow_constructs_on_headless_platform()
    {
        var window = new MainWindow();
        window.Show();

        Assert.Equal("Cellestra", window.Title);
    }
}
