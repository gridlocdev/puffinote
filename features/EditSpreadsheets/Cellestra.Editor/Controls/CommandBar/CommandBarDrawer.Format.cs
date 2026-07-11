using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Controls.Icons;
using CellStyle = Cellestra.Core.Styling.CellStyle;
using NumberFormat = Cellestra.Core.Styling.NumberFormat;
using NumberFormatKind = Cellestra.Core.Styling.NumberFormatKind;
using NumberFormatter = Cellestra.Core.Styling.NumberFormatter;

namespace Cellestra.Desktop.Controls.CommandBar;

public static partial class CommandBarDrawer
{
    // ---- Numbers-group extra: a clearer, previewed "Format as" picker ------

    private sealed record FormatOption(string Label, NumberFormat Format, NumberFormatKind Kind);

    // Grouped, plain-language options, each previewed live against a sample value (PRD §8.3).
    private static readonly FormatOption[] FormatOptions =
    [
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
    ];

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
            Foreground = CommandBarChrome.ActiveForeground,
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
        void Paint() => row.Background = on ? CommandBarChrome.ActiveBackground : hover ? CommandBarChrome.HoverBackground : Brushes.Transparent;

        CellStyle Transform(CellStyle s) => s.WithNumberFormat(option.Format);
        row.PointerEntered += (_, _) =>
        {
            hover = true;
            Paint();
            env.PreviewCellStyle(Transform);
        };
        row.PointerExited += (_, _) =>
        {
            hover = false;
            Paint();
            env.PreviewCellStyle(null);
        };
        row.Click += (_, _) =>
        {
            env.ApplyCellStyle(Transform, "Number format");
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
            // Numeric-format rows grey out on text cells, just like the bar's primary number buttons
            // (IsNumericContext) — applying "Currency"/"Date"/… to a label can't fail silently.
            // "Automatic (General)" and "Text" always stay enabled (you can always reset or force text).
            row.IsEnabled = !NeedsNumericContext(option) || IsNumericContext(env);
            on = IsActiveFormat(ActiveStyle(env).NumberFormat, option);
            check.IsVisible = on;
            Paint();
        }

        return (row, Refresh);
    }

    /// <summary>True for "Format as" options that only make sense on a numeric cell (everything but General/Text).</summary>
    private static bool NeedsNumericContext(FormatOption option) =>
        option.Kind is not (NumberFormatKind.General or NumberFormatKind.Text);

    /// <summary>
    /// Mirrors <c>AppCommands.IsNumericContext</c>: the active cell can take a number format when it's
    /// empty or already numeric. Used to grey the "Format as" numeric rows on text cells.
    /// </summary>
    private static bool IsNumericContext(IAppEnvironment env)
    {
        var value = env.Session.ActiveSheet.GetValue(env.Selection.ActiveRow, env.Selection.ActiveColumn);
        return value.IsEmpty || value.IsNumber;
    }

    /// <summary>
    /// Whether <paramref name="option"/> is the format currently on the active cell. Matching on the
    /// <see cref="NumberFormatKind"/> alone marks BOTH "Plain number" and "Whole number" (they're both
    /// <see cref="NumberFormatKind.Number"/>), so for kinds that have more than one option in the list
    /// we disambiguate by the exact format code — leaving exactly one row checked.
    /// </summary>
    private static bool IsActiveFormat(NumberFormat active, FormatOption option)
    {
        if (active.Kind != option.Kind)
        {
            return false;
        }

        // Count how many options share this kind; if more than one, require an exact code match.
        int sharing = 0;
        foreach (var o in FormatOptions)
        {
            if (o.Kind == option.Kind)
            {
                sharing++;
            }
        }

        return sharing <= 1 || string.Equals(active.Code, option.Format.Code, StringComparison.OrdinalIgnoreCase);
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
}
