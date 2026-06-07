using System.Linq;
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

        // The palette doubles as a way to learn shortcuts (PRD §6.7).
        Assert.Equal("⌘S", AppCommands.ById("file.save")!.ShortcutText);
        Assert.Equal("⇧⌘P", AppCommands.ById("view.commandPalette")!.ShortcutText);
    }

    [Fact]
    public void Every_command_has_a_label_and_action()
    {
        foreach (var command in AppCommands.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Label));
            Assert.NotNull(command.Execute);
        }
    }
}
