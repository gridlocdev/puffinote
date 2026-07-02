using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Puffinote.Components.Overlays;

/// <summary>One labelled button in a <see cref="ChoiceDialog{TChoice}"/>, returning <paramref name="Value"/>.</summary>
/// <param name="Label">The button text.</param>
/// <param name="Value">The choice this button resolves the dialog to.</param>
/// <param name="IsDefault">Whether this is the default button (Enter).</param>
/// <param name="IsCancel">Whether this is the cancel button (Esc).</param>
public readonly record struct Choice<TValue>(string Label, TValue Value, bool IsDefault = false, bool IsCancel = false);

/// <summary>
/// A message with a row of labelled buttons, each resolving to a distinct <typeparamref name="TChoice"/>
/// value — the multi-way generalization of <see cref="ConfirmDialog"/> (e.g. Save / Don't Save / Cancel).
/// Esc / backdrop resolve to <c>dismiss</c>, the value standing for "the user didn't choose".
/// </summary>
public sealed class ChoiceDialog<TChoice> : ModalDialog<TChoice>
{
    private readonly TChoice _dismiss;

    public ChoiceDialog(string heading, string message, TChoice dismiss, params Choice<TChoice>[] choices)
    {
        Title = heading;
        _dismiss = dismiss;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        foreach (var choice in choices)
        {
            var button = new Button { Content = choice.Label, IsDefault = choice.IsDefault, IsCancel = choice.IsCancel };
            TChoice value = choice.Value;
            button.Click += (_, _) => Close(value);
            buttons.Children.Add(button);
        }

        DialogContent = new StackPanel
        {
            Width = 400,
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                buttons,
            },
        };
    }

    protected override TChoice? CancelResult => _dismiss;
}
