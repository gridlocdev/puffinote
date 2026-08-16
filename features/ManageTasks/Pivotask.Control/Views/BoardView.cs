using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
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

    /// <summary>The layer the card being dragged is drawn on: over everything, part of nothing. Outside
    /// the scroller on purpose, so the lifted card follows the pointer rather than the content under it.</summary>
    private readonly Canvas _ghosts = new() { IsHitTestVisible = false };

    private readonly AddTaskState _add;
    private readonly CardDrag _drag;
    private readonly GroupDrag _groupDrag;
    private readonly CardKeyboard _keyboard;
    private readonly GestureGate _gate;

    public BoardView()
    {
        _add = new AddTaskState { Rebuild = Rebuild };
        _gate = new GestureGate(this, Rebuild);
        _drag = new CardDrag(this, () => Document, _ghosts);
        _groupDrag = new GroupDrag(this, () => Document, _ghosts, _columns, horizontal: true);
        _keyboard = new CardKeyboard(() => Document, _drag.Zones, sideBySide: true);
        // The inset is the columns' own margin, not the scroller's padding. A ScrollViewer's padding
        // shrinks the viewport without being counted in the extent, so the far edge of the content is
        // clipped by exactly that much however far you scroll — which is why the trailing "add a list"
        // could never be brought fully into view.
        _columns.Margin = new Thickness(12);

        // The paper the columns sit on: the recessed token, not the window's default. A board that painted
        // nothing wore whatever the platform theme decided — a flat white sheet in light, a near-black one
        // in dark — and in both the columns lost the contrast that says they are objects lying on it.
        Background = Tokens.Active.SurfaceInset;
        Content = new Panel
        {
            Children =
            {
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = _columns,
                },
                _ghosts,
            },
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

    /// <summary>Abandon a drag in flight — a card's or a whole column's — leaving the board as it was;
    /// false when there was none.</summary>
    public bool CancelDrag() => _drag.Cancel() || _groupDrag.Cancel();

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
        _groupDrag.Zones.Clear();
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

            // A column is a raised card on the paper, so it takes the *surface* token and the board keeps
            // the inset one. It was the other way round: the columns were the recessed wells and the sheet
            // behind them was whatever the window happened to be, which is the prototype's design inverted.
            Background = palette.Surface,
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

        // The heading is the column's handle: press it and travel, and the whole column moves. Attached
        // after the column exists, because what the gesture carries is the column, not the heading.
        _groupDrag.Attach(head, group, column);
        return column;
    }

    /// <summary>
    /// The tasks this group shows: filtered, searched, minus the completed ones when folded, and with
    /// whatever is finished sunk to the foot of the column.
    ///
    /// <para>A ticked task has stopped being work and become a record of work, and a record sitting
    /// between two things you still have to do is a thing you have to read past every time you look at
    /// the column. It keeps its words and its line through them — you should be able to see what you
    /// finished — it just stops holding a place in the queue.</para>
    ///
    /// <para>Ordering the <em>view</em>, not the group: the file keeps the order the tasks were put in,
    /// so ticking something is still one small undoable edit rather than a tick plus a move, and
    /// unticking it puts it back exactly where it was rather than at the end. <see cref="Enumerable.OrderBy"/>
    /// is stable, so everything else keeps the order it had.</para>
    /// </summary>
    private IEnumerable<TaskItem> Visible(TaskGroup group) =>
        group.Tasks
            .Where(task => !(group.FoldDone && task.Done))
            .Where(task => BoardQuery.Matches(task, Filter, Today) && BoardQuery.Matches(task, Query))
            .OrderBy(task => task.Done);

    /// <summary>
    /// Arrows walk the cards, ⌥⇧-arrows move one. Handled here rather than on the card so the whole set
    /// of zones is in scope: which card is "to the right" is a question only the view can answer.
    /// </summary>
    private void OnCardKey(object? sender, KeyEventArgs e)
    {
        if (sender is not TaskCard card)
        {
            return;
        }

        var outcome = _keyboard.Handle(e, card);
        if (outcome == CardKeyboard.Outcome.Ignored)
        {
            return;
        }

        e.Handled = true;
        if (outcome != CardKeyboard.Outcome.Moved)
        {
            return; // navigation already put the focus where it belongs
        }

        // A move rebuilds the view, which throws away the card that had focus. Put it back on the task
        // that moved — otherwise the second ⌥⇧-arrow of a two-step move has nothing to act on, and the
        // focus ring lands wherever the rebuild happened to leave it.
        Dispatcher.UIThread.Post(() => _keyboard.CardFor(card.Task)?.Focus(NavigationMethod.Directional));
    }

    private TaskCard BuildCard(TaskGroup group, TaskItem task)
    {
        var card = new TaskCard(Document.Board, task, Today);
        card.DoneToggled += (_, done) => Document.Execute(BoardCommands.SetDone(task, done));
        card.StarToggled += (_, important) => Document.Execute(BoardCommands.SetImportant(task, important));
        card.Opened += (_, _) => TaskOpened?.Invoke(this, task);
        _drag.Attach(card, group);
        card.KeyDown += OnCardKey;
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
