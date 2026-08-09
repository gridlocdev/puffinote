using Avalonia;
using Avalonia.Controls;
using Puffinote.Components.Commands;

namespace Puffinote.Components.Shell;

/// <summary>
/// The bold macOS application menu — the one next to the Apple menu, titled with the app's name.
/// <para>
/// Left alone, Avalonia.Native fills it with its own "About Avalonia" item, which opens Avalonia's about
/// box: the first menu a Mac user reads names the toolkit rather than the app. Setting <em>any</em>
/// <see cref="NativeMenu"/> on the <see cref="Application"/> replaces those built-ins with ours; Avalonia
/// still appends the standard Services / Hide / Quit block underneath, so nothing standard is lost.
/// </para>
/// <para>
/// <b>Timing matters.</b> This has to run during <c>Application.Initialize</c>, before Avalonia.Native's
/// <c>AfterSetup</c> builds the application-menu exporter — that exporter snapshots the menu once and does
/// not observe later changes. The window does not exist yet at that point, which is why the About action
/// is a callback resolved at click time rather than a reference to anything.
/// </para>
/// </summary>
public static class MacAppMenu
{
    /// <summary>
    /// Give <paramref name="app"/> an application menu named <paramref name="appName"/> whose About item
    /// runs <paramref name="showAbout"/>. Also sets <see cref="Application.Name"/>, which is what macOS
    /// titles the menu with — an app that leaves it unset gets its assembly name in the menu bar.
    /// </summary>
    public static void Install(Application app, string appName, Action showAbout)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(showAbout);

        app.Name = appName;

        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem($"About {appName}") { Command = new RelayCommand(showAbout) });
        NativeMenu.SetMenu(app, menu);
    }

    /// <summary>Test seam: the application menu's item headers, in order (empty when none is installed).</summary>
    public static IReadOnlyList<string> HeadersForTest(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return NativeMenu.GetMenu(app) is { } menu
            ? [.. menu.Items.OfType<NativeMenuItem>().Select(i => i.Header ?? string.Empty)]
            : [];
    }
}
