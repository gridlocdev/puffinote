using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BirdBrain.Components.Overlays;

/// <summary>
/// A yes/no confirmation rendered as an internal modal (Nielsen heuristic #5 — error prevention).
/// Its <see cref="ModalDialog{T}.Result"/> is <c>true</c> when the user confirms, <c>false</c> on
/// deny / Esc / backdrop. Use an empty <paramref name="heading"/> for a title-less prompt.
/// </summary>
/// <param name="heading">The modal heading; pass <c>""</c> for none.</param>
/// <param name="message">The body text.</param>
/// <param name="confirmLabel">The confirming button's label.</param>
/// <param name="denyLabel">The denying / cancelling button's label.</param>
/// <param name="confirmIsDefault">
/// Whether the confirm button is the default (Enter). Leave <c>false</c> for destructive actions so
/// Enter doesn't trigger them.
/// </param>
public sealed class ConfirmDialog : ModalDialog<bool>
{
    public ConfirmDialog(
        string heading,
        string message,
        string confirmLabel = "OK",
        string denyLabel = "Cancel",
        bool confirmIsDefault = true)
    {
        Title = heading;

        var confirm = new Button { Content = confirmLabel, IsDefault = confirmIsDefault };
        confirm.Click += (_, _) => Close(true);
        var deny = new Button { Content = denyLabel, IsCancel = true };
        deny.Click += (_, _) => Close(false);

        DialogContent = new StackPanel
        {
            Width = 360,
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { deny, confirm },
                },
            },
        };
    }
}
