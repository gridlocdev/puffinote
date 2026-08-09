using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Pivotask.Control.Commands;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Pivotask.Core.Query;
using Puffinote.Components.Commands;
using Puffinote.Components.Input;
using Puffinote.Components.Overlays;
using Puffinote.Components.Theming;

namespace Pivotask.Control.Views;

/// <summary>Board or list — two readings of the same tasks, sharing one filter and one search box.</summary>
public enum BoardLayout
{
    Board,
    List,
}

/// <summary>
/// The embeddable composite: the filter/search strip, the board (or list) under it, the task dialog over
/// it, and the one command catalog that acts on the document. A host — the standalone Pivotask window,
/// or a Puffinote tab — embeds <i>this</i> and gets the whole editor.
/// </summary>
public sealed class TaskBoardView : UserControl
{
    private readonly ContentControl _surface = new();
    private readonly LayoutTransformControl _zoomHost = new();
    /// <summary>"Search tasks", the prototype's words — "Search" alone leaves open what it searches.</summary>
    private readonly TextBox _search = new() { PlaceholderText = "Search tasks", Width = 260 };
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly ContentControl _switcher = new();
    private TopLevel? _keyHost;

    public TaskBoardView()
    {
        BoardForTest.TaskOpened += (_, task) => _ = OpenTaskAsync(task);
        ListForTest.TaskOpened += (_, task) => _ = OpenTaskAsync(task);
        BoardForTest.DeleteGroupRequested += (_, group) => _ = DeleteGroupAsync(group);
        ListForTest.DeleteGroupRequested += (_, group) => _ = DeleteGroupAsync(group);

        _search.TextChanged += (_, _) =>
        {
            BoardForTest.Query = _search.Text ?? string.Empty;
            ListForTest.Query = _search.Text ?? string.Empty;
        };

        // The prototype's order, left to right: the search, then the filters, then the view switch at the
        // far right. Search leads because it is the widest target and the one you reach for without
        // looking; the switch is last because it is the only control here that changes the *shape* of
        // what is below rather than what is in it.
        var strip = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 8) };
        DockPanel.SetDock(_search, Dock.Left);
        var switcher = BuildLayoutSwitch();
        DockPanel.SetDock(switcher, Dock.Right);
        _filters.HorizontalAlignment = HorizontalAlignment.Right;
        _filters.Margin = new Thickness(12, 0);
        strip.Children.Add(_search);
        strip.Children.Add(switcher);
        strip.Children.Add(_filters);

        // The board is scaled, the strip above it is not: zoom magnifies the tasks, and a filter row that
        // grew with them would eat the room the reader zoomed out to get. A LayoutTransformControl (rather
        // than a RenderTransform) is what makes the columns re-measure at the new size, so the cards
        // re-wrap and the scroll extent is honest instead of a magnified picture of the old layout.
        _zoomHost.Child = _surface;
        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(strip, Dock.Top);
        root.Children.Add(strip);
        root.Children.Add(_zoomHost);

        Content = new Panel { Children = { root, OverlayForTest } };

        // The trackpad half of zoom, over the same seam the ⌘= / ⌘− catalog uses (see PinchZoom).
        PinchZoom.Attach(this, () => Zoom, zoom => Zoom = zoom);
        BuildFilters();
        ApplyLayout();
        Document = Document;
    }

    /// <summary>The open board.</summary>
    public BoardDocument Document
    {
        get; set
        {
            field = value;
            BoardForTest.Document = value;
            ListForTest.Document = value;
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            value.Changed += (_, _) => DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    } = new();

    /// <summary>Raised on any change to the document, so a host can repaint its dirty indicator.</summary>
    public event EventHandler? DocumentChanged;

    /// <summary>Board or list. One filter set and one search box serve both — which is why the list view
    /// was an addition in Phase 5 rather than a rework (proposal 0004 §10.6).</summary>
    public BoardLayout Layout
    {
        get;
        set
        {
            field = value;
            ApplyLayout();
        }
    } = BoardLayout.Board;

    public TaskFilter Filter
    {
        get => BoardForTest.Filter;
        set
        {
            BoardForTest.Filter = value;
            ListForTest.Filter = value;
            BuildFilters();
        }
    }

    public string Query
    {
        get => _search.Text ?? string.Empty;
        set => _search.Text = value;
    }

    /// <summary>
    /// How much larger than authored the board draws, on the family's shared ladder
    /// (<see cref="ZoomScale"/>). A board has no single "font size" to scale the way a text editor does —
    /// it is cards, columns and badges — so it scales the whole surface's layout instead, which is the
    /// same thing the reader asked for and keeps the card design in one place.
    /// </summary>
    public double Zoom
    {
        get;
        set
        {
            double clamped = ZoomScale.Clamp(value);
            if (clamped == field)
            {
                return;
            }

            field = clamped;
            _zoomHost.LayoutTransform = clamped == ZoomScale.Default ? null : new ScaleTransform(clamped, clamped);
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    } = ZoomScale.Default;

    /// <summary>Raised after <see cref="Zoom"/> changes, so a host can report or persist it.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>The catalog, built once per view so every surface shares one command instance.</summary>
    public IReadOnlyList<AppCommand> EditingCommands => field ??= TaskEditingCommands.Build(this);

    /// <summary>The groups, for a host's outline panel. Unlike plaintext, a board has real structure.</summary>
    public IReadOnlyList<TaskGroup> Groups => Document.Board.Groups;

    /// <summary>Scroll the board to a group — what an outline item does when clicked.</summary>
    public void Reveal(TaskGroup group)
    {
        Layout = BoardLayout.Board;
        BoardForTest.Rebuild();
        _ = group; // the board lays every column out at once; revealing is bringing the board forward
    }

    /// <summary>True while the task dialog is up, so a host stands its shortcuts down.</summary>
    public bool IsModalOpen => OverlayForTest.IsOpen;

    internal BoardView BoardForTest { get; } = new();

    internal TaskListView ListForTest { get; } = new();

    internal OverlayHost OverlayForTest { get; } = new() { Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) };

    /// <summary>Today, injectable so tests pin what "due soon" means rather than racing the clock.</summary>
    internal DateOnly Today
    {
        get => BoardForTest.Today;
        set
        {
            BoardForTest.Today = value;
            ListForTest.Today = value;
            BoardForTest.Rebuild();
        }
    }

    /// <summary>Open the task dialog on <paramref name="task"/>.</summary>
    public Task OpenTaskAsync(TaskItem task) => OverlayForTest.ShowModalAsync(new TaskDialog(Document, task));

    /// <summary>
    /// Delete a group, asking first when it still holds tasks — the prototype's rule, and the right one:
    /// an empty column is a click to remake, a full one is work. Deleting the last group leaves a fresh
    /// empty one behind rather than a board with nowhere to put anything, which is also what it does.
    /// </summary>
    internal async Task DeleteGroupAsync(TaskGroup group)
    {
        if (group.Tasks.Count > 0)
        {
            bool go = await OverlayForTest.ShowModalAsync(new ConfirmDialog(
                "Delete list",
                $"Delete “{group.Name}” and its {group.Tasks.Count} task{(group.Tasks.Count == 1 ? string.Empty : "s")}?",
                confirmLabel: "Delete",
                denyLabel: "Cancel",
                confirmIsDefault: false));

            if (!go)
            {
                return;
            }
        }

        Document.Execute(BoardCommands.DeleteGroup(Document.Board, group));
        if (Document.Board.Groups.Count == 0)
        {
            Document.Execute(BoardCommands.AddGroup("Tasks"));
        }
    }

    /// <summary>
    /// A bare key has no owner until something is focused, so the two unmodified shortcuts are watched at
    /// the window rather than on this control — the prototype watches the document for the same reason.
    /// The guard in <see cref="OnBareKey"/> is what keeps that from reaching across a host: in a Puffinote
    /// tab, another editor's keystrokes are not ours to read.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _keyHost = TopLevel.GetTopLevel(this);
        _keyHost?.AddHandler(KeyDownEvent, OnBareKey, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _keyHost?.RemoveHandler(KeyDownEvent, OnBareKey);
        _keyHost = null;
    }

    /// <summary>
    /// The two unmodified keys the prototype binds: <c>/</c> jumps to the search box and selects what is
    /// in it, and <c>V</c> flips between board and list.
    /// <para>
    /// Both are suppressed while you are typing — into the search box, a card's add-a-task field, a group
    /// name, or anything else that takes text — and while the task dialog is up, because a bare letter has
    /// to be a letter wherever a letter is expected. A modifier held means the chord belongs to the host's
    /// catalog, so those are left alone too.
    /// </para>
    /// </summary>
    private void OnBareKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || IsModalOpen || IsTyping() || !FocusIsOurs())
        {
            return;
        }

        if (e.Key == Key.Oem2 || e.Key == Key.Divide)
        {
            _search.Focus();
            _search.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.V)
        {
            Layout = Layout == BoardLayout.Board ? BoardLayout.List : BoardLayout.Board;
            e.Handled = true;
        }
    }

    /// <summary>Whether the keyboard currently belongs to a text field somewhere inside this view.</summary>
    private bool IsTyping() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;

    /// <summary>
    /// Whether this board is the thing the keyboard is pointed at. Nothing focused counts as ours — that
    /// is a freshly-opened window with the board the only thing in it — but focus sitting in somebody
    /// else's control does not, so a board in a Puffinote tab can't answer a key meant for the sidebar.
    /// </summary>
    private bool FocusIsOurs() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual focused
        || this.IsVisualAncestorOf(focused);

    private void ApplyLayout()
    {
        _surface.Content = Layout == BoardLayout.Board ? BoardForTest : ListForTest;
        BuildFilters();
    }

    private void BuildFilters()
    {
        var palette = Tokens.Active;
        BuildLayoutSwitch(); // the switch lights the view in force, so it is rebuilt whenever that changes
        _filters.Children.Clear();
        foreach (var (filter, label) in new[]
                 {
                     (TaskFilter.All, "All"),
                     (TaskFilter.Starred, "Starred"),
                     (TaskFilter.DueSoon, "Due soon"),
                     (TaskFilter.Unfinished, "Unfinished"),
                 })
        {
            // A pill, filled when it is the one in force — the prototype's shape, and the right one here:
            // these four are one choice with four answers, and a filled pill says "this is the answer"
            // where an underline reads as a tab you could be on several of.
            bool active = Filter == filter;
            var button = new Button
            {
                Content = label,
                Padding = new Thickness(12, 5),
                CornerRadius = new CornerRadius(999),
                BorderThickness = new Thickness(0),
                Background = active ? palette.SurfaceInset : Brushes.Transparent,
                Foreground = active ? palette.Text : palette.MutedText,
                FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
            };
            Avalonia.Automation.AutomationProperties.SetName(button, label);
            Avalonia.Automation.AutomationProperties.SetHelpText(button, active ? "selected" : null);

            var captured = filter;
            button.Click += (_, _) => Filter = captured;
            _filters.Children.Add(button);
        }
    }

    /// <summary>
    /// The view switch as a segmented pair — Board and List side by side with the current one lit, not one
    /// button that says "Board / List" and leaves you to work out which you are in. A control that shows
    /// the state and the way to change it in the same glance is the whole point of a segmented switch,
    /// and it is what the prototype has.
    /// </summary>
    private Avalonia.Controls.Control BuildLayoutSwitch()
    {
        var palette = Tokens.Active;
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        Avalonia.Automation.AutomationProperties.SetName(group, "Choose a view");

        foreach (var (layout, label) in new[] { (BoardLayout.Board, "Board"), (BoardLayout.List, "List") })
        {
            bool active = Layout == layout;
            var button = new Button
            {
                Content = label,
                Padding = new Thickness(12, 5),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = active ? palette.Accent : Brushes.Transparent,
                Background = active ? palette.SurfaceInset : Brushes.Transparent,
                Foreground = active ? palette.Accent : palette.MutedText,
                FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
            };
            ToolTip.SetTip(button, $"{label} view (V)"); // the chord the prototype advertises here too
            Avalonia.Automation.AutomationProperties.SetName(button, $"{label} view");
            Avalonia.Automation.AutomationProperties.SetHelpText(button, active ? "selected" : null);

            var captured = layout;
            button.Click += (_, _) => Layout = captured;
            group.Children.Add(button);
        }

        _switcher.Content = group;
        return _switcher;
    }
}
