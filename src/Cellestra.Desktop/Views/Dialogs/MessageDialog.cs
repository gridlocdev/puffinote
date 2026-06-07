using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// A simple modal that states a problem and what to do about it (Nielsen heuristic #9 — help
/// users recognize, diagnose, and recover from errors). Used in place of letting an operation
/// throw: a heading in plain language, an explanatory body, and a single dismiss button.
/// </summary>
public sealed class MessageDialog : Window
{
    public MessageDialog(string heading, string message)
    {
        Title = heading;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var ok = new Button
        {
            Content = "OK",
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ok.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, FontSize = 14 },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                ok,
            },
        };
    }
}
