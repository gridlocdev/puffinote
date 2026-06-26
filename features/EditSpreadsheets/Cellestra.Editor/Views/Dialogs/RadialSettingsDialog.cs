using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.RadialMenu;
using Cellestra.Desktop.Views.Overlays;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// Settings for the press-and-hold radial command menu (task 3): on the left, the number of segments,
/// a show-labels toggle, a command picker per segment, and a Reset button; on the right, a live preview
/// of the radial drawn exactly as it appears in the grid (same <see cref="RadialMenuOverlay"/>, scaled
/// to fit). Hovering a segment in the preview highlights it and bolds the matching segment row; clicking
/// it focuses that row's picker. Returns the edited <see cref="RadialMenuConfig"/>, or null on cancel.
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

    private const double PreviewSize = 250;
    private const double LeftWidth = 250;

    private readonly NumericUpDown _countField;
    private readonly CheckBox _labelsToggle;
    private readonly StackPanel _bindingRows = new() { Spacing = 6 };
    private readonly RadialMenuOverlay _preview = new() { Width = PreviewSize, Height = PreviewSize, FitToBounds = true };
    private readonly IReadOnlyList<CommandChoice> _choices;

    // Per-segment picker + name label, in segment order, so the preview can bold/focus the matching row.
    private readonly List<ComboBox> _pickers = new();
    private readonly List<TextBlock> _segmentLabels = new();

    // The inline "reset to defaults?" confirmation, layered over the dialog body (one modal at a time is
    // hosted by the editor, so the confirmation can't be a second ShowModalAsync — it lives in here).
    private readonly Border _resetConfirm;

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

        var resetButton = new Button
        {
            Content = "Reset",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        resetButton.Click += (_, _) => ShowResetConfirm();

        var left = new StackPanel
        {
            Width = LeftWidth,
            Spacing = 14,
            Children =
            {
                Labeled("Segments", _countField),
                _labelsToggle,
                new TextBlock { Text = "Segment commands", FontWeight = FontWeight.SemiBold },
                new ScrollViewer
                {
                    MaxHeight = 260,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = _bindingRows,
                },
                resetButton,
            },
        };

        var previewBox = new Border
        {
            Width = PreviewSize,
            Height = PreviewSize,
            CornerRadius = new Avalonia.CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x20, 0x80, 0x80, 0x80)),
            Child = _preview,
        };
        // The overlay is hit-test invisible (it has to be, in the grid), so the box forwards pointer
        // events: hover highlights the segment and bolds its row; a press focuses that row's picker.
        previewBox.PointerMoved += (_, e) => SetHover(_preview.SegmentAtLocalPoint(e.GetPosition(_preview)));
        previewBox.PointerExited += (_, _) => SetHover(-1);
        previewBox.PointerPressed += (_, e) =>
        {
            int segment = _preview.SegmentAtLocalPoint(e.GetPosition(_preview));
            if (segment >= 0 && segment < _pickers.Count)
            {
                _pickers[segment].Focus();
            }
        };

        var right = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                new TextBlock { Text = "Preview", FontWeight = FontWeight.SemiBold },
                previewBox,
            },
        };

        var ok = new Button { Content = "Done", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => Close(BuildConfig());
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        var body = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 18,
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

        _resetConfirm = BuildResetConfirm();
        DialogContent = new Panel { Children = { body, _resetConfirm } };

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
        _pickers.Clear();
        _segmentLabels.Clear();

        for (int i = 0; i < SegmentCount; i++)
        {
            int index = i;
            var picker = new ComboBox
            {
                ItemsSource = _choices,
                SelectedItem = _choices.FirstOrDefault(c => c.Id == _bindings[index]) ?? _choices[0],
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            picker.SelectionChanged += (_, _) =>
            {
                _bindings[index] = (picker.SelectedItem as CommandChoice)?.Id;
                RefreshPreview();
            };

            var name = new TextBlock
            {
                Text = $"Segment {i + 1}",
                VerticalAlignment = VerticalAlignment.Center,
                Width = 80,
            };

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(picker, 1);
            row.Children.Add(name);
            row.Children.Add(picker);

            _pickers.Add(picker);
            _segmentLabels.Add(name);
            _bindingRows.Children.Add(row);
        }

        SetHover(-1);
        RefreshPreview();
    }

    /// <summary>Highlight <paramref name="segment"/> in the preview and bold its row's label (-1 clears).</summary>
    private void SetHover(int segment)
    {
        _preview.SetActiveSegment(segment);
        for (int i = 0; i < _segmentLabels.Count; i++)
        {
            _segmentLabels[i].FontWeight = i == segment ? FontWeight.Bold : FontWeight.Normal;
        }
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

    /// <summary>The semi-transparent "reset to defaults?" confirmation layered over the body.</summary>
    private Border BuildResetConfirm()
    {
        var resetNow = new Button { Content = "Reset", IsDefault = true };
        resetNow.Click += (_, _) =>
        {
            ApplyDefaults();
            HideResetConfirm();
        };
        var keep = new Button { Content = "Cancel" };
        keep.Click += (_, _) => HideResetConfirm();

        var card = new Border
        {
            MaxWidth = 320,
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x2A, 0x2C, 0x31)),
            CornerRadius = new Avalonia.CornerRadius(8),
            Padding = new Avalonia.Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, OffsetY = 4, Color = Color.FromArgb(0x60, 0, 0, 0) }),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Reset radial menu?", FontWeight = FontWeight.SemiBold, FontSize = 14 },
                    new TextBlock
                    {
                        Text = "This restores the default segment count, labels, and command bindings. "
                            + "Your current changes will be discarded.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { keep, resetNow },
                    },
                },
            },
        };

        return new Border
        {
            IsVisible = false,
            // A scrim that swallows clicks to the body behind it; clicking it dismisses the prompt.
            Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)),
            Child = card,
        };
    }

    private void ShowResetConfirm() => _resetConfirm.IsVisible = true;

    private void HideResetConfirm() => _resetConfirm.IsVisible = false;

    /// <summary>Restore every field — count, labels, and all bindings — to <see cref="RadialMenuConfig.Default"/>.</summary>
    private void ApplyDefaults()
    {
        var defaults = RadialMenuConfig.Default;
        var fullBindings = (defaults with { SegmentCount = RadialMenuConfig.MaxSegments }).Normalized().CommandIds;
        for (int i = 0; i < _bindings.Count; i++)
        {
            _bindings[i] = i < fullBindings.Count ? fullBindings[i] : null;
        }

        _countField.Value = defaults.SegmentCount;
        _labelsToggle.IsChecked = defaults.ShowLabels;
        RebuildBindingRows(); // covers the case where the count was already at the default (no ValueChanged)
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // While the reset prompt is up, Esc dismisses just the prompt, not the whole settings dialog.
        if (_resetConfirm.IsVisible && e.Key == Key.Escape)
        {
            HideResetConfirm();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private RadialMenuConfig BuildConfig() => new RadialMenuConfig
    {
        SegmentCount = SegmentCount,
        ShowLabels = _labelsToggle.IsChecked == true,
        CommandIds = _bindings.Take(SegmentCount).ToList(),
    }.Normalized();

    /// <summary>Test seam: the config the dialog would return for its current fields.</summary>
    internal RadialMenuConfig BuildConfigForTest() => BuildConfig();

    /// <summary>Test seam: drive the preview's hover→row-bold + reset-confirm wiring without a pointer.</summary>
    internal void HoverSegmentForTest(int segment) => SetHover(segment);

    internal bool IsRowBoldForTest(int segment) =>
        segment >= 0 && segment < _segmentLabels.Count && _segmentLabels[segment].FontWeight == FontWeight.Bold;

    internal void OpenResetConfirmForTest() => ShowResetConfirm();

    internal bool ResetConfirmVisibleForTest => _resetConfirm.IsVisible;

    internal void ConfirmResetForTest()
    {
        ApplyDefaults();
        HideResetConfirm();
    }
}
