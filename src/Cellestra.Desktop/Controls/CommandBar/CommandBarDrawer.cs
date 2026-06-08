using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.Icons;

namespace Cellestra.Desktop.Controls.CommandBar;

/// <summary>
/// Builds the contents of a group's overflow drawer: the full set of that group's tools, each as a
/// labelled, described row (recognition over recall). Returns the panel plus a refresh action the
/// flyout runs on open so rows reflect the current selection. Enriched with colour swatches and a
/// font-size stepper for the Text group by a later stage.
/// </summary>
public static class CommandBarDrawer
{
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    public static (Control Content, Action Refresh) Build(CommandBarGroup group, IAppEnvironment env, Action close)
    {
        var panel = new StackPanel { Spacing = 3, MinWidth = 248 };
        panel.Children.Add(new TextBlock
        {
            Text = group.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            Margin = new Thickness(2, 0, 0, 8),
        });

        var refreshers = new List<Action>();
        foreach (string id in group.Overflow)
        {
            if (AppCommands.ById(id) is { } command)
            {
                var (row, refresh) = BuildRow(command, env, close);
                panel.Children.Add(row);
                refreshers.Add(refresh);
            }
        }

        if (group.HasStyleDrawer)
        {
            // Font size and colours are parametric, not discrete commands, so they live here as
            // bespoke affordances that apply via ApplyCellStyle and preview live on hover.
            panel.Children.Add(SizeSection(env, close));
            panel.Children.Add(SwatchSection("Text colour", TextColours, env, close, fill: false));
            panel.Children.Add(SwatchSection("Cell fill", FillColours, env, close, fill: true));
        }

        void Refresh()
        {
            foreach (var refresh in refreshers)
            {
                refresh();
            }
        }

        return (panel, Refresh);
    }

    private static (Control Row, Action Refresh) BuildRow(AppCommand command, IAppEnvironment env, Action close)
    {
        var icon = new LucideIcon { IconKey = command.Icon, Size = 16, VerticalAlignment = VerticalAlignment.Center };
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = command.Label, FontSize = 13, FontWeight = FontWeight.Medium },
                new TextBlock { Text = command.Description, FontSize = 11, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap },
            },
        };

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(icon, Dock.Left);
        layout.Children.Add(icon);
        if (!string.IsNullOrEmpty(command.ShortcutText))
        {
            var shortcut = new TextBlock
            {
                Text = command.ShortcutText,
                FontSize = 10.5,
                Foreground = MutedBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            DockPanel.SetDock(shortcut, Dock.Right);
            layout.Children.Add(shortcut);
        }

        icon.Margin = new Thickness(0, 0, 11, 0);
        layout.Children.Add(text);

        var row = new Button
        {
            Content = layout,
            Background = Brushes.Transparent,
            BorderThickness = default,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        row.Click += (_, _) =>
        {
            if (command.IsEnabled(env))
            {
                command.Execute(env);
            }

            close();
        };

        void Refresh()
        {
            row.IsEnabled = command.IsEnabled(env);
            bool on = command.IsToggleActive(env);
            row.Background = on ? CommandBar.ActiveBackground : Brushes.Transparent;
            if (on)
            {
                icon.Foreground = CommandBar.ActiveForeground;
            }
            else
            {
                icon.ClearValue(LucideIcon.ForegroundProperty);
            }
        }

        return (row, Refresh);
    }

    // ---- Text-group extras: size stepper + colour swatches ----------------

    private static readonly double[] FontSizes = { 10, 12, 14, 18, 24 };
    private static readonly string[] TextColours = { "#23211C", "#15734F", "#B9742A", "#B23A35", "#2D5FA8", "#928C7E" };
    private static readonly string?[] FillColours = { null, "#DCEBE2", "#F3E3CD", "#F6D9D6", "#DDE3F0", "#ECE7DA" };

    private static Control SizeSection(IAppEnvironment env, Action close)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (double px in FontSizes)
        {
            System.Func<Cellestra.Core.Styling.CellStyle, Cellestra.Core.Styling.CellStyle> transform =
                s => s.WithFont(s.Font with { SizePoints = px });

            var button = new Button
            {
                Content = px.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0x88, 0x88, 0x88)),
                BorderThickness = default,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12,
            };
            Hook(button, env, transform, $"Font size {px:0}", close);
            row.Children.Add(button);
        }

        return Section("Font size", row);
    }

    private static Control SwatchSection(string title, string?[] colours, IAppEnvironment env, Action close, bool fill)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (string? hex in colours)
        {
            System.Func<Cellestra.Core.Styling.CellStyle, Cellestra.Core.Styling.CellStyle> transform = fill
                ? (hex is null
                    ? s => s.WithFill(Cellestra.Core.Styling.Fill.None)
                    : s => s.WithFill(Cellestra.Core.Styling.Fill.Solid(Cellestra.Core.Styling.ArgbColor.FromHex(hex))))
                : s => s.WithFont(s.Font with { Color = Cellestra.Core.Styling.ArgbColor.FromHex(hex!) });

            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 7, 0),
                Background = hex is null ? Brushes.White : new SolidColorBrush(ParseHex(hex)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            ToolTip.SetTip(swatch, hex is null ? "No fill" : hex);
            Hook(swatch, env, transform, fill ? "Cell fill" : "Text colour", close);
            row.Children.Add(swatch);
        }

        return Section(title, row);
    }

    /// <summary>Wire hover-to-preview and click-to-apply onto a swatch or size chip.</summary>
    private static void Hook(Control control, IAppEnvironment env,
        System.Func<Cellestra.Core.Styling.CellStyle, Cellestra.Core.Styling.CellStyle> transform, string label, Action close)
    {
        control.PointerEntered += (_, _) => env.PreviewCellStyle(transform);
        control.PointerExited += (_, _) => env.PreviewCellStyle(null);
        control.PointerPressed += (_, _) =>
        {
            env.ApplyCellStyle(transform, label);
            close();
        };
    }

    private static Control Section(string title, Control content) => new StackPanel
    {
        Margin = new Thickness(0, 11, 0, 0),
        Children =
        {
            new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 12.5, Margin = new Thickness(2, 0, 0, 6) },
            content,
        },
    };

    private static Color ParseHex(string hex) =>
        Cellestra.Core.Styling.ArgbColor.TryFromHex(hex, out var c) ? Color.FromArgb(c.A, c.R, c.G, c.B) : Colors.Transparent;
}
