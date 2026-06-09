using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Cellestra.Core.Styling;
using Cellestra.Desktop.Views.Overlays;
using AvColor = Avalonia.Media.Color;
using SAlign = Cellestra.Core.Styling.HorizontalAlignment;
using SVAlign = Cellestra.Core.Styling.VerticalAlignment;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// The Format Cells dialog (PRD §6.2, ⌘1), rendered as an internal modal: number format, font
/// emphasis/size, colors, fill, and horizontal alignment. Colors use the first-party Avalonia
/// <see cref="ColorPicker"/> (spectrum + palette + hex). Its <see cref="ModalDialog{T}.Result"/> is
/// a style transform applied to every selected cell, or null on cancel.
/// </summary>
public sealed class FormatCellsDialog : ModalDialog<Func<CellStyle, CellStyle>?>
{
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

    public FormatCellsDialog(CellStyle current)
    {
        Title = "Format Cells";

        _format = new ComboBox { Width = 220, ItemsSource = Formats.Select(f => f.Label).ToList() };
        _format.SelectedIndex = Math.Max(0, Array.FindIndex(Formats, f => f.Code == current.NumberFormat.Code));

        _bold = new CheckBox { Content = "Bold", IsChecked = current.Font.Bold };
        _italic = new CheckBox { Content = "Italic", IsChecked = current.Font.Italic };
        _underline = new CheckBox { Content = "Underline", IsChecked = current.Font.Underline };
        _strikethrough = new CheckBox { Content = "Strikethrough", IsChecked = current.Font.Strikethrough };
        // Wider than the 90px it used to be so the value (e.g. "10") isn't clipped by the spin buttons.
        _size = new NumericUpDown { Width = 130, Minimum = 6, Maximum = 96, Value = (decimal)current.Font.SizePoints };

        _textColor = new ColorPicker { Color = ToAvalonia(current.Font.Color.IsTransparent ? ArgbColor.Black : current.Font.Color) };
        var textReset = ResetButton("Reset the text colour to black", () => _textColor.Color = ToAvalonia(ArgbColor.Black));

        _fillEnabled = new CheckBox { Content = "Fill", IsChecked = current.Fill.HasBackground };
        _fillColor = new ColorPicker
        {
            Color = ToAvalonia(current.Fill.Background ?? ArgbColor.White),
            IsEnabled = current.Fill.HasBackground,
        };
        _fillEnabled.IsCheckedChanged += (_, _) => _fillColor.IsEnabled = _fillEnabled.IsChecked == true;
        var fillReset = ResetButton("Clear the fill (no background)", () =>
        {
            _fillEnabled.IsChecked = false;
            _fillColor.Color = ToAvalonia(ArgbColor.White);
        });

        _align = new ComboBox { Width = 140, ItemsSource = new[] { "General", "Left", "Center", "Right" }, SelectedIndex = (int)current.Alignment.Horizontal };
        _valign = new ComboBox { Width = 140, ItemsSource = new[] { "Top", "Middle", "Bottom" }, SelectedIndex = (int)current.Alignment.Vertical };

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

    /// <summary>A compact "Reset" button that reverts a colour control to its default (PRD §6.2).</summary>
    private static Button ResetButton(string tip, Action onClick)
    {
        var button = new Button { Content = "Reset" };
        Avalonia.Controls.ToolTip.SetTip(button, tip);
        button.Click += (_, _) => onClick();
        return button;
    }

    private static StackPanel Row(string label, Control control) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children = { new TextBlock { Text = label, Width = 90, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, control },
    };

    private static AvColor ToAvalonia(ArgbColor c) => AvColor.FromRgb(c.R, c.G, c.B);

    private static ArgbColor FromAvalonia(AvColor c) => ArgbColor.FromRgb(c.R, c.G, c.B);

    private Func<CellStyle, CellStyle> BuildTransform()
    {
        string code = Formats[_format.SelectedIndex].Code;
        bool bold = _bold.IsChecked == true;
        bool italic = _italic.IsChecked == true;
        bool underline = _underline.IsChecked == true;
        bool strikethrough = _strikethrough.IsChecked == true;
        double size = (double)(_size.Value ?? 10m);
        var textColor = FromAvalonia(_textColor.Color);
        Fill fill = _fillEnabled.IsChecked == true ? Fill.Solid(FromAvalonia(_fillColor.Color)) : Fill.None;
        var horizontal = (SAlign)_align.SelectedIndex;
        var vertical = (SVAlign)_valign.SelectedIndex;

        return style => style with
        {
            NumberFormat = new NumberFormat(code),
            Font = style.Font with { Bold = bold, Italic = italic, Underline = underline, Strikethrough = strikethrough, SizePoints = size, Color = textColor },
            Fill = fill,
            Alignment = style.Alignment with { Horizontal = horizontal, Vertical = vertical },
        };
    }
}
