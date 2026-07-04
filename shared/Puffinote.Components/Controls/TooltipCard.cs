using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Theming;

namespace Puffinote.Components.Controls;

/// <summary>
/// A small tooltip body the shell floats over a control: a title with an optional shortcut beside it,
/// and an optional wrapped description beneath. The secondary text (shortcut + description) reads the
/// theme's <see cref="ThemePalette.MutedText"/> token, resolved when the tooltip popup mounts. Apps
/// adapt their own command type to the three strings; the card stays domain-agnostic.
/// </summary>
public sealed class TooltipCard : StackPanel
{
    private readonly List<TextBlock> _muted = [];

    public TooltipCard(string title, string? shortcut = null, string? description = null)
    {
        Spacing = 2;
        MaxWidth = 240;

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        if (!string.IsNullOrEmpty(shortcut))
        {
            var shortcutText = new TextBlock { Text = shortcut };
            _muted.Add(shortcutText);
            header.Children.Add(shortcutText);
        }

        Children.Add(header);
        if (!string.IsNullOrEmpty(description))
        {
            var descriptionText = new TextBlock { Text = description, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            _muted.Add(descriptionText);
            Children.Add(descriptionText);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Resolve muted text once the card mounts (in a tooltip popup), when ActualThemeVariant is valid.
        IBrush muted = Tokens.For(ActualThemeVariant).MutedText;
        foreach (var block in _muted)
        {
            block.Foreground = muted;
        }
    }
}
