using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Cellestra.Core.Styling;
using BirdBrain.Components.Overlays;
using AvColor = Avalonia.Media.Color;
using SAlign = Cellestra.Core.Styling.HorizontalAlignment;
using SVAlign = Cellestra.Core.Styling.VerticalAlignment;
using SBorder = Cellestra.Core.Styling.Border;
using SBorderEdge = Cellestra.Core.Styling.BorderEdge;
using SBorderLineStyle = Cellestra.Core.Styling.BorderLineStyle;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// The Format Cells dialog (PRD §6.2, ⌘1), rendered as an internal modal: number format, font
/// emphasis/size, colors, fill, and horizontal alignment. Colors use the first-party Avalonia
/// <see cref="ColorPicker"/> (spectrum + palette + hex). Its <see cref="ModalDialog{T}.Result"/> is
/// a style transform applied to every selected cell, or null on cancel.
///
/// <para>Mixed selections: when opened on cells whose styles differ, every control reflects that —
/// emphasis checkboxes go tri-state (indeterminate), and combos/colours seed to a "(varies)" /
/// untouched state. <see cref="BuildTransform"/> then only rewrites the fields the user actually
/// touched, so a field left "(varies)" preserves each cell's own value instead of flattening the
/// whole selection to the active cell's style.</para>
/// </summary>
public sealed class FormatCellsDialog : ModalDialog<Func<CellStyle, CellStyle>?>
{
    private const string Varies = "(varies)";

    private static readonly (string Label, string Code)[] Formats =
    {
        ("General", "General"), ("Number (0)", "0"), ("Number (0.00)", "0.00"),
        ("Thousands (#,##0)", "#,##0"), ("Thousands (#,##0.00)", "#,##0.00"),
        ("Percent (0%)", "0%"), ("Percent (0.00%)", "0.00%"),
        ("Currency ($#,##0.00)", "$#,##0.00"), ("Date (yyyy-mm-dd)", "yyyy-mm-dd"),
    };

    private readonly ComboBox _format;
    private readonly CheckBox _bold;
    private readonly CheckBox _italic;
    private readonly CheckBox _underline;
    private readonly CheckBox _strikethrough;
    private readonly NumericUpDown _size;
    private readonly ColorPicker _textColor;
    private readonly CheckBox _fillEnabled;
    private readonly ColorPicker _fillColor;
    private readonly ComboBox _align;
    private readonly ComboBox _valign;

    // Border controls. The dialog holds an in-progress Border; edge checkboxes and presets mutate it.
    private readonly ComboBox _borderStyle;
    private readonly ColorPicker _borderColor;
    private readonly CheckBox _borderTop;
    private readonly CheckBox _borderBottom;
    private readonly CheckBox _borderLeft;
    private readonly CheckBox _borderRight;
    private readonly bool _borderVaries;

    // The border is only rewritten once the user touches it (a preset, an edge toggle, or — when the
    // selection's borders all agree — any change at all). A mixed, untouched border is left per-cell.
    private bool _borderTouched;

    private static readonly (string Label, SBorderLineStyle Style)[] BorderStyles =
    {
        ("Thin", SBorderLineStyle.Thin), ("Medium", SBorderLineStyle.Medium), ("Thick", SBorderLineStyle.Thick),
        ("Dashed", SBorderLineStyle.Dashed), ("Dotted", SBorderLineStyle.Dotted), ("Double", SBorderLineStyle.Double),
    };

    // When the selection's styles disagree on a field, the matching control seeds to a neutral
    // "varies" state and we leave that field untouched unless the user changes it.
    private readonly bool _formatVaries;
    private readonly bool _sizeVaries;
    private readonly bool _textColorVaries;
    private readonly bool _fillVaries;
    private readonly bool _alignVaries;
    private readonly bool _valignVaries;

    // Colour pickers can't show a "varies" sentinel, so we track whether the user touched them; an
    // untouched, varying colour is left alone.
    private bool _textColorTouched;
    private bool _fillColorTouched;

