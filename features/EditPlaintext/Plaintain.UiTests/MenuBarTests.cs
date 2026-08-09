using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Plaintain.Desktop.Services;
using Plaintain.Desktop.Views;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// Where the menus live. macOS keeps an app's menus in the system bar at the top of the screen, and every
/// other windowed app in this family already puts them there; Plaintain was the one still carrying its own
/// bar inside the window on every platform. The two bars are built from the same commands, so what is
/// tested here is the choice, not the contents.
/// </summary>
public class MenuBarTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"plaintain-menu-{Guid.NewGuid():N}.json");

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
            Assert.Equal(["File", "Edit", "View"], native.Items.OfType<NativeMenuItem>().Select(i => i.Header));
        }
        else
        {
            Assert.Null(NativeMenu.GetMenu(window));
            var menu = Assert.Single(inWindow);
            Assert.Equal(["_File", "_Edit", "_View"], menu.Items.OfType<MenuItem>().Select(i => (string?)i.Header));
        }
    }

    /// <summary>
    /// "Open Recent" is filled from the persisted MRU list whichever bar carries it — the placeholder when
    /// there is nothing to list is the observable both paths share.
    /// </summary>
    [AvaloniaFact]
    public void Open_Recent_starts_out_saying_there_is_nothing_to_open()
    {
        var window = Open();

        IEnumerable<string?> headers = OperatingSystem.IsMacOS()
            ? NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().First(i => i.Header == "File")
                .Menu!.Items.OfType<NativeMenuItem>().First(i => i.Header == "Open Recent")
                .Menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header)
            : window.GetLogicalDescendants().OfType<MenuItem>().First(m => (string?)m.Header == "Open _Recent")
                .Items.OfType<MenuItem>().Select(i => (string?)i.Header);

        Assert.Equal(["(no recent files)"], headers);
    }
}
