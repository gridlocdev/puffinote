using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class RibbonTests
{
    [AvaloniaFact]
    public void Ribbon_is_hidden_by_default_and_toggles()
    {
        var window = new MainWindow();
        window.Show();

        Assert.False(window.IsRibbonVisible); // minimal by default (PRD §6)

        AppCommands.ById("view.showRibbon")!.Execute(window);
        Assert.True(window.IsRibbonVisible);

        AppCommands.ById("view.showRibbon")!.Execute(window);
        Assert.False(window.IsRibbonVisible);
    }

    [AvaloniaFact]
    public void Ribbon_builds_from_the_command_catalog()
    {
        var window = new MainWindow();
        window.Show();
        var ribbon = new Cellestra.Desktop.Controls.Ribbon.Ribbon(window);

        // Constructed without throwing: every referenced command id resolves in the catalog.
        Assert.NotNull(ribbon.Content);
    }
}
