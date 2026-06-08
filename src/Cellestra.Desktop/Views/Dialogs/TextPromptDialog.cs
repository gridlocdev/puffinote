using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// A modal single-line text prompt (e.g. renaming a sheet). Returns the trimmed entry from
/// <see cref="Window.ShowDialog{TResult}"/> on OK/Enter, or null on cancel/close.
/// </summary>
public sealed class TextPromptDialog : Window
{
    private readonly TextBox _input;

    public TextPromptDialog(string heading, string label, string initial = "")
    {
        Title = heading;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _input = new TextBox { Text = initial, Width = 220 };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
            }
        };

        var ok = new Button { Content = "OK", IsDefault = true };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, FontSize = 14 },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, _input },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        };

        Opened += (_, _) => _input.Focus();
    }

    private void Accept()
    {
        string text = (_input.Text ?? string.Empty).Trim();
        Close(text.Length == 0 ? null : text);
    }
}
