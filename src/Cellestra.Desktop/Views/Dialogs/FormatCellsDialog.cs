using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Cellestra.Core.Styling;
using Cellestra.Desktop.Controls;
using SAlign = Cellestra.Core.Styling.HorizontalAlignment;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// The Format Cells dialog (PRD §6.2, ⌘1): number format, font emphasis/size, colors, fill, and
/// horizontal alignment. Returns a style transform applied to every selected cell, or null on cancel.
/// </summary>
public sealed class FormatCellsDialog : Window
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
    private readonly NumericUpDown _size;
    private readonly ColorPickerButton _textColor;
    private readonly ColorPickerButton _fillColor;
    private readonly ComboBox _align;

    public FormatCellsDialog(CellStyle current)
    {
        Title = "Format Cells";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _format = new ComboBox { Width = 220, ItemsSource = Formats.Select(f => f.Label).ToList() };
        _format.SelectedIndex = Math.Max(0, Array.FindIndex(Formats, f => f.Code == current.NumberFormat.Code));

        _bold = new CheckBox { Content = "Bold", IsChecked = current.Font.Bold };
        _italic = new CheckBox { Content = "Italic", IsChecked = current.Font.Italic };
        _underline = new CheckBox { Content = "Underline", IsChecked = current.Font.Underline };
        _size = new NumericUpDown { Width = 90, Minimum = 6, Maximum = 96, Value = (decimal)current.Font.SizePoints };
        _textColor = new ColorPickerButton(current.Font.Color.IsTransparent ? ArgbColor.Black : current.Font.Color, allowNone: false);
        _fillColor = new ColorPickerButton(current.Fill.HasBackground ? current.Fill.Background : null, allowNone: true);
        _align = new ComboBox { Width = 140, ItemsSource = new[] { "General", "Left", "Center", "Right" }, SelectedIndex = (int)current.Alignment.Horizontal };

        var ok = new Button { Content = "OK", IsDefault = true };
        ok.Click += (_, _) => Close(BuildTransform());
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                Row("Number", _format),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _bold, _italic, _underline } },
                Row("Size", _size),
                Row("Text color", _textColor),
                Row("Fill color", _fillColor),
                Row("Alignment", _align),
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

    private static StackPanel Row(string label, Control control) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children = { new TextBlock { Text = label, Width = 90, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, control },
    };

    private Func<CellStyle, CellStyle> BuildTransform()
    {
        string code = Formats[_format.SelectedIndex].Code;
        bool bold = _bold.IsChecked == true;
        bool italic = _italic.IsChecked == true;
        bool underline = _underline.IsChecked == true;
        double size = (double)(_size.Value ?? 10m);
        var textColor = _textColor.Color ?? ArgbColor.Black;
        Fill fill = _fillColor.Color is { } fc ? Fill.Solid(fc) : Fill.None;
        var horizontal = (SAlign)_align.SelectedIndex;

        return style => style with
        {
            NumberFormat = new NumberFormat(code),
            Font = style.Font with { Bold = bold, Italic = italic, Underline = underline, SizePoints = size, Color = textColor },
            Fill = fill,
            Alignment = style.Alignment with { Horizontal = horizontal },
        };
    }
}
