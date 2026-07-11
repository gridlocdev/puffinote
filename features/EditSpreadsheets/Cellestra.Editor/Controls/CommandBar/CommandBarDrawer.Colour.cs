using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Controls.Icons;
using ArgbColor = Cellestra.Core.Styling.ArgbColor;
using CellStyle = Cellestra.Core.Styling.CellStyle;
using Fill = Cellestra.Core.Styling.Fill;
using Font = Cellestra.Core.Styling.Font;

namespace Cellestra.Desktop.Controls.CommandBar;

public static partial class CommandBarDrawer
{
    // ---- Text-group extra: colour swatches --------------------------------

    private static readonly string[] TextColours = ["#23211C", "#15734F", "#B9742A", "#B23A35", "#2D5FA8", "#928C7E"];
    private static readonly string[] FillColours = ["#DCEBE2", "#F3E3CD", "#F6D9D6", "#DDE3F0", "#ECE7DA", "#FFFFFF"];

    private static Control SwatchSection(string title, string[] colours, IAppEnvironment env, Action close, bool fill)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };

        // "Automatic" resets the colour to the default (black text / no fill) — clears any custom choice.
        Func<CellStyle, CellStyle> reset = fill
            ? s => s.WithFill(Fill.None)
            : s => s.WithFont(s.Font with { Color = Font.Default.Color });
        row.Children.Add(ResetSwatch(fill ? "No fill (automatic)" : "Automatic colour", reset, env, close, fill));

        foreach (string hex in colours)
        {
            Func<CellStyle, CellStyle> transform = fill
                ? s => s.WithFill(Fill.Solid(ArgbColor.FromHex(hex)))
                : s => s.WithFont(s.Font with { Color = ArgbColor.FromHex(hex) });

            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 7, 7),
                Background = new SolidColorBrush(ParseHex(hex)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            ToolTip.SetTip(swatch, hex);
            Hook(swatch, env, transform, fill ? "Cell fill" : "Text colour", close);
            row.Children.Add(swatch);
        }

        // "Custom…" opens the first-party Avalonia colour picker for any colour at all.
        row.Children.Add(CustomColourButton(env, close, fill));

        return Section(title, row);
    }

    /// <summary>The "Automatic" reset chip — a checkerboard-ish swatch that clears the custom colour.</summary>
    private static Control ResetSwatch(string tip, Func<CellStyle, CellStyle> reset, IAppEnvironment env, Action close, bool fill)
    {
        var swatch = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 7, 7),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            // A red diagonal slash reads universally as "none / clear".
            Child = new Avalonia.Controls.Shapes.Line
            {
                StartPoint = new Point(3, 21),
                EndPoint = new Point(21, 3),
                Stroke = Brushes.Red,
                StrokeThickness = 2,
            },
        };
        ToolTip.SetTip(swatch, tip);
        Hook(swatch, env, reset, fill ? "Cell fill" : "Text colour", close);
        return swatch;
    }

    private static Control CustomColourButton(IAppEnvironment env, Action close, bool fill)
    {
        var button = new Button
        {
            Content = new LucideIcon
            {
                IconKey = "pipette",
                Size = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = ChipBrush,
            BorderThickness = default,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 7, 7),
        };
        ToolTip.SetTip(button, "Pick any colour…");
        Avalonia.Automation.AutomationProperties.SetName(button, "Pick a custom colour");
        CommandBarChrome.AddHover(button, ChipBrush);

        // A live ColorView (first-party Avalonia control, Golden Rule #6) plus an Apply button.
        var picker = new ColorView
        {
            Color = ToAvalonia(fill
                ? ActiveStyle(env).Fill.Background ?? ArgbColor.White
                : DefaultedTextColour(env)),
            Width = 280,
        };

        var apply = new Button
        {
            Content = "Apply",
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var flyout = new Flyout
        {
            Placement = PlacementMode.Bottom,
            Content = new StackPanel { Children = { picker, apply } },
        };
        apply.Click += (_, _) =>
        {
            var chosen = FromAvalonia(picker.Color);
            Func<CellStyle, CellStyle> transform = fill
                ? s => s.WithFill(Fill.Solid(chosen))
                : s => s.WithFont(s.Font with { Color = chosen });
            env.ApplyCellStyle(transform, fill ? "Cell fill" : "Text colour");
            env.Narrate($"{(fill ? "Cell fill" : "Text colour")} {chosen.ToHex()} · {env.Selection.ActiveAddress}");
            flyout.Hide();
            close();
        };
        // Re-seed the picker from the current style each time it opens.
        flyout.Opened += (_, _) => picker.Color = ToAvalonia(fill
            ? ActiveStyle(env).Fill.Background ?? ArgbColor.White
            : DefaultedTextColour(env));
        button.Flyout = flyout;
        return button;
    }
}
