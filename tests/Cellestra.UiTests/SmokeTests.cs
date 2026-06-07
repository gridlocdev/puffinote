using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class SmokeTests
{
    [AvaloniaFact]
    public void MainWindow_constructs_and_shows()
    {
        var window = new MainWindow();
        window.Show();

        Assert.Contains("Cellestra", window.Title);
    }
}
