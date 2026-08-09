using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Pivotask.Desktop.Services;
using Pivotask.Desktop.Views;
using Xunit;

namespace Pivotask.UiTests;

/// <summary>
/// Where the menus live. macOS keeps an app's menus in the system bar at the top of the screen; Pivotask
/// was the last of the five still carrying its own bar inside the window on every platform, and a task
/// board has no reason to be the odd one out. The two bars are built from the same commands, so what is
/// tested here is the choice, not the contents.
/// </summary>
public class MenuBarTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pivotask-menu-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    private MainWindow Open()
    {
        var window = new MainWindow(new SettingsService(_path));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>
    /// One bar, not two: whichever the platform gets, the other must not also be there. An in-window menu
    /// on macOS would duplicate every command a few pixels below the system bar that already has them.
    /// </summary>
    [AvaloniaFact]
    public void The_menu_goes_to_the_platforms_own_menu_bar()
    {
        var window = Open();
        var inWindow = window.GetLogicalDescendants().OfType<Menu>().ToList();

        if (OperatingSystem.IsMacOS())
        {
            Assert.Empty(inWindow);
            var native = NativeMenu.GetMenu(window);
            Assert.NotNull(native);
            Assert.Equal(["File", "Edit", "View", "Help"], native.Items.OfType<NativeMenuItem>().Select(i => i.Header));
        }
        else
        {
            Assert.Null(NativeMenu.GetMenu(window));
            var menu = Assert.Single(inWindow);
            Assert.Equal(["_File", "_Edit", "_View", "_Help"], menu.Items.OfType<MenuItem>().Select(i => (string?)i.Header));
        }
    }

    /// <summary>
    /// About is reachable on every platform. On macOS it is in the application menu <em>and</em> in Help,
    /// which is where Mac apps put it; elsewhere Help is the only place there is.
    /// </summary>
    [AvaloniaFact]
    public void About_is_in_the_help_menu()
    {
        var window = Open();

        var headers = OperatingSystem.IsMacOS()
            ? NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Last().Menu!.Items
                .OfType<NativeMenuItem>().Select(i => i.Header)
            : window.GetLogicalDescendants().OfType<Menu>().Single().Items.OfType<MenuItem>().Last().Items
                .OfType<MenuItem>().Select(i => (string?)i.Header);

        Assert.Contains(headers, h => h?.Contains("About Pivotask", StringComparison.Ordinal) == true);
    }
}
