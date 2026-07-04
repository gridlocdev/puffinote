using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Commands;
using Puffinote.Components.Commands;
using Xunit;

namespace Cellestra.UiTests;

public class CommandCatalogTests
{
    // The catalog's metadata (ids, titles, shortcuts, icons) is environment-independent, so these build it
    // against a no-op environment (FakeAppEnvironment) instead of a whole MainWindow — fast, and they never
    // invoke a command body. Behaviour is covered by the window-based tests elsewhere.
    private static readonly AppCommands Commands = FakeAppEnvironment.Catalog;

    [Fact]
    public void Catalog_has_unique_ids_and_is_lookup_consistent()
    {
        var ids = Commands.All.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        foreach (var command in Commands.All)
        {
            Assert.Same(command, Commands.ById(command.Id));
        }
    }

    [Fact]
    public void Core_commands_are_present_with_shortcuts()
    {
        Assert.NotNull(Commands.ById("file.save"));
        Assert.NotNull(Commands.ById("edit.undo"));
        Assert.NotNull(Commands.ById("view.commandPalette"));

        // The palette doubles as a way to learn shortcuts (PRD §6.7). The Command Palette takes ⌘K
        // (the bar's "Find a tool…" affordance); the sheet switcher sits on ⇧⌘P. Cellestra is macOS-only,
        // so assert the macOS rendering explicitly (the test host is Linux).
        Assert.Equal("⌘S", ShortcutFormatter.Format(Commands.ById("file.save")!.Shortcut!.Value, mac: true));
        Assert.Equal("⌘K", ShortcutFormatter.Format(Commands.ById("view.commandPalette")!.Shortcut!.Value, mac: true));
        Assert.Equal("⇧⌘P", ShortcutFormatter.Format(Commands.ById("view.goToSheet")!.Shortcut!.Value, mac: true));
    }

    [Fact]
    public void Every_command_has_a_label_action_and_description()
    {
        foreach (var command in Commands.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Title));
            Assert.NotNull(command.Execute);
            // The bar's tooltips and the palette read Description; an empty one is a hole in the UI.
            Assert.False(string.IsNullOrWhiteSpace(command.Description), $"{command.Id} has no description");
        }
    }

    [AvaloniaFact] // LucideIcons.Get resolves an avares:// resource, so it needs the Avalonia app (no window needed).
    public void Every_command_icon_resolves_to_a_vendored_glyph()
    {
        foreach (var command in Commands.All)
        {
            if (command.Icon is null)
            {
                continue;
            }

            Assert.True(Puffinote.Components.Controls.Icons.LucideIcons.Get(command.Icon) is not null,
                $"{command.Id} references missing icon '{command.Icon}'");
        }
    }
}
