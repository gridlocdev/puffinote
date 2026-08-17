using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;
using VAlign = Cellestra.Core.Styling.VerticalAlignment;

namespace Cellestra.UiTests;

/// <summary>
/// Vertical alignment as ordinary catalog commands (PRD §10.1). It used to exist only as three
/// bespoke buttons inside the command bar's Align drawer, which is why it was missing from the
/// Command Menu, the menu bar, the radial, and the shortcut reference — every surface reads the
/// catalog, and the catalog didn't have it.
/// </summary>
public class VerticalAlignmentCommandTests
{
    private static readonly AppCommands Catalog = FakeAppEnvironment.Catalog;

    [Fact]
    public void The_catalog_carries_both_axes()
    {
        foreach (string id in new[]
        {
            "format.alignLeft", "format.alignCenter", "format.alignRight",
            "format.alignTop", "format.alignMiddle", "format.alignBottom",
        })
        {
            Assert.NotNull(Catalog.ById(id));
        }
    }

    [Fact]
    public void Every_alignment_command_names_its_axis()
    {
        // "Align Center" alone is two commands in a searchable list; the axis is what tells them apart.
        Assert.Equal("Align Left (Horizontal)", Catalog.ById("format.alignLeft")!.Title);
        Assert.Equal("Align Center (Horizontal)", Catalog.ById("format.alignCenter")!.Title);
        Assert.Equal("Align Right (Horizontal)", Catalog.ById("format.alignRight")!.Title);
        Assert.Equal("Align Top (Vertical)", Catalog.ById("format.alignTop")!.Title);
        Assert.Equal("Align Middle (Vertical)", Catalog.ById("format.alignMiddle")!.Title);
        Assert.Equal("Align Bottom (Vertical)", Catalog.ById("format.alignBottom")!.Title);
    }

    [AvaloniaFact]
    public void The_command_menu_offers_the_vertical_alignments()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        env.Selection.MoveTo(0, 0);

        var titles = PaletteCatalogTitles(env);

        Assert.Contains("Align Top (Vertical)", titles);
        Assert.Contains("Align Middle (Vertical)", titles);
        Assert.Contains("Align Bottom (Vertical)", titles);
    }

    [AvaloniaTheory]
    [InlineData("format.alignTop", VAlign.Top)]
    [InlineData("format.alignMiddle", VAlign.Middle)]
    [InlineData("format.alignBottom", VAlign.Bottom)]
    public void Running_one_sets_the_selection_and_lights_up(string id, VAlign expected)
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        env.Selection.SetRange(0, 0, 1, 1); // A1:B2

        var command = env.Commands.ById(id)!;
        command.Execute();

        var style = env.Session.Workbook.Styles.Get(sheet.EffectiveStyleId(1, 1));
        Assert.Equal(expected, style.Alignment.Vertical);
        Assert.True(command.IsToggleActive);
    }

    [AvaloniaFact]
    public void The_two_axes_are_independent()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        env.Selection.MoveTo(0, 0);

        env.Commands.ById("format.alignRight")!.Execute();
        env.Commands.ById("format.alignBottom")!.Execute();

        var style = env.Session.Workbook.Styles.Get(sheet.EffectiveStyleId(0, 0));
        Assert.Equal(Cellestra.Core.Styling.HorizontalAlignment.Right, style.Alignment.Horizontal);
        Assert.Equal(VAlign.Bottom, style.Alignment.Vertical);
    }

    /// <summary>The titles the Command Menu would list right now (it reads the catalog, enabled-only).</summary>
    private static IReadOnlyList<string> PaletteCatalogTitles(IAppEnvironment env) =>
        [.. env.Commands.All.Where(c => c.IsEnabled).Select(c => c.Title)];
}
