using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Controls;

/// <summary>
/// Dragging a row onto a folder <b>moves</b> it there (PRD §7.2). The gesture is drawn by the tree itself
/// rather than handed to the platform's drag-and-drop: a notebook move is an in-app operation, and the three
/// things that make a tree drag legible — a <b>ghost</b> of what is moving, a <b>drop indicator</b> on where
/// it would land, and a hovered folder <b>opening</b> so you can aim inside it — are all ours to draw.
///
/// <para>What a drop means is decided by where the pointer is, not by what it is over: a <i>folder</i> row is
/// the folder, a <i>file</i> row is the folder that holds it (dropping "onto" a file plainly means "in
/// there, beside it"), and the empty space below the rows is the top level of whatever the tree is scoped
/// to. A move that would change nothing — into the folder it is already in — and one that would swallow
/// itself — a folder into its own descendant — are refused while dragging rather than after dropping, so the
/// indicator never promises something the drop won't do.</para>
/// </summary>
internal sealed partial class FileTreeView
{
    /// <summary>How far the pointer must travel with the button down before a click becomes a drag.</summary>
    private const double DragThreshold = 4;

    /// <summary>How long a folder must be hovered before it opens — long enough not to fire while passing over.</summary>
    private static readonly TimeSpan HoverExpandDelay = TimeSpan.FromSeconds(1);

    private readonly Canvas _dragOverlay = new() { IsHitTestVisible = false };

    private Border? _ghost;
    private Border? _dropIndicator;
    private DispatcherTimer? _hoverExpandTimer;

    /// <summary>The row the pointer went down on, and where — a drag candidate until it moves far enough.</summary>
    private NotebookNode? _pressedNode;
    private Point _pressOrigin;

    /// <summary>The entry being dragged (null when no drag is in flight).</summary>
    private NotebookNode? _draggingNode;

    /// <summary>The folder the drop would move into, or null when the pointer is over nowhere it could go (a test seam reads it).</summary>
    internal string? DropTarget { get; private set; }

    /// <summary>The collapsed folder currently under the pointer, waiting out <see cref="HoverExpandDelay"/>.</summary>
    private string? _hoverFolder;

    /// <summary>Raised to move <paramref>node</paramref> into a folder — the shell does the filesystem work.</summary>
    public event Action<NotebookNode, string>? MoveRequested;

    /// <summary>Whether a drag is in flight (the ghost is up).</summary>
    internal bool IsDraggingForTest => _draggingNode is not null;