    /// <param name="current">The active cell's style — seeds every control and is the fallback when no selection styles are supplied.</param>
    /// <param name="styles">
    /// Optional: the distinct styles across the whole selection. When supplied and they differ, the
    /// dialog reflects the mixed state and only rewrites touched fields. Defaults to just
    /// <paramref name="current"/> (the previous single-cell behaviour), so existing callers are unchanged.
    /// </param>
    public FormatCellsDialog(CellStyle current, IReadOnlyCollection<CellStyle>? styles = null)
    {
        Title = "Format Cells";

        var all = styles is { Count: > 0 } ? styles : new[] { current };

        _formatVaries = !AllSame(all, s => s.NumberFormat.Code);
        _sizeVaries = !AllSame(all, s => s.Font.SizePoints);
        _textColorVaries = !AllSame(all, s => s.Font.Color);
        _fillVaries = !AllSame(all, s => s.Fill);
        _alignVaries = !AllSame(all, s => s.Alignment.Horizontal);
        _valignVaries = !AllSame(all, s => s.Alignment.Vertical);

        var formatLabels = Formats.Select(f => f.Label).ToList();
        if (_formatVaries)
        {
            formatLabels.Add(Varies);
        }

        _format = new ComboBox { Width = 220, ItemsSource = formatLabels };
        _format.SelectedIndex = _formatVaries
            ? formatLabels.Count - 1
            : Math.Max(0, Array.FindIndex(Formats, f => f.Code == current.NumberFormat.Code));

        _bold = EmphasisBox("Bold", all, f => f.Bold);
        _italic = EmphasisBox("Italic", all, f => f.Italic);
        _underline = EmphasisBox("Underline", all, f => f.Underline);
        _strikethrough = EmphasisBox("Strikethrough", all, f => f.Strikethrough);

        // Wider than the 90px it used to be so the value (e.g. "10") isn't clipped by the spin buttons.
        _size = new NumericUpDown { Width = 130, Minimum = 6, Maximum = 96 };
        _size.Value = _sizeVaries ? null : (decimal)current.Font.SizePoints;

        _textColor = new ColorPicker { Color = ToAvalonia(current.Font.Color.IsTransparent ? ArgbColor.Black : current.Font.Color) };
        _textColor.ColorChanged += (_, _) => _textColorTouched = true;
        var textReset = ResetButton("Reset the text colour to black", () => _textColor.Color = ToAvalonia(ArgbColor.Black));

        _fillEnabled = new CheckBox
        {
            Content = "Fill",
            IsThreeState = _fillVaries,
            IsChecked = _fillVaries ? null : current.Fill.HasBackground,
        };
        _fillColor = new ColorPicker
        {
            Color = ToAvalonia(current.Fill.Background ?? ArgbColor.White),
            IsEnabled = !_fillVaries && current.Fill.HasBackground,
        };
        _fillColor.ColorChanged += (_, _) => _fillColorTouched = true;
        _fillEnabled.IsCheckedChanged += (_, _) => _fillColor.IsEnabled = _fillEnabled.IsChecked == true;
        var fillReset = ResetButton("Clear the fill (no background)", () =>
        {
            _fillEnabled.IsChecked = false;
            _fillColor.Color = ToAvalonia(ArgbColor.White);
        });

        var alignItems = new List<string> { "General", "Left", "Center", "Right" };
        if (_alignVaries)
        {
            alignItems.Add(Varies);
        }

        _align = new ComboBox { Width = 140, ItemsSource = alignItems };
        _align.SelectedIndex = _alignVaries ? alignItems.Count - 1 : (int)current.Alignment.Horizontal;

        var valignItems = new List<string> { "Top", "Middle", "Bottom" };
        if (_valignVaries)
        {
            valignItems.Add(Varies);
        }

        _valign = new ComboBox { Width = 140, ItemsSource = valignItems };
        _valign.SelectedIndex = _valignVaries ? valignItems.Count - 1 : (int)current.Alignment.Vertical;

        // ---- Border section -------------------------------------------------
        _borderVaries = !AllSame(all, s => s.Border);
        var seedBorder = current.Border;

        _borderStyle = new ComboBox { Width = 140, ItemsSource = BorderStyles.Select(b => b.Label).ToList() };
        // Seed the style picker from any visible edge so it reflects what's already there.
        var seedEdge = FirstVisibleEdge(seedBorder);
        _borderStyle.SelectedIndex = Math.Max(0, Array.FindIndex(BorderStyles, b => b.Style == seedEdge.Style));
        _borderStyle.SelectionChanged += (_, _) => RestyleBorder();

        _borderColor = new ColorPicker { Color = ToAvalonia(seedEdge.Color.IsTransparent ? ArgbColor.Black : seedEdge.Color) };
        _borderColor.ColorChanged += (_, _) => RestyleBorder();

        _borderTop = EdgeBox("Top", seedBorder.Top.IsVisible);
        _borderBottom = EdgeBox("Bottom", seedBorder.Bottom.IsVisible);
        _borderLeft = EdgeBox("Left", seedBorder.Left.IsVisible);
        _borderRight = EdgeBox("Right", seedBorder.Right.IsVisible);

        var presets = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                BorderPreset("None", () => SetEdges(false, false, false, false)),
                BorderPreset("Outline", () => SetEdges(true, true, true, true)),
                BorderPreset("All", () => SetEdges(true, true, true, true)),
                BorderPreset("Top", () => SetEdges(top: true, bottom: false, left: false, right: false)),
                BorderPreset("Bottom", () => SetEdges(top: false, bottom: true, left: false, right: false)),
                BorderPreset("Left", () => SetEdges(top: false, bottom: false, left: true, right: false)),
                BorderPreset("Right", () => SetEdges(top: false, bottom: false, left: false, right: true)),
            },
        };

        var ok = new Button { Content = "OK", IsDefault = true };
        ok.Click += (_, _) => Close(BuildTransform());
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        DialogContent = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Row("Number", _format),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _bold, _italic, _underline, _strikethrough } },
                Row("Size", _size),
                Row("Text color", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _textColor, textReset } }),
                Row("Fill", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _fillEnabled, _fillColor, fillReset } }),
                Row("Horizontal", _align),
                Row("Vertical", _valign),
                Row("Border line", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _borderStyle, _borderColor } }),
                Row("Border edges", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _borderTop, _borderBottom, _borderLeft, _borderRight } }),
                Row("Presets", presets),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        };
    }

    private static bool AllSame<T>(IEnumerable<CellStyle> styles, Func<CellStyle, T> select)
    {
        bool first = true;
        T? seed = default;
        foreach (var style in styles)
        {
            var value = select(style);
            if (first)
            {
                seed = value;
                first = false;
            }
            else if (!EqualityComparer<T>.Default.Equals(seed, value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A bold/italic/… checkbox; tri-state (indeterminate) when the selection disagrees.</summary>
    private static CheckBox EmphasisBox(string label, IEnumerable<CellStyle> styles, Func<Font, bool> get)
    {
        bool varies = !AllSame(styles, s => get(s.Font));
        return new CheckBox
        {
            Content = label,
            IsThreeState = varies,
            IsChecked = varies ? null : get(styles.First().Font),
        };
    }

    /// <summary>A compact "Reset" button that reverts a colour control to its default (PRD §6.2).</summary>
    private static Button ResetButton(string tip, Action onClick)
    {
        var button = new Button { Content = "Reset" };
        Avalonia.Controls.ToolTip.SetTip(button, tip);
        button.Click += (_, _) => onClick();
        return button;
    }

    // ---- Border helpers --------------------------------------------------

    /// <summary>An edge checkbox (Top/Bottom/Left/Right); toggling it marks the border touched.</summary>
    private CheckBox EdgeBox(string label, bool on)
    {
        var box = new CheckBox { Content = label, IsChecked = on };
        box.IsCheckedChanged += (_, _) => _borderTouched = true;
        return box;
    }

    /// <summary>A preset button (None/Outline/All/Top/…) that sets the edge checkboxes and marks the border touched.</summary>
    private Button BorderPreset(string label, Action apply)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => { apply(); _borderTouched = true; };
        return button;
    }

    /// <summary>Set the four edge checkboxes (used by presets).</summary>
    private void SetEdges(bool top, bool bottom, bool left, bool right)
    {
        _borderTop.IsChecked = top;
        _borderBottom.IsChecked = bottom;
        _borderLeft.IsChecked = left;
        _borderRight.IsChecked = right;
    }

    /// <summary>Changing the line style or colour re-skins whatever edges are on, and counts as a touch.</summary>
    private void RestyleBorder() => _borderTouched = true;

    /// <summary>The chosen edge (style + colour) the dialog applies to every "on" edge.</summary>
    private SBorderEdge CurrentEdge() =>
        new(BorderStyles[Math.Max(0, _borderStyle.SelectedIndex)].Style, FromAvalonia(_borderColor.Color));

    /// <summary>The first visible edge of a border, to seed the style/colour pickers; falls back to a thin black edge.</summary>
    private static SBorderEdge FirstVisibleEdge(SBorder border)
    {
        if (border.Top.IsVisible)
        {
            return border.Top;
        }

        if (border.Bottom.IsVisible)
        {
            return border.Bottom;
        }

        if (border.Left.IsVisible)
        {
            return border.Left;
        }

        if (border.Right.IsVisible)
        {
            return border.Right;
        }

        return SBorderEdge.Of(SBorderLineStyle.Thin);
    }

    private static StackPanel Row(string label, Control control) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children = { new TextBlock { Text = label, Width = 90, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, control },
    };

    private static AvColor ToAvalonia(ArgbColor c) => AvColor.FromRgb(c.R, c.G, c.B);

    private static ArgbColor FromAvalonia(AvColor c) => ArgbColor.FromRgb(c.R, c.G, c.B);

    /// <summary>
    /// Build the style transform applied to every selected cell. Each field is only rewritten when
    /// the user actually touched it — a tri-state checkbox left indeterminate, a combo left on
    /// "(varies)", or an untouched mixed colour all preserve each cell's own value, so a mixed
    /// selection isn't flattened to one style.
    /// </summary>
    internal Func<CellStyle, CellStyle> BuildTransform()
    {
        // Capture each decision once; the returned closure applies them per cell.
        bool changeFormat = !(_formatVaries && _format.SelectedIndex == Formats.Length);
        string? code = changeFormat ? Formats[_format.SelectedIndex].Code : null;

        bool? bold = _bold.IsChecked;
        bool? italic = _italic.IsChecked;
        bool? underline = _underline.IsChecked;
        bool? strike = _strikethrough.IsChecked;

        bool changeSize = _size.Value is not null;
        double size = (double)(_size.Value ?? 10m);

        bool changeTextColor = !_textColorVaries || _textColorTouched;
        var textColor = FromAvalonia(_textColor.Color);

        // Fill: tri-state checkbox. Indeterminate → leave each cell's fill alone. Unchecked → no fill.
        // Checked → solid; only swap the colour when it's unambiguous or the user picked one.
        bool? fillOn = _fillEnabled.IsChecked;
        bool changeFillColor = !_fillVaries || _fillColorTouched;
        var fillColor = FromAvalonia(_fillColor.Color);

        bool changeAlign = !(_alignVaries && _align.SelectedIndex == 4);
        var horizontal = changeAlign ? (SAlign)_align.SelectedIndex : default;

        bool changeValign = !(_valignVaries && _valign.SelectedIndex == 3);
        var vertical = changeValign ? (SVAlign)_valign.SelectedIndex : default;

        // Border: only rewrite when the user touched it (a preset, an edge toggle, or any change while
        // the selection's borders agreed). The chosen edge (style+colour) applies to every "on" edge;
        // "off" edges become BorderEdge.None. An untouched, varying border is left per-cell.
        bool changeBorder = _borderTouched || !_borderVaries;
        var edge = CurrentEdge();
        var border = new SBorder(
            _borderLeft.IsChecked == true ? edge : SBorderEdge.None,
            _borderRight.IsChecked == true ? edge : SBorderEdge.None,
            _borderTop.IsChecked == true ? edge : SBorderEdge.None,
            _borderBottom.IsChecked == true ? edge : SBorderEdge.None);

        return style =>
        {
            var font = style.Font;
            if (bold is { } b)
            {
                font = font with { Bold = b };
            }

            if (italic is { } i)
            {
                font = font with { Italic = i };
            }

            if (underline is { } u)
            {
                font = font with { Underline = u };
            }

            if (strike is { } s)
            {
                font = font with { Strikethrough = s };
            }

            if (changeSize)
            {
                font = font with { SizePoints = size };
            }

            if (changeTextColor)
            {
                font = font with { Color = textColor };
            }

            var fill = style.Fill;
            if (fillOn == false)
            {
                fill = Fill.None;
            }
            else if (fillOn == true)
            {
                fill = Fill.Solid(changeFillColor ? fillColor : style.Fill.Background ?? fillColor);
            }

            var alignment = style.Alignment;
            if (changeAlign)
            {
                alignment = alignment with { Horizontal = horizontal };
            }

            if (changeValign)
            {
                alignment = alignment with { Vertical = vertical };
            }

            var result = style with { Font = font, Fill = fill, Alignment = alignment };
            if (changeBorder)
            {
                result = result.WithBorder(border);
            }

            return changeFormat ? result.WithNumberFormat(new NumberFormat(code!)) : result;
        };
    }
}
