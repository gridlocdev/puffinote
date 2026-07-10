using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls.Icons;

namespace Puffinote.Components.Controls.CommandBar;

/// <summary>
/// A single primary icon tool for an adaptive command bar, bound to a shared <see cref="AppCommand"/>: it
/// shows the command's Lucide icon, reflects enabled/disabled state, and lights up (emerald wash + a dot)
/// when the command's toggle is on. Deliberately not focusable — a toolbar must never steal keyboard focus
/// from the editor. Reused by both the spreadsheet and the note-editor bars.
/// </summary>
public sealed class CommandTool : Button
{
    private readonly AppCommand _command;
    private readonly LucideIcon _icon;
    private readonly Ellipse _dot;
    private bool _on;
    private bool _hover;

    /// <summary>
    /// Build a tool for <paramref name="command"/>. <paramref name="onExecuted"/> runs after a successful
    /// execute (e.g. to narrate a status line); <paramref name="onClicked"/> runs on every click regardless
    /// (e.g. to dismiss the overflow drawer the tool sits in).
    /// </summary>
    public CommandTool(AppCommand command, Action<AppCommand>? onExecuted = null, Action? onClicked = null)
    {
        _command = command;
        Background = Brushes.Transparent;
        BorderThickness = default;
        Padding = new Thickness(7);
        Width = 34;
        Height = 34;
        CornerRadius = new CornerRadius(6);
        Margin = new Thickness(1, 0);
        Focusable = false; // never pull focus off the editor — commands stay reachable via menu, ⌘K, shortcuts

        _icon = new LucideIcon
        {
            IconKey = command.Icon,
            Size = 17,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _dot = new Ellipse
        {
            Width = 4,
            Height = 4,
            Fill = CommandBarChrome.ActiveForeground,
            IsVisible = false,
            Margin = new Thickness(0, 0, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        Content = new Panel { Children = { _icon, _dot } };

        ToolTip.SetTip(this, new TooltipCard(command.Title, command.ShortcutText, command.Description));
        PointerEntered += (_, _) =>
        {
            _hover = true;
            ApplyBackground();
        };
        PointerExited += (_, _) =>
        {
            _hover = false;
            ApplyBackground();
        };
        Click += (_, _) =>
        {
            if (_command.IsEnabled)
            {
                _command.Execute();
                onExecuted?.Invoke(_command);
            }

            onClicked?.Invoke();
        };
    }

    /// <summary>Re-evaluate the tool's enabled and toggle state (the bar calls this on selection changes).</summary>
    public void UpdateState()
    {
        IsEnabled = _command.IsEnabled;
        _on = _command.IsToggleActive;
        _dot.IsVisible = _on;
        ApplyBackground();
        if (_on)
        {
            _icon.Foreground = CommandBarChrome.ActiveForeground;
        }
        else
        {
            _icon.ClearValue(LucideIcon.ForegroundProperty);
        }
    }

    // "On" wins over hover so a lit toggle keeps its emerald wash; hover supplies a subtle highlight otherwise.
    private void ApplyBackground() =>
        Background = _on ? CommandBarChrome.ActiveBackground : _hover ? CommandBarChrome.HoverBackground : Brushes.Transparent;
}
