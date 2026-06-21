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

        // The title is just the document name now (#15): a new workbook shows "Untitled".
        Assert.Equal("Untitled", window.Title);
    }
}
