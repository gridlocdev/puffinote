using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Controls.RadialMenu;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// The per-segment binding rows plus the two ways of reordering them by pointer: dragging a row's
/// left-hand label and dragging a wedge in the preview onto another. Selection/hover highlighting and the
/// live preview redraw are here too, since a drag rebuilds the rows and re-asserts them.
/// </summary>
public sealed partial class RadialSettingsDialog
{
    /// <summary>Rebuild the per-segment rows ("Segment N — Command" + a Select button) for the count.</summary>
    private void RebuildBindingRows()
    {
        _bindingRows.Children.Clear();
        _segmentLabels.Clear();
        _rowBorders.Clear();

        if (SelectedSegmentForTest >= SegmentCount)
        {
            SelectedSegmentForTest = -1;
        }

        for (int i = 0; i < SegmentCount; i++)
        {
            int index = i;

            // The label names the segment and its current command, and doubles as the drag handle and the
            // keyboard focus target for reordering.
            var name = new TextBlock
            {
                Text = $"Segment {i + 1} — {BindingLabel(_bindings[index])}",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Focusable = true,
                Cursor = HandCursor,
            };
            name.GotFocus += (_, _) =>
            {
                _labelHasFocus = true;
                SelectSegment(index, focus: false);
            };
            name.LostFocus += (_, _) => _labelHasFocus = false;
            name.PointerPressed += (_, e) => OnLabelPressed(index, e);

            var select = new Button { Content = "Select…", Margin = new Avalonia.Thickness(8, 0, 0, 0) };
            select.Click += (_, _) => ShowCommandPicker(index);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(select, 1);
            grid.Children.Add(name);
            grid.Children.Add(select);

            var row = new Border
            {
                CornerRadius = new Avalonia.CornerRadius(4),
                Padding = new Avalonia.Thickness(4, 2),
                Child = grid,
            };

            _segmentLabels.Add(name);
            _rowBorders.Add(row);
            _bindingRows.Children.Add(row);
        }

        ApplySelectionHighlight();
        RefreshPreview();    // resets the preview's highlight...
        SetHover(-1);        // ...so re-assert it (selection wins when nothing is hovered)
    }

    /// <summary>The display name of a binding for the row label: the command's label, or "Unbound".</summary>
    private string BindingLabel(string? commandId) =>
        commandId is null ? "Unbound" : _commands?.ById(commandId)?.Title ?? commandId;

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

    /// <summary>
    /// The preview highlights the hovered wedge, or — when nothing is hovered — the selected one. The
    /// floating tooltip only follows a real hover, so navigating the list from the keyboard highlights a
    /// wedge without popping a tip that would linger over other windows.
    /// </summary>
    private void UpdatePreviewHighlight() =>
        _preview.SetActiveSegment(_hoverSegment >= 0 ? _hoverSegment : SelectedSegmentForTest, showTooltip: _hoverSegment >= 0);

    /// <summary>Paint the selected row's background so the ⌥⇧-arrow / drag target is visible.</summary>
    private void ApplySelectionHighlight()
    {
        for (int i = 0; i < _rowBorders.Count; i++)
        {
            _rowBorders[i].Background = i == SelectedSegmentForTest ? SelectedRowBrush : null;
        }
    }

    /// <summary>Mark <paramref name="index"/> as the selected row (optionally focusing its label).</summary>
    private void SelectSegment(int index, bool focus)
    {
        SelectedSegmentForTest = InRange(index) ? index : -1;
        ApplySelectionHighlight();
        UpdatePreviewHighlight(); // mirror the selection onto the preview wedge
        if (focus && SelectedSegmentForTest >= 0)
        {
            _segmentLabels[SelectedSegmentForTest].Focus();
        }
    }

    /// <summary>Swap the bindings at <paramref name="a"/> and <paramref name="b"/>, then rebuild the rows.</summary>
    private void Reorder(int a, int b, int select, bool focus)
    {
        if (InRange(a) && InRange(b) && a != b)
        {
            (_bindings[a], _bindings[b]) = (_bindings[b], _bindings[a]);
        }

        SelectedSegmentForTest = InRange(select) ? select : -1;
        RebuildBindingRows();
        if (focus && SelectedSegmentForTest >= 0)
        {
            _segmentLabels[SelectedSegmentForTest].Focus();
        }
    }

    /// <summary>Move the selected row one place up (-1) or down (+1), keeping it selected and focused.</summary>
    private void MoveSelected(int delta)
    {
        if (SelectedSegmentForTest < 0)
        {
            return;
        }

        int target = SelectedSegmentForTest + delta;
        if (!InRange(target))
        {
            return;
        }

        Reorder(SelectedSegmentForTest, target, target, focus: true);
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
        if (SelectedSegmentForTest >= 0)
        {
            _segmentLabels[SelectedSegmentForTest].Focus();
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

        int segment = _radialDragIndex;
        bool wasClick = !_radialDragMoved;
        _radialDragIndex = -1;
        _previewBox.Cursor = HandCursor;
        e.Pointer.Capture(null);

        // A press that never swapped is a plain click: open the command picker to (re)bind that wedge.
        if (wasClick && segment >= 0 && segment < SegmentCount)
        {
            ShowCommandPicker(segment);
        }
    }

    /// <summary>Redraw the preview from the current count and bindings.</summary>
    private void RefreshPreview()
    {
        int count = SegmentCount;
        var segments = new List<RadialSegment>(count);
        for (int i = 0; i < count; i++)
        {
            var command = _bindings[i] is { } id ? _commands?.ById(id) : null;
            segments.Add(new RadialSegment(command?.Id, command?.Icon, command?.Title ?? string.Empty, command is not null));
        }

        _preview.Open(new Avalonia.Point(PreviewSize / 2, PreviewSize / 2), segments);
    }
}
