using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BirdBrain.Components.Overlays;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// A simple internal modal that states a problem and what to do about it (Nielsen heuristic #9 —
/// help users recognize, diagnose, and recover from errors). Used in place of letting an operation
/// throw: a plain-language body and a single dismiss button. The bool result is unused.
/// </summary>
public sealed class MessageDialog : ModalDialog<bool>
{
    public MessageDialog(string heading, string message)
    {
        Title = heading;

        var ok = new Button
        {
            Content = "OK",
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ok.Click += (_, _) => Close();

        DialogContent = new StackPanel
        {
            Width = 360,
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                ok,
            },
        };
    }
}
