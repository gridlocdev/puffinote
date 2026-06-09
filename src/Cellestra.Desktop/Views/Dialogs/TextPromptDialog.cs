using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Cellestra.Desktop.Views.Overlays;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// A single-line text prompt rendered as an internal modal (e.g. renaming a sheet). Its
/// <see cref="ModalDialog{T}.Result"/> is the trimmed entry on OK/Enter, or null on cancel/Esc.
/// </summary>
public sealed class TextPromptDialog : ModalDialog<string?>
{
    private readonly TextBox _input;

    public TextPromptDialog(string heading, string label, string initial = "")
    {
        Title = heading;

        _input = new TextBox { Text = initial, Width = 220 };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
            }
        };
        InitialFocus = _input;

        var ok = new Button { Content = "OK", IsDefault = true };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        DialogContent = new StackPanel
        {
            Spacing = 12,
            Children =
            {
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
    }

    private void Accept()
    {
        string text = (_input.Text ?? string.Empty).Trim();
        Close(text.Length == 0 ? null : text);
    }
}
