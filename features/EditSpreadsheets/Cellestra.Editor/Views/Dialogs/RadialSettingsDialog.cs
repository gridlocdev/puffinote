using System;
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
/// a command picker per segment, and a Reset button; on the right, a live preview of the radial drawn
/// exactly as it appears in the grid (same <see cref="RadialMenuOverlay"/>, scaled to fit and centred).
/// Segments can be reordered three ways — dragging a row's label, dragging a wedge in the preview onto
/// another (swap), or ⌥⇧↑/↓ on the selected row. Hovering a wedge highlights it and bolds the matching
/// row; clicking it focuses that row's picker. Selecting a row from the keyboard highlights its wedge in
/// the preview. Returns the edited <see cref="RadialMenuConfig"/>, or null on cancel.
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

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor RowDragCursor = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor RadialDragCursor = new(StandardCursorType.DragMove);
    private static readonly IBrush SelectedRowBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x2A, 0x7D, 0xE1));

    private readonly NumericUpDown _countField;
    private readonly StackPanel _bindingRows = new() { Spacing = 6 };
    private readonly RadialMenuOverlay _preview = new() { Width = PreviewSize, Height = PreviewSize, FitToBounds = true };
    private readonly Border _previewBox;
    private readonly IReadOnlyList<CommandChoice> _choices;

    // Per-segment widgets, in segment order, so the preview can bold/focus/highlight the matching row.
    private readonly List<ComboBox> _pickers = new();
    private readonly List<TextBlock> _segmentLabels = new();
    private readonly List<Border> _rowBorders = new();

    // Inline confirmations layered over the dialog body (one modal at a time is hosted by the editor, so
    // these can't be a second ShowModalAsync — they live in here): "reset to defaults?" and, when Escape
    // is pressed with unsaved edits, "save or cancel?".
    private readonly Border _resetConfirm;
    private readonly Border _saveConfirm;

    // The config as it stood when the dialog opened, to tell whether Escape is discarding real changes.
    private readonly RadialMenuConfig _originalConfig;

    // A full-length working buffer of per-segment bindings (kept the maximum length so shrinking then
    // growing the count remembers earlier picks); only the first N are shown and saved.
    private readonly List<string?> _bindings;

    private int _selectedSegment = -1; // the row targeted by ⌥⇧↑/↓ and arrow navigation (-1 = none)
    private int _hoverSegment = -1;    // the wedge the pointer is over (transient; takes the preview highlight)
    private bool _labelHasFocus;       // a row's drag-handle label currently holds keyboard focus
    private int _rowDragIndex = -1;    // segment being dragged by its left-hand label (-1 = not dragging)
    private int _radialDragIndex = -1; // segment being dragged inside the preview (-1 = not dragging)
    private bool _radialDragMoved;     // the preview drag swapped at least once (so release isn't a click)

    public RadialSettingsDialog(RadialMenuConfig config)
    {
        Title = "Radial Menu Settings";

        var normalized = config.Normalized();
        _originalConfig = normalized;
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
                new TextBlock { Text = "Segment commands", FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = "Drag a row, or use ⌥⇧↑/↓, to reorder.",
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                },
                new ScrollViewer
                {
                    MaxHeight = 260,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = _bindingRows,
                },
                resetButton,
            },
        };

        _previewBox = new Border
        {
            Width = PreviewSize,
            Height = PreviewSize,
            CornerRadius = new Avalonia.CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x20, 0x80, 0x80, 0x80)),
            Cursor = HandCursor,
            Child = _preview,
        };
        // The overlay is hit-test invisible (it has to be, in the grid), so the box forwards pointer
        // events: hovering highlights a wedge + bolds its row, dragging swaps wedges, and a plain click
        // focuses the wedge's picker.
        _previewBox.PointerMoved += OnPreviewPointerMoved;
        _previewBox.PointerExited += (_, _) => { if (_radialDragIndex < 0) { SetHover(-1); } };
        _previewBox.PointerPressed += OnPreviewPointerPressed;
        _previewBox.PointerReleased += OnPreviewPointerReleased;

        var right = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Preview", FontWeight = FontWeight.SemiBold },
                _previewBox,
            },
        };

        // The rows panel carries the left-hand drag: a label press captures it, and the moves/release
        // that follow are read here (capture survives the row rebuild a swap triggers).
        _bindingRows.PointerMoved += OnRowsPointerMoved;
        _bindingRows.PointerReleased += OnRowsPointerReleased;

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
        _saveConfirm = BuildSaveConfirm();
        DialogContent = new Panel { Children = { body, _resetConfirm, _saveConfirm } };

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

    private bool InRange(int index) => index >= 0 && index < SegmentCount;

    /// <summary>Rebuild the per-segment command pickers to match the current segment count.</summary>
    private void RebuildBindingRows()
    {
        _bindingRows.Children.Clear();
        _pickers.Clear();
        _segmentLabels.Clear();
        _rowBorders.Clear();

        if (_selectedSegment >= SegmentCount)
        {
            _selectedSegment = -1;
        }

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

            // The label doubles as the drag handle and the keyboard focus target for reordering.
            var name = new TextBlock
            {
                Text = $"Segment {i + 1}",
                VerticalAlignment = VerticalAlignment.Center,
                Width = 80,
                Focusable = true,
                Cursor = HandCursor,
            };
            name.GotFocus += (_, _) => { _labelHasFocus = true; SelectSegment(index, focus: false); };
            name.LostFocus += (_, _) => _labelHasFocus = false;
            name.PointerPressed += (_, e) => OnLabelPressed(index, e);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(picker, 1);
            grid.Children.Add(name);
            grid.Children.Add(picker);

            var row = new Border
            {
                CornerRadius = new Avalonia.CornerRadius(4),
                Padding = new Avalonia.Thickness(4, 2),
                Child = grid,
            };

            _pickers.Add(picker);
            _segmentLabels.Add(name);
            _rowBorders.Add(row);
            _bindingRows.Children.Add(row);
        }

        ApplySelectionHighlight();
        RefreshPreview();    // resets the preview's highlight...
        SetHover(-1);        // ...so re-assert it (selection wins when nothing is hovered)
    }

    /// <summary>Set the hovered wedge (-1 = none): bolds its row and gives it the preview highlight.</summary>
    private void SetHover(int segment)
    {
        _hoverSegment = segment;
        for (int i = 0; i < _segmentLabels.Count; i++)
        {
            _segmentLabels[i].FontWeight = i == segment ? FontWeight.Bold : FontWeight.Normal;
        }

        UpdatePreviewHighlight();
    }

    /// <summary>The preview highlights the hovered wedge, or — when nothing is hovered — the selected one.</summary>
    private void UpdatePreviewHighlight() =>
        _preview.SetActiveSegment(_hoverSegment >= 0 ? _hoverSegment : _selectedSegment);

    /// <summary>Paint the selected row's background so the ⌥⇧-arrow / drag target is visible.</summary>
    private void ApplySelectionHighlight()
    {
        for (int i = 0; i < _rowBorders.Count; i++)
        {
            _rowBorders[i].Background = i == _selectedSegment ? SelectedRowBrush : null;
        }
    }

    /// <summary>Mark <paramref name="index"/> as the selected row (optionally focusing its label).</summary>
    private void SelectSegment(int index, bool focus)
    {
        _selectedSegment = InRange(index) ? index : -1;
        ApplySelectionHighlight();
        UpdatePreviewHighlight(); // mirror the selection onto the preview wedge
        if (focus && _selectedSegment >= 0)
        {
            _segmentLabels[_selectedSegment].Focus();
        }
    }

    /// <summary>Swap the bindings at <paramref name="a"/> and <paramref name="b"/>, then rebuild the rows.</summary>
    private void Reorder(int a, int b, int select, bool focus)
    {
        if (InRange(a) && InRange(b) && a != b)
        {
            (_bindings[a], _bindings[b]) = (_bindings[b], _bindings[a]);
        }

        _selectedSegment = InRange(select) ? select : -1;
        RebuildBindingRows();
        if (focus && _selectedSegment >= 0)
        {
            _segmentLabels[_selectedSegment].Focus();
        }
    }

    /// <summary>Move the selected row one place up (-1) or down (+1), keeping it selected and focused.</summary>
    private void MoveSelected(int delta)
    {
        if (_selectedSegment < 0)
        {
            return;
        }

        int target = _selectedSegment + delta;
        if (!InRange(target))
        {
            return;
        }

        Reorder(_selectedSegment, target, target, focus: true);
    }

    // --- Left-hand label drag ------------------------------------------------------------------------

    private void OnLabelPressed(int index, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_segmentLabels[index]).Properties.IsLeftButtonPressed)
        {
            return;
        }

        SelectSegment(index, focus: true);
        _rowDragIndex = index;
        _bindingRows.Cursor = RowDragCursor;
        e.Pointer.Capture(_bindingRows); // capture the panel, not the label — it survives the row rebuild
        e.Handled = true;
    }

    private void OnRowsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_rowDragIndex < 0)
        {
            return;
        }

        int target = RowAt(e.GetPosition(_bindingRows).Y);
        if (target >= 0 && target != _rowDragIndex)
        {
            Reorder(_rowDragIndex, target, target, focus: false);
            _rowDragIndex = target;
        }
    }

    private void OnRowsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_rowDragIndex < 0)
        {
            return;
        }

        _rowDragIndex = -1;
        _bindingRows.Cursor = null;
        e.Pointer.Capture(null);
        if (_selectedSegment >= 0)
        {
            _segmentLabels[_selectedSegment].Focus();
        }
    }

    /// <summary>The row index whose band contains <paramref name="y"/> (in the rows panel's space).</summary>
    private int RowAt(double y)
    {
        for (int i = 0; i < _rowBorders.Count; i++)
        {
            if (y <= _rowBorders[i].Bounds.Bottom)
            {
                return i;
            }
        }

        return _rowBorders.Count - 1;
    }

    // --- Preview (radial) interaction ----------------------------------------------------------------

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        int segment = _preview.SegmentAtLocalPoint(e.GetPosition(_preview));
        if (_radialDragIndex >= 0)
        {
            if (segment >= 0 && segment != _radialDragIndex)
            {
                Reorder(_radialDragIndex, segment, segment, focus: false);
                _radialDragIndex = segment;
                _radialDragMoved = true;
            }

            SetHover(_radialDragIndex);
            return;
        }

        SetHover(segment);
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_previewBox).Properties.IsLeftButtonPressed)
        {
            return;
        }

        int segment = _preview.SegmentAtLocalPoint(e.GetPosition(_preview));
        if (segment < 0)
        {
            return;
        }

        _radialDragIndex = segment;
        _radialDragMoved = false;
        SelectSegment(segment, focus: false);
        _previewBox.Cursor = RadialDragCursor;
        e.Pointer.Capture(_previewBox);
        e.Handled = true;
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_radialDragIndex < 0)
        {
            return;
        }

        // A press that never swapped is a plain click: focus that wedge's picker.
        if (!_radialDragMoved && _radialDragIndex < _pickers.Count)
        {
            _pickers[_radialDragIndex].Focus();
        }

        _radialDragIndex = -1;
        _previewBox.Cursor = HandCursor;
        e.Pointer.Capture(null);
    }

    /// <summary>Redraw the preview from the current count and bindings.</summary>
    private void RefreshPreview()
    {
        int count = SegmentCount;
        var segments = new List<RadialSegment>(count);
        for (int i = 0; i < count; i++)
        {
            var command = _bindings[i] is { } id ? AppCommands.ById(id) : null;
            segments.Add(new RadialSegment(command?.Id, command?.Icon, command?.Label ?? string.Empty, command is not null));
        }

        _preview.Open(new Avalonia.Point(PreviewSize / 2, PreviewSize / 2), segments);
    }

    /// <summary>The "reset to defaults?" confirmation layered over the body.</summary>
    private Border BuildResetConfirm() => BuildConfirmOverlay(
        "Reset radial menu?",
        "This restores the default segment count and command bindings. Your current changes will be discarded.",
        ("Cancel", false, HideResetConfirm),
        ("Reset", true, () => { ApplyDefaults(); HideResetConfirm(); }
    ));

    /// <summary>
    /// The "save or cancel?" confirmation shown when Escape is pressed with unsaved edits. Saving closes
    /// with the edited config; cancelling discards (closes with null, like Escape would have); Esc on the
    /// prompt backs out to keep editing.
    /// </summary>
    private Border BuildSaveConfirm() => BuildConfirmOverlay(
        "Save changes?",
        "You've changed the radial menu settings. Save them, or cancel to discard your changes.",
        ("Cancel", false, () => Close(null)),
        ("Save Settings", true, () => Close(BuildConfig())));

    /// <summary>A semi-transparent confirmation card (heading, message, buttons) layered over the body.</summary>
    private static Border BuildConfirmOverlay(string heading, string message, params (string Label, bool IsDefault, Action OnClick)[] buttons)
    {
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        foreach (var (label, isDefault, onClick) in buttons)
        {
            var button = new Button { Content = label, IsDefault = isDefault };
            button.Click += (_, _) => onClick();
            buttonRow.Children.Add(button);
        }

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
                    new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, FontSize = 14 },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    buttonRow,
                },
            },
        };

        return new Border
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), // a scrim that swallows clicks to the body
            Child = card,
        };
    }

    private void ShowResetConfirm() => _resetConfirm.IsVisible = true;

    private void HideResetConfirm() => _resetConfirm.IsVisible = false;

    private void ShowSaveConfirm() => _saveConfirm.IsVisible = true;

    private void HideSaveConfirm() => _saveConfirm.IsVisible = false;

    /// <summary>Whether the edited config differs from how it was when the dialog opened.</summary>
    private bool IsDirty => !ConfigsEqual(BuildConfig(), _originalConfig);

    /// <summary>Content equality for two normalized configs (record equality won't compare the id list).</summary>
    private static bool ConfigsEqual(RadialMenuConfig a, RadialMenuConfig b)
    {
        if (a.SegmentCount != b.SegmentCount)
        {
            return false;
        }

        for (int i = 0; i < a.SegmentCount; i++)
        {
            if (a.CommandIdAt(i) != b.CommandIdAt(i))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Restore every field — count and all bindings — to <see cref="RadialMenuConfig.Default"/>.</summary>
    private void ApplyDefaults()
    {
        var defaults = RadialMenuConfig.Default;
        var fullBindings = (defaults with { SegmentCount = RadialMenuConfig.MaxSegments }).Normalized().CommandIds;
        for (int i = 0; i < _bindings.Count; i++)
        {
            _bindings[i] = i < fullBindings.Count ? fullBindings[i] : null;
        }

        _selectedSegment = -1;
        _countField.Value = defaults.SegmentCount;
        RebuildBindingRows(); // covers the case where the count was already at the default (no ValueChanged)
    }

    /// <summary>Handle Escape: dismiss an open prompt, or guard a dirty close. Returns true if it acted.</summary>
    private bool HandleEscape()
    {
        if (_resetConfirm.IsVisible)
        {
            HideResetConfirm();
            return true;
        }

        if (_saveConfirm.IsVisible)
        {
            HideSaveConfirm();
            return true;
        }

        if (IsDirty)
        {
            ShowSaveConfirm();
            return true;
        }

        return false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // A prompt dismisses first; otherwise unsaved edits raise the save prompt instead of closing.
            if (HandleEscape())
            {
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e); // nothing to guard — let the dialog cancel/close as usual
            return;
        }

        // Reordering shortcuts are inert while a confirmation scrim is up over the body.
        if (_resetConfirm.IsVisible || _saveConfirm.IsVisible)
        {
            base.OnKeyDown(e);
            return;
        }

        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (_selectedSegment >= 0)
        {
            // ⌥⇧↑/↓ reorders the selected command; plain ↑/↓ (while a row label has focus) moves the
            // selection so it can be reordered without the mouse.
            if (alt && shift && e.Key == Key.Up) { MoveSelected(-1); e.Handled = true; return; }
            if (alt && shift && e.Key == Key.Down) { MoveSelected(1); e.Handled = true; return; }
            if (!alt && !shift && _labelHasFocus && e.Key == Key.Up && InRange(_selectedSegment - 1))
            {
                SelectSegment(_selectedSegment - 1, focus: true);
                e.Handled = true;
                return;
            }

            if (!alt && !shift && _labelHasFocus && e.Key == Key.Down && InRange(_selectedSegment + 1))
            {
                SelectSegment(_selectedSegment + 1, focus: true);
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    private RadialMenuConfig BuildConfig() => new RadialMenuConfig
    {
        SegmentCount = SegmentCount,
        CommandIds = _bindings.Take(SegmentCount).ToList(),
    }.Normalized();

    // --- Test seams ----------------------------------------------------------------------------------

    /// <summary>Test seam: the config the dialog would return for its current fields.</summary>
    internal RadialMenuConfig BuildConfigForTest() => BuildConfig();

    /// <summary>Test seam: drive the preview's hover→row-bold wiring without a pointer.</summary>
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

    internal void SelectSegmentForTest(int index) => SelectSegment(index, focus: false);

    internal void MoveSelectedForTest(int delta) => MoveSelected(delta);

    internal void DragReorderForTest(int from, int to) => Reorder(from, to, to, focus: false);

    internal int SelectedSegmentForTest => _selectedSegment;

    internal bool IsRowSelectedForTest(int segment) =>
        segment >= 0 && segment < _rowBorders.Count && ReferenceEquals(_rowBorders[segment].Background, SelectedRowBrush);

    /// <summary>Test seam: which wedge the preview currently highlights.</summary>
    internal int PreviewActiveSegmentForTest => _preview.ActiveSegment;

    internal bool IsDirtyForTest => IsDirty;

    /// <summary>Test seam: run the Escape handler; returns true if it intercepted (prompt shown/dismissed).</summary>
    internal bool PressEscapeForTest() => HandleEscape();

    internal bool SaveConfirmVisibleForTest => _saveConfirm.IsVisible;

    internal void SetSegmentCountForTest(int count) => _countField.Value = count;
}
