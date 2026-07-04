using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.RadialMenu;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// The press-and-hold radial command menu gesture (right mouse button). A quick right-click still
/// raises the ordinary context menu (via <see cref="ContextMenuRequested"/>); holding the button down
/// for <see cref="RadialHoldMilliseconds"/> over a cell brings up the radial instead. While it is up
/// the cursor's <em>direction</em> from the press point highlights a wedge; releasing runs that
/// wedge's bound command (or cancels in the dead-zone centre). Pointer-driven context menus are owned
/// entirely by this gesture so the radial and the menu never both appear.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    private const int RadialHoldMilliseconds = 250;

    private readonly RadialMenuOverlay _radialOverlay = new();
    private DispatcherTimer? _radialHoldTimer;
    private Point _radialCenter;
    private bool _radialPending;        // right button down over a cell, waiting out the hold
    private bool _radialActive;         // the radial is up and tracking the cursor
    private bool _radialContextEligible; // a quick release should raise the context menu
    private int _radialActiveSegment = -1;
    private IReadOnlyList<RadialSegment> _radialSegments = [];

    /// <summary>The segment bindings, labels, and count shown by the radial; set by the host from settings.</summary>
    public RadialMenuConfig RadialConfig
    {
        get;
        set => field = value ?? RadialMenuConfig.Default;
    } = RadialMenuConfig.Default;

    /// <summary>Raised when a radial wedge is released over an enabled bound command; carries its id.</summary>
    public event Action<string>? RadialCommandActivated;

    /// <summary>Asked whether a command id is enabled right now, so a disabled wedge dims (default: enabled).</summary>
    public Func<string, bool>? RadialCommandEnabled { get; set; }

    /// <summary>
    /// The host's command catalog, used to resolve a bound wedge's icon/label and to feed the overlay's
    /// hover tooltip. Set by the editor (which owns the one env-bound catalog); the overlay is kept in sync.
    /// </summary>
    public AppCommands? Commands
    {
        get;
        set
        {
            field = value;
            _radialOverlay.Commands = value;
        }
    }

    /// <summary>Raised on a quick right-click (no hold) so the host shows the ordinary context menu at the point.</summary>
    public event Action<Point>? ContextMenuRequested;

    private void InitRadial()
    {
        _radialHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RadialHoldMilliseconds) };
        _radialHoldTimer.Tick += (_, _) => ActivateRadial();
        Children.Add(_radialOverlay);
    }

    /// <summary>Begin tracking a right-button press: settle the selection, then arm the hold (over a cell).</summary>
    private void BeginRightClickGesture(Point point, IPointer pointer)
    {
        _radialCenter = point;
        _radialActive = false;
        _radialActiveSegment = -1;

        // Right-clicking settles the selection on the target (a cell outside the selection, or a whole
        // row/column for a header) just like the menu does, so the radial acts on what was clicked.
        var target = SelectForContext(point);
        _radialContextEligible = target != ContextTarget.None;
        _radialPending = target == ContextTarget.Cell; // the radial is a cell affordance; headers keep the menu

        pointer.Capture(this);
        if (_radialPending)
        {
            _radialHoldTimer?.Stop();
            _radialHoldTimer?.Start();
        }
    }

    /// <summary>The hold elapsed: raise the radial centred on the press point and start tracking direction.</summary>
    private void ActivateRadial()
    {
        _radialHoldTimer?.Stop();
        if (!_radialPending)
        {
            return;
        }

        _radialPending = false;
        _radialContextEligible = false; // a hold supersedes the quick-click menu
        _radialActive = true;
        _radialSegments = BuildRadialSegments();
        _radialActiveSegment = -1;
        _radialOverlay.Open(_radialCenter, _radialSegments);
    }

    private void UpdateRadialHover(Point point)
    {
        int segment = RadialMenuLayout.SegmentAt(
            point.X - _radialCenter.X, point.Y - _radialCenter.Y, _radialSegments.Count, RadialMenuOverlay.DeadZoneRadius);
        _radialActiveSegment = segment;
        _radialOverlay.SetActiveSegment(segment);
    }

    /// <summary>
    /// Right button released: run the highlighted command, or fall back to the context menu on a quick
    /// click. State is snapshotted and the gesture torn down <em>before</em> acting, so the caller's
    /// capture release (which fires CaptureLost → <see cref="CancelRadial"/>) can't clobber the decision.
    /// </summary>
    private void HandleRadialRelease()
    {
        bool active = _radialActive;
        bool contextEligible = _radialContextEligible;
        int segment = _radialActiveSegment;
        var segments = _radialSegments;
        var center = _radialCenter;

        CancelRadial();

        if (active)
        {
            if (segment >= 0 && segment < segments.Count
                && segments[segment] is { CommandId: { } id, Enabled: true })
            {
                RadialCommandActivated?.Invoke(id);
            }
        }
        else if (contextEligible)
        {
            ContextMenuRequested?.Invoke(center);
        }
    }

    /// <summary>Tear the radial down without acting (Esc, lost capture, or after committing).</summary>
    private void CancelRadial()
    {
        _radialHoldTimer?.Stop();
        _radialPending = false;
        _radialContextEligible = false;
        if (_radialActive)
        {
            _radialActive = false;
            _radialOverlay.Hide();
        }
    }

    /// <summary>Test seam: drive a ⌥⇧-arrow move without key-event plumbing.</summary>
    internal void MoveSelectionForTest(int rowDelta, int columnDelta) => MoveSelectionRequested?.Invoke(rowDelta, columnDelta);

    /// <summary>Test seam: the resolved wedges (icon/label/enabled) for the current config.</summary>
    internal IReadOnlyList<RadialSegment> RadialSegmentsForTest() => BuildRadialSegments();

    /// <summary>Test seam: run a wedge's bound command, as releasing over it would.</summary>
    internal void ActivateRadialForTest(string commandId) => RadialCommandActivated?.Invoke(commandId);

    private IReadOnlyList<RadialSegment> BuildRadialSegments()
    {
        var config = RadialConfig.Normalized();
        var segments = new List<RadialSegment>(config.SegmentCount);
        for (int i = 0; i < config.SegmentCount; i++)
        {
            string? id = config.CommandIdAt(i);
            var command = id is null ? null : Commands?.ById(id);
            bool enabled = command is not null && (RadialCommandEnabled?.Invoke(command.Id) ?? true);
            segments.Add(new RadialSegment(command?.Id, command?.Icon, command?.Title ?? string.Empty, enabled));
        }

        return segments;
    }
}
