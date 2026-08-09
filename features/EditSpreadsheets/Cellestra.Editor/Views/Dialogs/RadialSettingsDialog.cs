using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.RadialMenu;
using Puffinote.Components.Overlays;
using Puffinote.Components.Theming;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// Settings for the press-and-hold radial command menu (task 3): on the left, the number of segments, one
/// row per segment — a grip, the bound command's name, and a pencil that opens the picker — and a Reset
/// button; on the right, a live preview of the radial drawn exactly as it appears in the grid (same
/// <see cref="RadialMenuOverlay"/>, scaled to fit and centred). A row is identified by its position, not
/// by a "Segment N" prefix on every line. Segments can be reordered three ways — dragging a row by its
/// grip or its name, dragging a wedge in the preview onto another (swap), or ⌥⇧↑/↓ on the selected row.
/// Hovering a wedge highlights it and bolds the matching row; clicking it opens a command-palette-style
/// picker to (re)bind that wedge. Selecting a row from the keyboard highlights its wedge in the preview.
/// Returns the edited <see cref="RadialMenuConfig"/>, or null on cancel.
/// </summary>
public sealed partial class RadialSettingsDialog : ModalDialog<RadialMenuConfig>
{
    private const double PreviewSize = 250;
    private const double LeftWidth = 250;

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor RowDragCursor = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor RadialDragCursor = new(StandardCursorType.DragMove);
    // This dialog and its sub-overlays are dark chrome, so they read the dark token palette.
    private static readonly IBrush SelectedRowBrush = Tokens.Active.AccentSubtle;
    private static readonly IBrush GripBrush = Tokens.Active.MutedText;
    private static readonly IBrush PickerMuted = Tokens.Active.MutedText;
    private static readonly IBrush PickerCardBrush = Tokens.Active.Surface;
    private static readonly IBrush ScrimBrush = Tokens.Active.Scrim;

    private readonly NumericUpDown _countField;
    private readonly StackPanel _bindingRows = new() { Spacing = 6 };
    private readonly RadialMenuOverlay _preview = new() { Width = PreviewSize, Height = PreviewSize, FitToBounds = true };
    private readonly Border _previewBox;

    // The one command catalog, used to render each wedge's bound command (label/icon) and populate the
    // binding picker. Optional so the dialog can be constructed standalone (e.g. layout tests); when null
    // the rows read "Unbound" and the picker is empty.
    private readonly AppCommands? _commands;

    // Per-segment widgets, in segment order, so the preview can bold/highlight the matching row.
    private readonly List<TextBlock> _segmentLabels = [];
    private readonly List<Border> _rowBorders = [];
    private readonly List<Button> _editButtons = [];

    // Inline confirmations layered over the dialog body (one modal at a time is hosted by the editor, so
    // these can't be a second ShowModalAsync — they live in here): "reset to defaults?" and, when Escape
    // is pressed with unsaved edits, "save or cancel?".
    private readonly Border _resetConfirm;
    private readonly Border _saveConfirm;

    // A command-palette-style picker for binding one wedge, layered over the body like the prompts above.
    private readonly Border _commandPicker;
    private TextBox _pickerSearch = null!;
    private ListBox _pickerList = null!;
    private TextBlock _pickerTitle = null!;

    // The config as it stood when the dialog opened, to tell whether Escape is discarding real changes.
    private readonly RadialMenuConfig _originalConfig;

    // A full-length working buffer of per-segment bindings (kept the maximum length so shrinking then
    // growing the count remembers earlier picks); only the first N are shown and saved.
    private readonly List<string?> _bindings;
    private int _hoverSegment = -1;    // the wedge the pointer is over (transient; takes the preview highlight)
    private bool _labelHasFocus;       // a row's drag-handle label currently holds keyboard focus
    private int _rowDragIndex = -1;    // segment being dragged by its left-hand label (-1 = not dragging)
    private int _radialDragIndex = -1; // segment being dragged inside the preview (-1 = not dragging)
    private bool _radialDragMoved;     // the preview drag swapped at least once (so release isn't a click)

    public RadialSettingsDialog(RadialMenuConfig config, AppCommands? commands = null)
    {
        Title = "Radial Menu Settings";

        _commands = commands;
        _preview.Commands = commands;

        var normalized = config.Normalized();
        _originalConfig = normalized;
        _bindings = [.. (config with { SegmentCount = RadialMenuConfig.MaxSegments }).Normalized().CommandIds];

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
                    Text = "Drag a row by its grip, or use ⌥⇧↑/↓, to reorder.",
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
        _previewBox.PointerExited += (_, _) =>
        {
            if (_radialDragIndex < 0)
            {
                SetHover(-1);
            }
        };
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
        _commandPicker = BuildCommandPicker();
        DialogContent = new Panel { Children = { body, _resetConfirm, _saveConfirm, _commandPicker } };

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

    internal int SelectedSegmentForTest { get; private set; } = -1;

    internal bool IsRowSelectedForTest(int segment) =>
        segment >= 0 && segment < _rowBorders.Count && ReferenceEquals(_rowBorders[segment].Background, SelectedRowBrush);

    /// <summary>Test seam: the bound command's name, which is all a row's label says.</summary>
    internal string? RowLabelForTest(int segment) =>
        segment >= 0 && segment < _segmentLabels.Count ? _segmentLabels[segment].Text : null;

    /// <summary>Test seam: a row's pencil button — the one that opens the picker for that segment.</summary>
    internal Button? RowEditButtonForTest(int segment) =>
        segment >= 0 && segment < _editButtons.Count ? _editButtons[segment] : null;

    /// <summary>Test seam: which wedge the preview currently highlights.</summary>
    internal int PreviewActiveSegmentForTest => _preview.ActiveSegment;

    internal bool IsDirtyForTest => IsDirty;

    /// <summary>Test seam: run the Escape handler; returns true if it intercepted (prompt shown/dismissed).</summary>
    internal bool PressEscapeForTest() => HandleEscape();

    internal bool SaveConfirmVisibleForTest => _saveConfirm.IsVisible;

    internal void SetSegmentCountForTest(int count) => _countField.Value = count;

    internal void OpenCommandPickerForTest(int segment) => ShowCommandPicker(segment);

    internal bool CommandPickerVisibleForTest => _commandPicker.IsVisible;

    /// <summary>Test seam: the picker's scrim element, to assert Tab focus stays inside it.</summary>
    internal Control CommandPickerOverlayForTest => _commandPicker;

    internal int CommandPickerSegmentForTest { get; private set; } = -1;

    /// <summary>Test seam: command ids the picker lists for <paramref name="query"/>, in ranked order.</summary>
    internal IReadOnlyList<string?> PickerResultIdsForTest(string query)
    {
        PopulatePicker(query);
        return [.. _pickerList.ItemsSource!.Cast<PickerItem>().Select(i => i.Id)];
    }

    /// <summary>Test seam: pick the row with <paramref name="commandId"/> (null = unbind) and commit it.</summary>
    internal void CommitPickerForTest(string? commandId)
    {
        var items = _pickerList.ItemsSource!.Cast<PickerItem>().ToList();
        _pickerList.SelectedItem = items.First(i => i.Id == commandId);
        CommitPicker();
    }
}
