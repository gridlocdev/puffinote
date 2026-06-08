using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// A modal yes/no confirmation (Nielsen heuristic #5 — error prevention). Returns <c>true</c> from
/// <see cref="Window.ShowDialog{TResult}"/> when the user confirms, <c>false</c> on cancel/close.
/// Used for destructive actions like deleting a sheet (PRD §8.7).
/// </summary>
public sealed class ConfirmDialog : Window
{
    public ConfirmDialog(string heading, string message, string confirmLabel = "OK")
    {
        Title = heading;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var confirm = new Button { Content = confirmLabel, IsDefault = true };
        confirm.Click += (_, _) => Close(true);
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(false);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, FontSize = 14 },
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
