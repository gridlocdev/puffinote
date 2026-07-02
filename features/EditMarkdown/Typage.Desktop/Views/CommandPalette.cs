using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Puffinote.Components.Controls;
using Puffinote.Components.Theming;
using Puffinote.Components.Commands;

namespace Typage.Desktop.Views;

/// <summary>
/// The Command Palette (⌘K) — a VS Code / Cellestra-style overlay that runs any command in the one
/// catalog by name. A full-window dim overlay hosting the shared <see cref="PaletteListView"/>;
/// collapsed until opened, clicking the backdrop or pressing Esc dismisses it.
/// </summary>
public sealed class CommandPalette : Panel
{
    private readonly PaletteListView _palette = new() { Placeholder = "Type a command…" };

    public CommandPalette()
    {
        IsVisible = false;
        Background = Tokens.Dark.Scrim; // dim backdrop (Typage's shell is dark)

        var card = new Border
        {
            Width = 560,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 90, 0, 0),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            Background = Tokens.Dark.Surface,
            BorderBrush = Tokens.Dark.SurfaceBorder,
            BorderThickness = new Thickness(1),
            Child = _palette, // the shared list view traps Tab inside itself
        };

        Children.Add(card);

        // Clicking the dim backdrop (but not the card) closes the palette.
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Visual v && !card.IsVisualAncestorOf(v))
            {
                Close();
            }
        }, handledEventsToo: true);

        _palette.Cancelled += (_, _) => Close();
        _palette.Chosen += entry => { Close(); entry.Run(); };
    }

    /// <summary>Open the palette over the given (enabled) commands and focus the search box.</summary>
    public void Open(IReadOnlyList<AppCommand> commands)
    {
        _palette.SetItems(commands
            .Where(c => c.IsEnabled)
            .Select(c => new PaletteEntry(c.Title, c.Execute, c.Description, c.ShortcutText))
            .ToList());
        IsVisible = true;
        _palette.SearchBox.Focus();
    }

    public void Close() => IsVisible = false;
}