    /// <summary>The name shown on the drag ghost, or null when no ghost is up; a test seam.</summary>
    internal string? GhostNameForTest => (_ghost?.Child as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text;

    private void InitializeDrag()
    {
        // Moves and the release are handled on the control, not the list: once the drag starts the pointer is
        // captured here, so both arrive whether or not they are still over a row (or over the tree at all).
        AddHandler(PointerMovedEvent, OnDragPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnDragPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Remember the row a left press landed on; the drag itself only begins once the pointer moves.</summary>
    private void NoteDragCandidate(NotebookNode node, PointerPressedEventArgs e)
    {
        _pressedNode = IsRoot(node) ? null : node; // the scope's own root isn't a row, and can't be moved
        _pressOrigin = e.GetPosition(this);
    }

    private void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        if (_draggingNode is null)
        {
            if (_pressedNode is not { } candidate
                || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                || Math.Abs(point.X - _pressOrigin.X) + Math.Abs(point.Y - _pressOrigin.Y) < DragThreshold)
            {
                return;
            }

            BeginDrag(candidate, e.Pointer);
        }

        UpdateDrag(point);
    }

    private void BeginDrag(NotebookNode node, IPointer pointer)
    {
        _draggingNode = node;
        _activateOnTap = false; // the press that started this drag must not also open the file it picked up
        pointer.Capture(this);
        ShowGhost(node);
    }

    /// <summary>Follow the pointer with the ghost, re-aim the drop indicator, and time the hovered folder open.</summary>
    private void UpdateDrag(Point point)
    {
        if (_draggingNode is not { } dragged)
        {
            return;
        }

        if (_ghost is not null)
        {
            // Offset down-right of the pointer, the way every desktop draws a drag: under the cursor it would
            // cover the row it is being aimed at.
            Canvas.SetLeft(_ghost, point.X + 12);
            Canvas.SetTop(_ghost, point.Y + 8);
        }

        var hit = RowAt(point);
        var directory = ResolveDropDirectory(hit?.Node, _root);
        DropTarget = directory is not null && NotebookMutations.CanMove(dragged.Path, directory) ? directory : null;
        ShowDropIndicator(hit?.Bounds);
        TrackHoverExpand(hit?.Node);
    }

    private void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressedNode = null;
        if (_draggingNode is not { } dragged)
        {
            return;
        }

        var target = DropTarget;
        e.Pointer.Capture(null);
        EndDrag();

        if (target is not null)
        {
            MoveRequested?.Invoke(dragged, target);
        }
    }

    /// <summary>Abandon a drag in flight (Escape, or the tree losing the pointer) without moving anything.</summary>
    private void CancelDrag()
    {
        if (_draggingNode is not null)
        {
            EndDrag();
        }
    }

    private void EndDrag()
    {
        _draggingNode = null;
        DropTarget = null;
        _hoverFolder = null;
        _hoverExpandTimer?.Stop();
        _dragOverlay.Children.Clear();
        _ghost = null;
        _dropIndicator = null;
    }

    /// <summary>
    /// The folder a drop on <paramref name="hovered"/> moves into: the folder itself, the folder holding a
    /// file, or — over the empty space below the rows — the top level of the tree's current scope.
    /// </summary>
    private static string? ResolveDropDirectory(NotebookNode? hovered, NotebookNode? scopeRoot) => hovered switch
    {
        { IsDirectory: true } folder => folder.Path,
        { } file => System.IO.Path.GetDirectoryName(file.Path),
        _ => scopeRoot?.Path,
    };

    /// <summary>The row under <paramref name="point"/> (in this control's space) and where it is drawn.</summary>
    private (NotebookNode Node, Rect Bounds)? RowAt(Point point)
    {
        foreach (var container in _list.GetRealizedContainers())
        {
            if (container.DataContext is not Row row || container.TranslatePoint(default, this) is not { } origin)
            {
                continue;
            }

            var bounds = new Rect(origin, container.Bounds.Size);
            if (bounds.Contains(point))
            {
                return (row.Node, bounds);
            }
        }

        return null;
    }

    /// <summary>The floating label of what is being moved — the row's own glyph and name, lifted off the tree.</summary>
    private void ShowGhost(NotebookNode node)
    {
        var palette = Tokens.Active;
        var kind = node.IsDirectory ? default : FileGlyph.ForFile(node.Name);
        _ghost = new Border
        {
            Background = palette.Surface,
            BorderBrush = palette.Accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3),
            Opacity = 0.9,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new LucideIcon
                    {
                        IconKey = node.IsDirectory ? FileGlyph.FolderIcon(false) : kind.IconKey,
                        Size = 14,
                        Foreground = node.IsDirectory ? FileGlyph.Folder : kind.Color,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = node.Name,
                        Foreground = palette.Text,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };

        _dragOverlay.Children.Add(_ghost);
    }

    /// <summary>
    /// Outline where the drop would land: the target row, or the whole panel when the drop is aimed at the
    /// tree's top level (there is no row for it — the scope root is not drawn). A drop that is refused shows
    /// no outline at all, which is how the tree says "not here" without a second colour to learn.
    /// </summary>
    private void ShowDropIndicator(Rect? rowBounds)
    {
        _dropIndicator ??= new Border
        {
            BorderBrush = Tokens.Active.Accent,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Background = Tokens.Active.AccentSubtle,
            IsHitTestVisible = false,
        };

        if (!_dragOverlay.Children.Contains(_dropIndicator))
        {
            _dragOverlay.Children.Insert(0, _dropIndicator); // under the ghost
        }

        if (DropTarget is null)
        {
            _dropIndicator.IsVisible = false;
            return;
        }

        var bounds = rowBounds ?? new Rect(default, Bounds.Size);
        _dropIndicator.IsVisible = bounds.Width > 0 && bounds.Height > 0;
        _dropIndicator.Width = bounds.Width;
        _dropIndicator.Height = bounds.Height;
        Canvas.SetLeft(_dropIndicator, bounds.X);
        Canvas.SetTop(_dropIndicator, bounds.Y);
    }

    /// <summary>
    /// Open a collapsed folder the drag has rested on. Dropping into a folder you cannot see inside is a guess;
    /// hovering to open it is how every file manager lets you aim deeper without letting go. The dwell restarts
    /// whenever the pointer moves to a different row, so passing over a folder on the way somewhere else never
    /// opens it.
    /// </summary>
    private void TrackHoverExpand(NotebookNode? hovered)
    {
        var folder = hovered is { IsDirectory: true } directory
            && !_expanded.Contains(directory.Path)
            && VisibleChildren(directory).Count > 0
                ? directory.Path
                : null;

        if (string.Equals(folder, _hoverFolder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _hoverFolder = folder;
        _hoverExpandTimer?.Stop();
        if (folder is null)
        {
            return;
        }

        _hoverExpandTimer ??= new DispatcherTimer();
        _hoverExpandTimer.Interval = HoverExpandDelay;
        _hoverExpandTimer.Tick -= OnHoverExpandElapsed;
        _hoverExpandTimer.Tick += OnHoverExpandElapsed;
        _hoverExpandTimer.Start();
    }

    private void OnHoverExpandElapsed(object? sender, EventArgs e)
    {
        _hoverExpandTimer?.Stop();
        if (_draggingNode is null || _hoverFolder is not { } folder)
        {
            return;
        }

        _expanded.Add(folder);
        Rebuild(selectPath: SelectedNode?.Path);
    }

    // ---- Test seams -----------------------------------------------------------------------------
    // Headless pointer plumbing can't hover a virtualized row for a second, so the tests drive the same
    // state machine the pointer does: pick a node up, move over another, drop or cancel.

    internal void BeginDragForTest(NotebookNode node) => BeginDrag(node, new Pointer(0, PointerType.Mouse, isPrimary: true));

    internal void DragOverForTest(NotebookNode? hovered)
    {
        if (_draggingNode is not { } dragged)
        {
            return;
        }

        var directory = ResolveDropDirectory(hovered, _root);
        DropTarget = directory is not null && NotebookMutations.CanMove(dragged.Path, directory) ? directory : null;
        ShowDropIndicator(rowBounds: null);
        TrackHoverExpand(hovered);
    }

    /// <summary>Fire the hover-expand dwell now, instead of waiting out a second of wall clock.</summary>
    internal void ElapseHoverExpandForTest() => OnHoverExpandElapsed(this, EventArgs.Empty);

    internal void DropForTest()
    {
        if (_draggingNode is not { } dragged)
        {
            return;
        }

        var target = DropTarget;
        EndDrag();
        if (target is not null)
        {
            MoveRequested?.Invoke(dragged, target);
        }
    }

    internal void CancelDragForTest() => CancelDrag();
}
