using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.Icons;
using AvColor = Avalonia.Media.Color;
using ArgbColor = Cellestra.Core.Styling.ArgbColor;
using CellStyle = Cellestra.Core.Styling.CellStyle;
using Fill = Cellestra.Core.Styling.Fill;
using Font = Cellestra.Core.Styling.Font;
using NumberFormat = Cellestra.Core.Styling.NumberFormat;
using NumberFormatKind = Cellestra.Core.Styling.NumberFormatKind;
using NumberFormatter = Cellestra.Core.Styling.NumberFormatter;
using StylePool = Cellestra.Core.Styling.StylePool;
using VAlign = Cellestra.Core.Styling.VerticalAlignment;

namespace Cellestra.Desktop.Controls.CommandBar;

/// <summary>
/// Builds the contents of a group's overflow drawer: the full set of that group's tools, each as a
/// labelled, described row (recognition over recall). Returns the panel plus a refresh action the
/// flyout runs on open so rows reflect the current selection. Beyond the discrete commands, groups
/// enrich the drawer with parametric affordances — font size + colours (Text), vertical alignment
/// (Align), and a previewed "Format as" picker (Numbers) — that apply via ApplyCellStyle.
/// </summary>
public static class CommandBarDrawer
{
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
    private static readonly IBrush ChipBrush = new SolidColorBrush(Color.FromArgb(0x22, 0x88, 0x88, 0x88));

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
        bool on = false;
        bool hover = false;
        void Paint() => row.Background = on ? CommandBar.ActiveBackground : hover ? CommandBar.HoverBackground : Brushes.Transparent;
        row.PointerEntered += (_, _) => { hover = true; Paint(); };
        row.PointerExited += (_, _) => { hover = false; Paint(); };
        row.Click += (_, _) =>
        {
            if (command.IsEnabled(env))
            {
                command.Execute(env);
                env.Narrate(CommandBar.Describe(command, env));
            }

            close();
        };

        void Refresh()
        {
            row.IsEnabled = command.IsEnabled(env);
            on = command.IsToggleActive(env);
            Paint();
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

    private static readonly double[] FontSizes = { 8, 9, 10, 11, 12, 14, 18, 24, 36 };
    private static readonly string[] TextColours = { "#23211C", "#15734F", "#B9742A", "#B23A35", "#2D5FA8", "#928C7E" };
    private static readonly string[] FillColours = { "#DCEBE2", "#F3E3CD", "#F6D9D6", "#DDE3F0", "#ECE7DA", "#FFFFFF" };

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
            CommandBar.AddHover(button, ChipBrush);
            // Preview on hover, commit on click — same feel as the colour swatches.
            button.PointerEntered += (_, _) => env.PreviewCellStyle(s => s.WithFont(s.Font with { SizePoints = px }));
            button.PointerExited += (_, _) => env.PreviewCellStyle(null);
            button.Click += (_, _) => { Apply(px); close(); };
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
        CommandBar.AddHover(button, ChipBrush);
        button.Click += (_, _) => onClick();
        return button;
    }

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
        CommandBar.AddHover(button, ChipBrush);

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

    // ---- Align-group extra: vertical alignment ----------------------------

    private static Control VerticalAlignSection(IAppEnvironment env, Action close, out Action refresh)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var repainters = new List<Action>();

        (string Label, VAlign Value, string Icon)[] options =
        {
            ("Top", VAlign.Top, "align-vertical-justify-start"),
            ("Middle", VAlign.Middle, "align-vertical-justify-center"),
            ("Bottom", VAlign.Bottom, "align-vertical-justify-end"),
        };

        foreach (var (label, value, iconKey) in options)
        {
            var icon = new LucideIcon
            {
                IconKey = iconKey,
                Size = 17,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var button = new Button
            {
                Content = icon,
                Height = 30,
                Padding = new Thickness(12, 0),
                FontSize = 12.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = default,
                CornerRadius = new CornerRadius(6),
            };
            ToolTip.SetTip(button, $"Align {label}");
            Avalonia.Automation.AutomationProperties.SetName(button, $"Align {label}");

            bool on = false;
            bool hover = false;
            void Paint() => button.Background = on ? CommandBar.ActiveBackground : hover ? CommandBar.HoverBackground : Brushes.Transparent;
            button.PointerEntered += (_, _) => { hover = true; Paint(); };
            button.PointerExited += (_, _) => { hover = false; Paint(); };
            button.Click += (_, _) =>
            {
                env.ApplyCellStyle(s => s.WithAlignment(s.Alignment with { Vertical = value }), "Vertical align");
                env.Narrate($"Align {label.ToLowerInvariant()} · {env.Selection.ActiveAddress}");
                close();
            };

            repainters.Add(() =>
            {
                on = ActiveStyle(env).Alignment.Vertical == value;
                Paint();
                if (on)
                {
                    icon.Foreground = CommandBar.ActiveForeground;
                }
                else
                {
                    icon.ClearValue(LucideIcon.ForegroundProperty);
                }
            });
            row.Children.Add(button);
        }

        void Refresh()
        {
            foreach (var paint in repainters)
            {
                paint();
            }
        }

        refresh = Refresh;
        return Section("Vertical alignment", row);
    }

