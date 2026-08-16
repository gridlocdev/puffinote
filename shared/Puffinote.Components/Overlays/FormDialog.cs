using Avalonia.Controls;
using Avalonia.Layout;

namespace Puffinote.Components.Overlays;

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
            Margin = new Avalonia.Thickness(0, 12, 0, 0),
        };
        if (cancelLabel is not null)
        {
            var cancel = new Button { Content = cancelLabel, IsCancel = true };
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);
        }

        buttons.Children.Add(ok);

        // The buttons are pinned to the foot and the body takes what is left, rather than the two being
        // stacked and the taller one pushing the shorter one off the card. The way out of a dialog is the
        // one thing in it that must never be the part that doesn't fit.
        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(body);

        DialogContent = layout;
    }
}
