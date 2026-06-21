using System.Linq;
using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Commands;
using Xunit;

namespace Cellestra.UiTests;

public class CommandCatalogTests
{
    [Fact]
    public void Catalog_has_unique_ids_and_is_lookup_consistent()
    {
        var ids = AppCommands.All.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        foreach (var command in AppCommands.All)
        {
            Assert.Same(command, AppCommands.ById(command.Id));
        }
    }

    [Fact]
    public void Core_commands_are_present_with_shortcuts()
    {
        Assert.NotNull(AppCommands.ById("file.save"));
        Assert.NotNull(AppCommands.ById("edit.undo"));
        Assert.NotNull(AppCommands.ById("view.commandPalette"));

        // The palette doubles as a way to learn shortcuts (PRD §6.7). The Command Palette takes ⌘K
        // (the bar's "Find a tool…" affordance); the sheet switcher sits on ⇧⌘P.
        Assert.Equal("⌘S", AppCommands.ById("file.save")!.ShortcutText);
        Assert.Equal("⌘K", AppCommands.ById("view.commandPalette")!.ShortcutText);
        Assert.Equal("⇧⌘P", AppCommands.ById("view.goToSheet")!.ShortcutText);
    }

    [Fact]
    public void Every_command_has_a_label_action_and_description()
    {
        foreach (var command in AppCommands.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Label));
            Assert.NotNull(command.Execute);
            // The bar's tooltips and the palette read Description; an empty one is a hole in the UI.
            Assert.False(string.IsNullOrWhiteSpace(command.Description), $"{command.Id} has no description");
        }
    }

    [AvaloniaFact]
    public void Every_command_icon_resolves_to_a_vendored_glyph()
    {
        foreach (var command in AppCommands.All)
        {
            if (command.Icon is null)
            {
                continue;
            }

            Assert.True(Cellestra.Desktop.Controls.Icons.LucideIcons.Get(command.Icon) is not null,
                $"{command.Id} references missing icon '{command.Icon}'");
        }
    }
}
