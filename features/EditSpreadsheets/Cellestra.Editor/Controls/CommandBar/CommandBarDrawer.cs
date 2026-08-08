using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Controls.Icons;
using ArgbColor = Cellestra.Core.Styling.ArgbColor;
using AvColor = Avalonia.Media.Color;
using CellStyle = Cellestra.Core.Styling.CellStyle;

namespace Cellestra.Desktop.Controls.CommandBar;

/// <summary>
/// Builds the contents of a group's overflow drawer: the full set of that group's tools, each as a
/// labelled, described row (recognition over recall). Returns the panel plus a refresh action the
/// flyout runs on open so rows reflect the current selection. Beyond the discrete commands, groups
/// enrich the drawer with parametric affordances — font size + colours (Text), vertical alignment
/// (Align), and a previewed "Format as" picker (Numbers) — that apply via ApplyCellStyle.
/// </summary>
public static partial class CommandBarDrawer
{
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
    private static readonly IBrush ChipBrush = new SolidColorBrush(Color.FromArgb(0x22, 0x88, 0x88, 0x88));

    public static (Control Content, Action Refresh) Build(CommandBarGroup group, IAppEnvironment env, Action close)
    {
        // Cap the width so a long description wraps onto the next line instead of stretching the drawer
        // ever wider and pushing the shortcut (and the drawer's right edge) off-screen. TextWrapping on
        // the description rows only engages once their container width is bounded.
        var panel = new StackPanel { Spacing = 3, MinWidth = 248, MaxWidth = 360 };
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
            if (env.Commands.ById(id) is { } command)
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

        if (group.HasAlignDrawer)
        {
            panel.Children.Add(VerticalAlignSection(env, close, out var vAlignRefresh));
            refreshers.Add(vAlignRefresh);
        }

        if (group.HasNumberDrawer)
        {
            panel.Children.Add(FormatAsSection(env, close, out var formatRefresh));
            refreshers.Add(formatRefresh);
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
                new TextBlock { Text = command.Title, FontSize = 13, FontWeight = FontWeight.Medium },
                new TextBlock { Text = command.Description, FontSize = 11, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap },
            },
        };

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(icon, Dock.Left);
        layout.Children.Add(icon);
        var shortcutText = command.MacShortcut();
        if (!string.IsNullOrEmpty(shortcutText))
        {
            var shortcut = new TextBlock
            {
                Text = shortcutText,
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
        bool on = false;
        bool hover = false;
        void Paint() => row.Background = on ? CommandBarChrome.ActiveBackground : hover ? CommandBarChrome.HoverBackground : Brushes.Transparent;
        row.PointerEntered += (_, _) =>
        {
            hover = true;
            Paint();
        };
        row.PointerExited += (_, _) =>
        {
            hover = false;
            Paint();
        };
        row.Click += (_, _) =>
        {
            if (command.IsEnabled)
            {
                command.Execute();
                env.Narrate(CommandBar.Describe(command, env));
            }

            close();
        };

        void Refresh()
        {
            row.IsEnabled = command.IsEnabled;
            on = command.IsToggleActive;
            Paint();
            if (on)
            {
                icon.Foreground = CommandBarChrome.ActiveForeground;
            }
            else
            {
                icon.ClearValue(LucideIcon.ForegroundProperty);
            }
        }

        return (row, Refresh);
    }

    // ---- shared helpers ---------------------------------------------------

    /// <summary>Wire hover-to-preview and click-to-apply onto a swatch or chip.</summary>
    private static void Hook(Control control, IAppEnvironment env,
        Func<CellStyle, CellStyle> transform, string label, Action close)
    {
        control.PointerEntered += (_, _) => env.PreviewCellStyle(transform);
        control.PointerExited += (_, _) => env.PreviewCellStyle(null);
        control.PointerPressed += (_, _) =>
        {
            env.ApplyCellStyle(transform, label);
            env.Narrate($"{label} · {env.Selection.ActiveAddress}");
            close();
        };
    }

    /// <summary>The active cell's resolved style — the basis for current size, colour, and toggle state.</summary>
    private static CellStyle ActiveStyle(IAppEnvironment env)
    {
        var sheet = env.Session.ActiveSheet;
        return env.Session.Workbook.Styles.Get(sheet.EffectiveStyleId(env.Selection.ActiveRow, env.Selection.ActiveColumn));
    }

    /// <summary>The active text colour, defaulting transparent/unset to black so the picker opens on something visible.</summary>
    private static ArgbColor DefaultedTextColour(IAppEnvironment env)
    {
        var colour = ActiveStyle(env).Font.Color;
        return colour.IsTransparent ? ArgbColor.Black : colour;
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
        ArgbColor.TryFromHex(hex, out var c) ? Color.FromArgb(c.A, c.R, c.G, c.B) : Colors.Transparent;

    private static AvColor ToAvalonia(ArgbColor c) => AvColor.FromRgb(c.R, c.G, c.B);

    private static ArgbColor FromAvalonia(AvColor c) => ArgbColor.FromRgb(c.R, c.G, c.B);
}
