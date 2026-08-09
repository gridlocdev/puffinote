using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Theming;

namespace Pivotask.Control.Views;

/// <summary>
/// The prototype's <c>.ghost</c>: the dashed outline at the end of the board and the foot of the list
/// that adds a group.
///
/// <para>It is deliberately not a filled button. A solid control at the end of a row of columns reads as
/// a fifth column — or, worse, as the primary thing to do on the screen — when what it actually marks is
/// the empty space <em>after</em> the board, and the dashes are how a placeholder says so. Being the
/// width of a column stub rather than the width of its own label is the other half of that: a button
/// that shrink-wraps two words looks like a control that wandered in, not like the next slot along.</para>
/// </summary>
internal static class GhostButton
{
    /// <summary>The prototype's <c>flex: 0 0 200px</c> — wide enough to read as the next slot along.</summary>
    public const double Width = 200;

    public static Button Build(string text, Action click)
    {
        var palette = Tokens.Active;

        var face = new DockPanel { LastChildFill = true, Margin = new Thickness(14, 11) };
        var icon = new LucideIcon
        {
            IconKey = "plus",
            Size = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        };
        DockPanel.SetDock(icon, Dock.Left);
        face.Children.Add(icon);
        face.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // Avalonia's Border draws solid edges only, so the dashes come from a Rectangle stretched behind
        // the label rather than from the button's own BorderThickness — which is also why the button
        // carries no padding of its own and the face brings its own margin.
        var outline = new Rectangle
        {
            Stroke = palette.SurfaceBorder,
            StrokeThickness = 1,
            StrokeDashArray = [4, 3],
            RadiusX = 8,
            RadiusY = 8,
        };

        var button = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = palette.MutedText,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Content = new Panel { Children = { outline, face } },
        };
        Avalonia.Automation.AutomationProperties.SetName(button, text);
        button.Click += (_, _) => click();
        return button;
    }
}
