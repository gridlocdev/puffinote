using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Pivotask.Core.Query;
using Puffinote.Components.Theming;

namespace Pivotask.Control.Views;

/// <summary>
/// The board: one column per group, cards in each, an inline "add a task" at the foot of every column.
///
/// <para>Rebuilt wholesale from the document on every change rather than diffed. A board is tens of
/// tasks, not thousands, so a rebuild is cheap and it removes an entire class of bug — a view that has
/// quietly stopped agreeing with the model. If a board ever gets big enough for this to show, that is
/// the moment to virtualize, and not before.</para>
/// </summary>
public sealed class BoardView : UserControl
{
    private readonly StackPanel _columns = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private TaskItem? _dragging;

    public BoardView()
    {
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12),
            Content = _columns,
        };

        Rebuild();
    }

    /// <summary>The open board. Setting it rewires the change subscription and repaints.</summary>
    public BoardDocument Document
    {
        get;
        set
        {
            field.Changed -= OnDocumentChanged;
            field = value;
            field.Changed += OnDocumentChanged;
            Rebuild();
        }
    } = new();

    /// <summary>The filter the board is showing, shared with the list view (Phase 5's whole seam).</summary>
    public TaskFilter Filter
    {
        get;
        set
        {
            field = value;
            Rebuild();
        }
    } = TaskFilter.All;

    /// <summary>The search text, matched against titles, notes, and step text.</summary>
    public string Query
    {
        get;
        set
        {
            field = value;
            Rebuild();
        }
    } = string.Empty;

    /// <summary>Raised when a card asks to be opened — the host shows the task dialog.</summary>
    public event EventHandler<TaskItem>? TaskOpened;

    /// <summary>Today, injectable so a test can pin what "due soon" means rather than racing the clock.</summary>
    internal DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    internal IReadOnlyList<TaskCard> CardsForTest =>
        [.. _columns.Children.OfType<Border>()
            .SelectMany(column => column.GetLogicalDescendants().OfType<TaskCard>())];

    internal int ColumnCountForTest => _columns.Children.OfType<Border>().Count();

    private void OnDocumentChanged(object? sender, EventArgs e) => Rebuild();

    /// <summary>Rebuild every column from the document.</summary>
    public void Rebuild()
    {
        _columns.Children.Clear();
        foreach (var group in Document.Board.Groups)
        {
            _columns.Children.Add(BuildColumn(group));
        }

        // A trailing "add a group" so a board can grow without a menu — the same affordance the
        // prototype has, and the reason a new board's three columns are a starting point, not a cage.
        var add = new Button { Content = "+ Group", Margin = new Thickness(0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        add.Click += (_, _) => Document.Execute(BoardCommands.AddGroup("New group"));
        _columns.Children.Add(add);
    }

    private Border BuildColumn(TaskGroup group)
    {
        var palette = Tokens.Dark;
        var body = new StackPanel { Spacing = 0 };

        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        var name = new TextBlock
        {
            Text = group.Name,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var visible = Visible(group).ToList();
        var count = new TextBlock
        {
            Text = visible.Count.ToString(System.Globalization.CultureInfo.CurrentCulture),
            Foreground = palette.MutedText,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };

        var collapse = new Button
        {
            Content = group.Collapsed ? "▸" : "▾",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 0),
        };
        collapse.Click += (_, _) => Document.Execute(BoardCommands.SetCollapsed(group, !group.Collapsed));

        DockPanel.SetDock(collapse, Dock.Left);
        DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(collapse);
        header.Children.Add(count);
        header.Children.Add(name);
        body.Children.Add(header);

        if (!group.Collapsed)
        {
            foreach (var task in visible)
            {
                body.Children.Add(BuildCard(group, task));
            }

            body.Children.Add(BuildAddRow(group));
        }

        var column = new Border
        {
            Width = group.Collapsed ? 180 : 280,
            Background = palette.SurfaceInset,
            BorderBrush = palette.SurfaceBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            VerticalAlignment = VerticalAlignment.Top,
            Child = body,
        };

        // Drop target: the column accepts whatever card is being dragged, appending it. Precise
        // between-cards placement is the card's own drop handler below.
        column.PointerReleased += (_, _) => Drop(group, before: null);
        return column;
    }

    /// <summary>The tasks this group shows: filtered, searched, and minus the completed ones when folded.</summary>
    private IEnumerable<TaskItem> Visible(TaskGroup group) =>
        group.Tasks
            .Where(task => !(group.FoldDone && task.Done))
            .Where(task => BoardQuery.Matches(task, Filter, Today) && BoardQuery.Matches(task, Query));

    private TaskCard BuildCard(TaskGroup group, TaskItem task)
    {
        var card = new TaskCard(Document.Board, task, Today);
        card.DoneToggled += (_, done) => Document.Execute(BoardCommands.SetDone(task, done));
        card.StarToggled += (_, important) => Document.Execute(BoardCommands.SetImportant(task, important));
        card.Opened += (_, _) => TaskOpened?.Invoke(this, task);

        // Press-and-drag to move. Kept to a pointer press/release pair rather than Avalonia's DoDragDrop
        // because the keyboard/menu path (BoardCommands.MoveTask, and `ptask move`) is the one that has
        // to work — proposal 0004 §7 says never to let the feature be blocked on drag-and-drop feeling
        // right, so this is the simple version and polish is its own change.
        card.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
            {
                _dragging = task;
            }
        };
        card.PointerReleased += (_, e) =>
        {
            if (_dragging is not null && !ReferenceEquals(_dragging, task))
            {
                Drop(group, before: task);
                e.Handled = true;
            }
            else
            {
                _dragging = null;
            }
        };

        return card;
    }

    private void Drop(TaskGroup target, TaskItem? before)
    {
        if (_dragging is not { } task)
        {
            return;
        }

        _dragging = null;
        if (Document.Board.Locate(task.Id) is { } located
            && (!ReferenceEquals(located.Group, target) || before is not null))
        {
            Document.Execute(BoardCommands.MoveTask(Document.Board, task, target, before));
        }
    }

    private Avalonia.Controls.Control BuildAddRow(TaskGroup group)
    {
        var box = new TextBox { PlaceholderText = "Add a task", Margin = new Thickness(0, 4, 0, 0) };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(box.Text))
            {
                return;
            }

            Document.Execute(BoardCommands.AddTask(group, new TaskItem { Id = Ids.New(), Title = box.Text.Trim() }));
            e.Handled = true;
        };

        return box;
    }
}
