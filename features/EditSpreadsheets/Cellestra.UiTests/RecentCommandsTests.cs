using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The palette opens on what you were just doing. A session's run commands are lifted to the top under
/// "Recent" — moved, not copied, so no row appears twice — and only while nothing is typed: once there
/// is a query, the ranking is the answer.
/// </summary>
public class RecentCommandsTests
{
    private static MainWindow ShownWindow()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static PaletteListView OpenPalette(MainWindow window)
    {
        ((IAppEnvironment)window.Editor).ShowCommandPalette();
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<OverlayHost>().First()
            .GetVisualDescendants().OfType<PaletteListView>().Last();
    }

    [AvaloniaFact]
    public void A_command_you_ran_is_offered_back_at_the_top()
    {
        var window = ShownWindow();
        Assert.DoesNotContain(PaletteListView.RecentCategory, OpenPalette(window).ListedTitles);
        window.Editor.OverlayHost.Close();

        window.Editor.Commands.ById("format.bold")!.Execute();
        window.Editor.Commands.ById("format.italic")!.Execute();

        var titles = OpenPalette(window).ListedTitles;
        Assert.Equal(PaletteListView.RecentCategory, titles[0]);
        Assert.Equal("Italic", titles[1]); // most recent first
        Assert.Equal("Bold", titles[2]);
    }

    [AvaloniaFact]
    public void A_recent_command_is_moved_to_the_top_rather_than_listed_twice()
    {
        var window = ShownWindow();
        window.Editor.Commands.ById("format.bold")!.Execute();

        var titles = OpenPalette(window).ListedTitles;
        Assert.Single(titles, t => t == "Bold");
    }

    [AvaloniaFact]
    public void Running_a_command_again_moves_it_back_to_the_front()
    {
        var window = ShownWindow();
        window.Editor.Commands.ById("format.bold")!.Execute();
        window.Editor.Commands.ById("format.italic")!.Execute();
        window.Editor.Commands.ById("format.bold")!.Execute();

        var titles = OpenPalette(window).ListedTitles;
        Assert.Equal("Bold", titles[1]);
        Assert.Equal("Italic", titles[2]);
    }

    [AvaloniaFact]
    public void Typing_a_query_drops_the_recent_section()
    {
        var window = ShownWindow();
        window.Editor.Commands.ById("format.bold")!.Execute();

        var palette = OpenPalette(window);
        palette.SearchBox.Text = "bold";
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(PaletteListView.RecentCategory, palette.ListedTitles);
    }

    [AvaloniaFact]
    public void Opening_the_palette_is_not_itself_a_recent_command()
    {
        // Otherwise "Command Palette…" would sit at the top of its own Recent list, every single time.
        var window = ShownWindow();
        OpenPalette(window);
        window.Editor.OverlayHost.Close();

        Assert.Empty(window.Editor.Commands.RecentTitles);
    }
}