    // ---- Numbers-group extra: a clearer, previewed "Format as" picker ------

    private sealed record FormatOption(string Label, NumberFormat Format, NumberFormatKind Kind);

    // Grouped, plain-language options, each previewed live against a sample value (PRD §8.3).
    private static readonly FormatOption[] FormatOptions =
    {
        new("Automatic (General)", NumberFormat.General, NumberFormatKind.General),
        new("Plain number", new NumberFormat("#,##0.00"), NumberFormatKind.Number),
        new("Whole number", NumberFormat.Integer, NumberFormatKind.Number),
        new("Currency", new NumberFormat("$#,##0.00"), NumberFormatKind.Currency),
        new("Percent", NumberFormat.Percent, NumberFormatKind.Percent),
        new("Scientific", new NumberFormat("0.00E+00"), NumberFormatKind.Scientific),
        new("Date", new NumberFormat("yyyy-mm-dd"), NumberFormatKind.Date),
        new("Time", new NumberFormat("h:mm:ss AM/PM"), NumberFormatKind.Time),
        new("Date & time", new NumberFormat("yyyy-mm-dd h:mm"), NumberFormatKind.DateTime),
        new("Text", NumberFormat.Text, NumberFormatKind.Text),
    };

    private static Control FormatAsSection(IAppEnvironment env, Action close, out Action refresh)
    {
        var culture = env.Session.Workbook.Settings.EffectiveLocale;
        var rows = new StackPanel { Spacing = 2 };
        var refreshers = new List<Action>();

        foreach (var option in FormatOptions)
        {
            var (row, rowRefresh) = FormatRow(option, env, culture, close);
            rows.Children.Add(row);
            refreshers.Add(rowRefresh);
        }

        void Refresh()
        {
            foreach (var r in refreshers)
            {
                r();
            }
        }

        refresh = Refresh;
        return Section("Format as", rows);
    }

    private static (Control Row, Action Refresh) FormatRow(FormatOption option, IAppEnvironment env, CultureInfo culture, Action close)
    {
        var label = new TextBlock { Text = option.Label, FontSize = 13, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
        var preview = new TextBlock
        {
            Text = PreviewOf(option, culture),
            FontSize = 12,
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        DockPanel.SetDock(preview, Dock.Right);

        var check = new LucideIcon
        {
            IconKey = "check",
            Size = 14,
            Foreground = CommandBar.ActiveForeground,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 16,
            Margin = new Thickness(0, 0, 6, 0),
            IsVisible = false,
        };
        Avalonia.Automation.AutomationProperties.SetName(check, "Active format");
        DockPanel.SetDock(check, Dock.Left);

        var layout = new DockPanel { LastChildFill = true };
        layout.Children.Add(check);
        layout.Children.Add(preview);
        layout.Children.Add(label);

        var row = new Button
        {
            Content = layout,
            Background = Brushes.Transparent,
            BorderThickness = default,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };

        bool on = false;
        bool hover = false;
        void Paint() => row.Background = on ? CommandBar.ActiveBackground : hover ? CommandBar.HoverBackground : Brushes.Transparent;

        Func<CellStyle, CellStyle> transform = s => s.WithNumberFormat(option.Format);
        row.PointerEntered += (_, _) => { hover = true; Paint(); env.PreviewCellStyle(transform); };
        row.PointerExited += (_, _) => { hover = false; Paint(); env.PreviewCellStyle(null); };
        row.Click += (_, _) =>
        {
            env.ApplyCellStyle(transform, "Number format");
            env.Narrate($"{option.Label} · {env.Selection.ActiveAddress}");
            // Currency/date can be wider than the column — grow it so the result shows, not #####.
            if (option.Kind != NumberFormatKind.General)
            {
                env.AutofitColumns(env.Selection.Left, env.Selection.Right);
            }

            close();
        };

        void Refresh()
        {
            on = ActiveStyle(env).NumberFormat.Kind == option.Kind;
            check.IsVisible = on;
            Paint();
        }

        return (row, Refresh);
    }

    /// <summary>A representative formatted sample for a format option, so the user can recognise it.</summary>
    private static string PreviewOf(FormatOption option, CultureInfo culture)
    {
        if (option.Kind == NumberFormatKind.Text)
        {
            return "abc";
        }

        // 45292 ≈ 2024-01-01 as an ODF serial date; reads sensibly for date/time/datetime too.
        double sample = option.Kind is NumberFormatKind.Date or NumberFormatKind.Time or NumberFormatKind.DateTime
            ? 45292.5
            : option.Kind == NumberFormatKind.Percent ? 0.25 : 1234.5;
        try
        {
            return NumberFormatter.Format(CellValue.Number(sample), option.Format, culture);
        }
        catch (Exception)
        {
            return string.Empty;
        }
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
        var active = sheet.Find(env.Selection.ActiveRow, env.Selection.ActiveColumn);
        return env.Session.Workbook.Styles.Get(active?.StyleId ?? StylePool.DefaultId);
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
