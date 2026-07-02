using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Puffinote.Components.Overlays;

/// <summary>
/// A simple internal modal that states something and offers a single dismiss button (Nielsen heuristic
/// #9 — help users recognize, diagnose, and recover from errors). An optional <paramref name="detail"/>
/// adds a second line beneath the message. The bool result is unused.
/// </summary>
public sealed class MessageDialog : ModalDialog<bool>
{
    public MessageDialog(string heading, string message, string? detail = null, string okLabel = "OK")
    {
        Title = heading;

        var body = new StackPanel { Width = 360, Spacing = 12 };
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(detail))
        {
            body.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap });
        }

        var ok = new Button
        {
            Content = okLabel,
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ok.Click += (_, _) => Close();
        body.Children.Add(ok);

        DialogContent = body;
    }
}
