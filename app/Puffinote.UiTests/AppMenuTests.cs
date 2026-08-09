using Avalonia.Headless.XUnit;
using Puffinote.Components.Overlays;
using Puffinote.Components.Shell;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The macOS application menu — the bold one next to the Apple menu. Left alone, Avalonia.Native fills it
/// with its own "About Avalonia" item: the first menu a Mac user reads names the toolkit, and the About
/// screen behind it describes the toolkit too. Cellestra had already fixed this for itself; the other four
/// hadn't, which is what docs/TODO called out.
/// </summary>
public sealed class AppMenuTests
{
    /// <summary>
    /// The app's name is what macOS titles that menu with, and an app that leaves it unset gets its
    /// assembly name in the menu bar.
    /// </summary>
    [AvaloniaFact]
    public void The_application_menu_is_named_after_the_app_and_offers_its_own_About()
    {
        var app = Avalonia.Application.Current!;

        MacAppMenu.Install(app, "Puffinote", () => { });

        Assert.Equal("Puffinote", app.Name);
        Assert.Equal(["About Puffinote"], MacAppMenu.HeadersForTest(app));
    }

    /// <summary>Installing again replaces the menu rather than stacking a second About onto it.</summary>
    [AvaloniaFact]
    public void Installing_twice_leaves_one_About_item()
    {
        var app = Avalonia.Application.Current!;

        MacAppMenu.Install(app, "Puffinote", () => { });
        MacAppMenu.Install(app, "Puffinote", () => { });

        Assert.Single(MacAppMenu.HeadersForTest(app));
    }

    /// <summary>
    /// The shared About card, with the app's own strings in it. The version comes from the entry assembly
    /// — this type lives in a shared library, and that library's version is not what anyone means by
    /// "what version am I running?".
    /// </summary>
    [AvaloniaFact]
    public void The_about_card_names_the_app_not_the_toolkit()
    {
        var about = new AboutDialog("Puffinote", "A notebook-style workspace.", subtitle: "from puffin + note");

        Assert.Equal("About Puffinote", about.Title);
        Assert.DoesNotContain("Avalonia", about.Title, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(AboutDialog.Version));
    }
}
