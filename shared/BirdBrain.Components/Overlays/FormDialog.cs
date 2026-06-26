using Avalonia.Controls;
using Avalonia.Layout;

namespace BirdBrain.Components.Overlays;

/// <summary>
/// A general OK/Cancel modal around an arbitrary <paramref name="body"/> (a settings form, a reference
/// panel, …). Its <see cref="ModalDialog{T}.Result"/> is <c>true</c> when the user accepts and
/// <c>false</c> on Cancel / Esc / backdrop. Pass <c>cancelLabel: null</c> for a single-button
/// acknowledge dialog (Esc still dismisses, resolving false).
/// </summary>
public sealed class FormDialog : ModalDialog<bool>
{
    public FormDialog(string heading, Control body, string okLabel = "OK", string? cancelLabel = "Cancel")
    {
        Title = heading;

        var ok = new Button { Content = okLabel, IsDefault = true };
        ok.Click += (_, _) => Close(true);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (cancelLabel is not null)
        {
            var cancel = new Button { Content = cancelLabel, IsCancel = true };
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);
        }

        buttons.Children.Add(ok);

        DialogContent = new StackPanel { Spacing = 12, Children = { body, buttons } };
    }
}
