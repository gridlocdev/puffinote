using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Views.Overlays;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// A yes/no confirmation rendered as an internal modal (Nielsen heuristic #5 — error prevention).
/// Its <see cref="ModalDialog{T}.Result"/> is <c>true</c> when the user confirms, <c>false</c> on
/// cancel/Esc/backdrop. Used for destructive actions like deleting a sheet (PRD §8.7).
/// </summary>
public sealed class ConfirmDialog : ModalDialog<bool>
{
    public ConfirmDialog(string heading, string message, string confirmLabel = "OK")
    {
        Title = heading;

        var confirm = new Button { Content = confirmLabel, IsDefault = true };
        confirm.Click += (_, _) => Close(true);
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(false);

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
                    Children = { cancel, confirm },
                },
            },
        };
    }
}
