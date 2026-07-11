using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Puffinote.Components.Controls.CommandBar;

namespace Cellestra.Desktop.Controls.CommandBar;

public static partial class CommandBarDrawer
{
    // ---- Text-group extra: size stepper -----------------------------------

    private static readonly double[] FontSizes = [8, 9, 10, 11, 12, 14, 18, 24, 36];

    /// <summary>A size stepper: −/+ around the current size, plus a row of common presets.</summary>
    private static Control SizeSection(IAppEnvironment env, Action close)
    {
        var value = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            MinWidth = 30,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        void Apply(double size)
        {
            size = Math.Clamp(size, 6, 96);
            env.ApplyCellStyle(s => s.WithFont(s.Font with { SizePoints = size }), $"Font size {size:0}");
            env.Narrate($"Font size {size:0} · {env.Selection.ActiveAddress}");
            value.Text = size.ToString("0", CultureInfo.InvariantCulture);
        }

        var minus = StepButton("−", () => Apply(CurrentSize(env) - 1));
        var plus = StepButton("+", () => Apply(CurrentSize(env) + 1));
        value.Text = CurrentSize(env).ToString("0", CultureInfo.InvariantCulture);

        var stepper = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { minus, value, plus },
        };

        var presets = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
        foreach (double px in FontSizes)
        {
            var button = new Button
            {
                Content = px.ToString(CultureInfo.InvariantCulture),
                Background = ChipBrush,
                BorderThickness = default,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 3),
                Margin = new Thickness(0, 0, 5, 5),
                FontSize = 11.5,
            };
            CommandBarChrome.AddHover(button, ChipBrush);
            // Preview on hover, commit on click — same feel as the colour swatches.
            button.PointerEntered += (_, _) => env.PreviewCellStyle(s => s.WithFont(s.Font with { SizePoints = px }));
            button.PointerExited += (_, _) => env.PreviewCellStyle(null);
            button.Click += (_, _) =>
            {
                Apply(px);
                close();
            };
            presets.Children.Add(button);
        }

        var content = new StackPanel { Children = { stepper, presets } };
        return Section("Font size", content);
    }

    private static double CurrentSize(IAppEnvironment env) => ActiveStyle(env).Font.SizePoints;

    private static Button StepButton(string glyph, Action onClick)
    {
        var button = new Button
        {
            Content = glyph,
            Width = 28,
            Height = 26,
            Background = ChipBrush,
            BorderThickness = default,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0),
            FontSize = 15,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        CommandBarChrome.AddHover(button, ChipBrush);
        button.Click += (_, _) => onClick();
        return button;
    }
}
