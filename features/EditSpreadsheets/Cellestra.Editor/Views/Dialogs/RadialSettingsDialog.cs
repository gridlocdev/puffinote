using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.RadialMenu;
using Cellestra.Desktop.Views.Overlays;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// Settings for the press-and-hold radial command menu (task 3): on the left, the number of segments,
/// a show-labels toggle, and a command picker per segment; on the right, a live preview of the radial
/// drawn exactly as it appears in the grid (same <see cref="RadialMenuOverlay"/>). Returns the edited
/// <see cref="RadialMenuConfig"/>, or null on cancel.
/// </summary>
public sealed class RadialSettingsDialog : ModalDialog<RadialMenuConfig>
{
    /// <summary>A choice in a segment's command picker; <see cref="Id"/> null is the "unbound" option.</summary>
    private sealed class CommandChoice
    {
        public CommandChoice(string? id, string label)
        {
            Id = id;
            Label = label;
        }

        public string? Id { get; }

        public string Label { get; }

        public override string ToString() => Label; // ComboBox shows this
    }

    private const double PreviewSize = 280;

    private readonly NumericUpDown _countField;
    private readonly CheckBox _labelsToggle;
    private readonly StackPanel _bindingRows = new() { Spacing = 6 };
    private readonly RadialMenuOverlay _preview = new() { Width = PreviewSize, Height = PreviewSize };
    private readonly IReadOnlyList<CommandChoice> _choices;

    // A full-length working buffer of per-segment bindings (kept the maximum length so shrinking then
    // growing the count remembers earlier picks); only the first N are shown and saved.
    private readonly List<string?> _bindings;

    public RadialSettingsDialog(RadialMenuConfig config)
    {
        Title = "Radial Menu Settings";

        var normalized = config.Normalized();
        _bindings = (config with { SegmentCount = RadialMenuConfig.MaxSegments }).Normalized().CommandIds.ToList();

        _choices = new[] { new CommandChoice(null, "— None —") }
            .Concat(AppCommands.All
                .OrderBy(c => c.Label, System.StringComparer.CurrentCulture)
                .Select(c => new CommandChoice(c.Id, c.Label)))
            .ToList();

        _countField = new NumericUpDown
        {
            Minimum = RadialMenuConfig.MinSegments,
            Maximum = RadialMenuConfig.MaxSegments,
            Increment = 1,
            FormatString = "0",
            Value = normalized.SegmentCount,
            Width = 110,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _countField.ValueChanged += (_, _) => RebuildBindingRows();

        _labelsToggle = new CheckBox { Content = "Show text labels", IsChecked = normalized.ShowLabels };
        _labelsToggle.IsCheckedChanged += (_, _) => RefreshPreview();

        var left = new StackPanel
        {
            Width = 300,
            Spacing = 14,
            Children =
            {
                Labeled("Segments", _countField),
                _labelsToggle,
                new TextBlock { Text = "Segment commands", FontWeight = FontWeight.SemiBold },
                new ScrollViewer
                {
                    MaxHeight = 280,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = _bindingRows,
                },
            },
        };

        var right = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                new TextBlock { Text = "Preview", FontWeight = FontWeight.SemiBold },
                new Border
                {
                    Width = PreviewSize,
                    Height = PreviewSize,
                    CornerRadius = new Avalonia.CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromArgb(0x20, 0x80, 0x80, 0x80)),
                    Child = _preview,
                },
            },
        };

        var ok = new Button { Content = "Done", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => Close(BuildConfig());
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        DialogContent = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 24,
                    Children = { left, right },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        };

        RebuildBindingRows();
    }

    private static Control Labeled(string label, Control field) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 80 },
            field,
        },
    };

    private int SegmentCount => (int)(_countField.Value ?? RadialMenuConfig.DefaultSegmentCount);

    /// <summary>Rebuild the per-segment command pickers to match the current segment count.</summary>
    private void RebuildBindingRows()
    {
        _bindingRows.Children.Clear();
        for (int i = 0; i < SegmentCount; i++)
        {
            int index = i;
            var picker = new ComboBox
            {
                ItemsSource = _choices,
                SelectedItem = _choices.FirstOrDefault(c => c.Id == _bindings[index]) ?? _choices[0],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 180,
            };
            picker.SelectionChanged += (_, _) =>
            {
                _bindings[index] = (picker.SelectedItem as CommandChoice)?.Id;
                RefreshPreview();
            };

            _bindingRows.Children.Add(Labeled($"Segment {i + 1}", picker));
        }

        RefreshPreview();
    }

    /// <summary>Redraw the preview from the current count, labels toggle, and bindings.</summary>
    private void RefreshPreview()
    {
        int count = SegmentCount;
        var segments = new List<RadialSegment>(count);
        for (int i = 0; i < count; i++)
        {
            var command = _bindings[i] is { } id ? AppCommands.ById(id) : null;
            segments.Add(new RadialSegment(command?.Id, command?.Icon, command?.Label ?? string.Empty, command is not null));
        }

        _preview.Open(new Avalonia.Point(PreviewSize / 2, PreviewSize / 2), segments, _labelsToggle.IsChecked == true);
    }

    private RadialMenuConfig BuildConfig() => new RadialMenuConfig
    {
        SegmentCount = SegmentCount,
        ShowLabels = _labelsToggle.IsChecked == true,
        CommandIds = _bindings.Take(SegmentCount).ToList(),
    }.Normalized();

    /// <summary>Test seam: the config the dialog would return for its current fields.</summary>
    internal RadialMenuConfig BuildConfigForTest() => BuildConfig();
}
