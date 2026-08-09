using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private readonly AddTaskState _add;
    private readonly CardDrag _drag;
    private readonly GestureGate _gate;

    public BoardView()
    {
        _add = new AddTaskState { Rebuild = Rebuild };
        _gate = new GestureGate(this, Rebuild);
        _drag = new CardDrag(this, () => Document);
        // The inset is the columns' own margin, not the scroller's padding. A ScrollViewer's padding
        // shrinks the viewport without being counted in the extent, so the far edge of the content is
        // clipped by exactly that much however far you scroll — which is why the trailing "add a list"
        // could never be brought fully into view.
        _columns.Margin = new Thickness(12);
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
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

    /// <summary>
    /// Raised when a group's × is pressed. The view doesn't delete it: throwing away a column of work
    /// wants a question first, and the overlay that asks it belongs to the host.
    /// </summary>
    public event EventHandler<TaskGroup>? DeleteGroupRequested;

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
        // Not while a click is still being delivered — see GestureGate. The repaint is owed, not dropped.
        if (_gate.Defer())
        {
            return;
        }

        _columns.Children.Clear();
        _drag.Zones.Clear();
        foreach (var group in Document.Board.Groups)
        {
            _columns.Children.Add(BuildColumn(group));
        }

        // A trailing "add a list" so a board can grow without a menu — the same affordance, and the same
        // words, the prototype uses, and the reason a new board's columns are a starting point not a cage.
        // A column-stub's width, not its label's: it marks the next slot along, not a control that
        // wandered in at the end of the row.
        var add = GhostButton.Build("Add a list", () => Document.Execute(BoardCommands.AddGroup("New list")));
        add.Width = GhostButton.Width;
        add.VerticalAlignment = VerticalAlignment.Top;
        _columns.Children.Add(add);
    }

    private Border BuildColumn(TaskGroup group)
    {
        var palette = Tokens.Active;
        var visible = Visible(group).ToList();

        var cards = new StackPanel { Spacing = 0 };
        foreach (var task in visible)
        {
            cards.Children.Add(BuildCard(group, task));
        }

        if (visible.Count == 0)
        {
            cards.Children.Add(EmptyNote(group));
        }

        // Head, scrolling body, pinned foot — the prototype's three parts, and the reason it has them:
        // a column of thirty tasks is taller than any window, and a board that simply grew past the
        // bottom edge put both the tasks below the fold and the "add a task" row out of reach. The head
        // stays because a column you have scrolled into is a column you still need the name of.
        var layout = new DockPanel { LastChildFill = true };

        // No collapse caret here: a board column is not something the prototype folds — that affordance
        // belongs to the list, where sections stack vertically and folding buys back a screen.
        var head = GroupHeader.Build(
            Document, group, visible.Count, caret: false, () => DeleteGroupRequested?.Invoke(this, group));
        DockPanel.SetDock(head, Dock.Top);
        layout.Children.Add(head);

        var foot = AddTaskRow.Build(Document, group, _add);
        DockPanel.SetDock(foot, Dock.Bottom);
        layout.Children.Add(foot);

        layout.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = cards,
        });

        var column = new Border
        {
            Width = 280,
            Background = palette.SurfaceInset,
            BorderBrush = palette.SurfaceBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),

            // Top, not Stretch: a column is as tall as its tasks, and only a column that would outgrow the
            // board is clipped back to it — a board of short columns should not be a board of tall empty
            // boxes. The cap comes from the height the board hands down, which is why its own scroller
            // below scrolls sideways only.
            VerticalAlignment = VerticalAlignment.Top,
            Child = layout,
        };

        _drag.Zones.Add(new DropZone(group, column, cards));
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
        _drag.Attach(card, group);
        return card;
    }

    /// <summary>
    /// What an empty column says. Two sentences, because they are two situations: a group with nothing in
    /// it is waiting for you, and a group whose tasks are all filtered out is telling you why it looks
    /// empty. Saying nothing at all — which is what this did — leaves the reader to guess which it is.
    /// </summary>
    private static TextBlock EmptyNote(TaskGroup group) => new()
    {
        Text = group.Tasks.Count > 0 ? "Nothing here matches the filter." : "Empty. Add the first task below.",
        Foreground = Tokens.Active.MutedText,
        FontStyle = FontStyle.Italic,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 4, 2, 6),
    };
}
