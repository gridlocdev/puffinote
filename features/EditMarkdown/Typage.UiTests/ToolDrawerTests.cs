using Avalonia.Headless.XUnit;
using Typage.Desktop.Services;
using Typage.Desktop.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The toolbar exposes Cellestra-style popout groups (Text / Heading / List / Insert), each a button
/// whose flyout holds a rich drawer with one row per command.
/// </summary>
public class ToolDrawerTests
{
    [AvaloniaFact]
    public void Toolbar_HasTheFourPopoutGroups()
    {
        var window = new MainWindow(new SettingsService());
        Assert.Equal(4, window.ToolGroupCountForTest());
    }

    [AvaloniaFact]
    public void ADrawer_BuildsOneRowPerCommand()
    {
        var window = new MainWindow(new SettingsService());
        Assert.Equal(5, window.DrawerRowCountForTest(
            "format.bold", "format.italic", "format.strike", "format.highlight", "format.code"));
    }

    [AvaloniaFact]
    public void ADrawer_SkipsUnknownCommandIds()
    {
        var window = new MainWindow(new SettingsService());
        Assert.Equal(1, window.DrawerRowCountForTest("format.bold", "does.not.exist"));
    }
}
