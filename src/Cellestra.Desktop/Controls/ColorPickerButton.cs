using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Cellestra.Core.Styling;
using AvColor = Avalonia.Media.Color;
using Border = Avalonia.Controls.Border;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using Orientation = Avalonia.Layout.Orientation;
using VAlign = Avalonia.Layout.VerticalAlignment;

namespace Cellestra.Desktop.Controls;

/// <summary>
/// A friendlier alternative to a hex text box for choosing a color: a button showing the current
/// swatch that opens a flyout with a preset palette, an optional "None" choice (for fill colors),
/// and a hex field for a custom value. Kept dependency-free (no third-party color-picker package,
/// Golden Rule #6) while still being point-and-click.
/// </summary>
public sealed class ColorPickerButton : Button
{
    // A small, evenly spaced palette: a grayscale row followed by common hues.
    private static readonly string[] Palette =
    {
        "#000000", "#444444", "#666666", "#999999", "#BBBBBB", "#DDDDDD", "#EEEEEE", "#FFFFFF",
        "#E53935", "#FB8C00", "#FDD835", "#43A047", "#1E88E5", "#3949AB", "#8E24AA", "#6D4C41",
        "#FFCDD2", "#FFE0B2", "#FFF9C4", "#C8E6C9", "#BBDEFB", "#C5CAE9", "#E1BEE7", "#D7CCC8",
        "#B71C1C", "#E65100", "#F9A825", "#1B5E20", "#0D47A1", "#1A237E", "#4A148C", "#3E2723",
    };

    private readonly bool _allowNone;
    private readonly Border _swatch;
    private readonly TextBlock _label;
    private readonly Flyout _flyout = new() { Placement = PlacementMode.BottomEdgeAlignedLeft };
    private ArgbColor? _color;

    public ColorPickerButton(ArgbColor? initial, bool allowNone)
    {
        _allowNone = allowNone;
        _color = initial;

        _swatch = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(3),
            BorderBrush = new SolidColorBrush(AvColor.FromRgb(0x99, 0x99, 0x99)),
            BorderThickness = new Thickness(1),
        };
        _label = new TextBlock { VerticalAlignment = VAlign.Center };

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _swatch, _label, new TextBlock { Text = "▾", VerticalAlignment = VAlign.Center } },
        };
        Width = 130;
        HorizontalContentAlignment = HAlign.Left;

        _flyout.Content = BuildFlyout();
        Flyout = _flyout;
        UpdateSwatch();
    }

    /// <summary>The chosen color, or null for "no color" (only reachable when <c>allowNone</c>).</summary>
    public ArgbColor? Color
    {
        get => _color;
        set
        {
            _color = value;
            UpdateSwatch();
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ColorChanged;

    private Control BuildFlyout()
    {
        var grid = new UniformGrid { Columns = 8, Width = 8 * 24 };
        foreach (var swatchHex in Palette)
        {
            var color = ArgbColor.FromHex(swatchHex);
            var cell = new Border
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                Background = ToBrush(color),
                BorderBrush = new SolidColorBrush(AvColor.FromRgb(0xAA, 0xAA, 0xAA)),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            cell.PointerPressed += (_, _) => Pick(color);
            grid.Children.Add(cell);
        }

        var hexBox = new TextBox { Width = 110, PlaceholderText = "#RRGGBB", Text = _color?.ToHex() ?? string.Empty };
        var apply = new Button { Content = "Set" };
        apply.Click += (_, _) =>
        {
            if (ArgbColor.TryFromHex(hexBox.Text, out var custom))
            {
                Pick(custom);
            }
        };

        var rows = new StackPanel
        {
            Margin = new Thickness(8),
            Spacing = 8,
            Children =
            {
                grid,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { hexBox, apply },
                },
            },
        };

        if (_allowNone)
        {
            var none = new Button { Content = "No fill", HorizontalAlignment = HAlign.Stretch };
            none.Click += (_, _) => Pick(null);
            rows.Children.Insert(1, none);
        }

        return rows;
    }

    private void Pick(ArgbColor? color)
    {
        Color = color;
        _flyout.Hide();
    }

    private void UpdateSwatch()
    {
        if (_color is { } c)
        {
            _swatch.Background = ToBrush(c);
            _label.Text = c.ToHex();
        }
        else
        {
            _swatch.Background = Brushes.Transparent;
            _label.Text = "None";
        }
    }

    private static IBrush ToBrush(ArgbColor c) => new SolidColorBrush(AvColor.FromArgb(0xFF, c.R, c.G, c.B));
}
