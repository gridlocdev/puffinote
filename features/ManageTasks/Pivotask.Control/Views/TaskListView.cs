using Avalonia;
using Avalonia.Controls;
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
/// The same tasks as rows under group headings, for when a board's columns are more layout than you
/// want. It consumes the same filter/search seam the board does — which is the whole reason Phase 5 was
/// an addition rather than a rework (proposal 0004 §10.6).
/// </summary>
public sealed class TaskListView : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = 2 };

    /// <summary>The layer the card being dragged is drawn on — see <see cref="BoardView"/>'s.</summary>
    private readonly Canvas _ghosts = new() { IsHitTestVisible = false };

    private readonly AddTaskState _add;
    private readonly CardDrag _drag;
    private readonly GroupDrag _groupDrag;
    private readonly CardKeyboard _keyboard;
    private readonly GestureGate _gate;

    public TaskListView()
    {
        _add = new AddTaskState { Rebuild = Rebuild };
        _gate = new GestureGate(this, Rebuild);
        // No glow on the section you are dropping into. On the board a column is a bordered box already, so
        // lighting its edge is a change of colour; a list section has no edge of its own, so the same code
        // *grew* one under the pointer — a line appearing and disappearing around a whole screen of tasks,
        // nudging everything under it by a pixel. The insertion marker says where the card is going, which
        // is the question a drop actually has, and it says it more precisely than a highlighted box.
        _drag = new CardDrag(this, () => Document, _ghosts, lightZones: false);
        _groupDrag = new GroupDrag(this, () => Document, _ghosts, _rows, horizontal: false);
        _keyboard = new CardKeyboard(() => Document, _drag.Zones, sideBySide: false);
        // Margin on the rows rather than padding on the scroller, for the reason the board has it there:
        // padding shrinks the viewport without joining the extent, so the last row is clipped by it.
        _rows.Margin = new Thickness(12);

        // The same paper the board lies on — one sheet under both readings, so switching views is not also
        // a change of background colour.
        Background = Tokens.Active.SurfaceInset;
        Content = new Panel { Children = { new ScrollViewer { Content = _rows }, _ghosts } };
        Rebuild();
    }

    public BoardDocument Document
    {
        get;
        set
        {
            field.Changed -= OnChanged;
            field = value;
            field.Changed += OnChanged;
            Rebuild();
        }
    } = new();

    public TaskFilter Filter
    {
        get;
        set
        {
            field = value;
            Rebuild();
        }
    } = TaskFilter.All;

    public string Query
    {
        get;
        set
        {
            field = value;
            Rebuild();
        }
    } = string.Empty;

    public event EventHandler<TaskItem>? TaskOpened;

    /// <summary>Raised when a section's × is pressed; the host asks before throwing the work away.</summary>
    public event EventHandler<TaskGroup>? DeleteGroupRequested;

    /// <summary>Abandon a drag in flight — a card's or a whole section's — leaving the board as it was;
    /// false when there was none.</summary>
    public bool CancelDrag() => _drag.Cancel() || _groupDrag.Cancel();

    internal DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Every card the list is showing, across all its sections (test seam).</summary>
    internal IReadOnlyList<TaskCard> CardsForTest =>
        [.. _rows.Children.OfType<Border>().SelectMany(s => s.GetLogicalDescendants().OfType<TaskCard>())];

    internal int RowCountForTest => CardsForTest.Count;

    private void OnChanged(object? sender, EventArgs e) => Rebuild();

    /// <summary>
    /// Rebuild every section. The prototype renders the <em>same</em> task markup here as on the board, so
    /// this builds the same <see cref="TaskCard"/> — a task that shows a label, a due pill and a step count
    /// in one view and a bare title in the other reads as two different tasks, and the reader has no way to
    /// know which one is lying.
    /// </summary>
    private void Rebuild()
    {
        // Not while a click is still being delivered — see GestureGate. The repaint is owed, not dropped.
        if (_gate.Defer())
        {
            return;
        }

        _rows.Children.Clear();
        _drag.Zones.Clear();
        _groupDrag.Zones.Clear();

        foreach (var group in Document.Board.Groups)
        {
            _rows.Children.Add(BuildSection(group));
        }

        // The list's own trailing "add a group" — the same ghost, stretched, as the prototype's is.
        var add = GhostButton.Build("Add a group", () => Document.Execute(BoardCommands.AddGroup("New list")));
        add.HorizontalAlignment = HorizontalAlignment.Stretch;
        _rows.Children.Add(add);
    }

    private Avalonia.Controls.Control BuildSection(TaskGroup group)
    {
        var body = new StackPanel { Spacing = 0 };
        var visible = group.Tasks
            .Where(task => BoardQuery.Matches(task, Filter, Today) && BoardQuery.Matches(task, Query))
            .ToList();

        // Unlike the board, a section always appears even when it is empty: the list is the view that
        // shows you the shape of the whole board, and a group that silently vanished from it would be a
        // group you forget you have.
        var head = GroupHeader.Build(
            Document, group, visible.Count, caret: true, () => DeleteGroupRequested?.Invoke(this, group));
        body.Children.Add(head);

        var frame = new Border { Margin = new Thickness(0, 0, 0, 14), Child = body };
        _groupDrag.Attach(head, group, frame); // the heading moves the whole section, as it does a column

        if (!group.Collapsed)
        {
            body.Children.Add(BuildBody(group, visible, frame));
        }

        return frame;
    }

    private Avalonia.Controls.Control BuildBody(TaskGroup group, IReadOnlyList<TaskItem> visible, Border frame)
    {
        var palette = Tokens.Active;
        var stack = new StackPanel { Spacing = 0 };

        // The cards get a panel of their own, separate from the add row under them, so a card dropped at
        // the end of a section lands after the last task rather than after the field you type into.
        var cards = new StackPanel { Spacing = 0 };
        stack.Children.Add(cards);
        _drag.Zones.Add(new DropZone(group, frame, cards));

        // Finished tasks sink below a fold of their own rather than sitting among the open ones. That is
        // the list's answer to the board's per-column "hide completed": same state (TaskGroup.FoldDone),
        // shown as a subhead you can open, because in a single column the finished ones would otherwise
        // push the work you still have off the screen.
        var open = visible.Where(task => !task.Done).ToList();
        var finished = visible.Where(task => task.Done).ToList();

        foreach (var task in open)
        {
            cards.Children.Add(BuildCard(group, task));
        }

        if (open.Count == 0 && finished.Count == 0)
        {
            cards.Children.Add(new TextBlock
            {
                Text = group.Tasks.Count > 0 ? "Nothing here matches the filter." : "Empty. Add the first task below.",
                Foreground = palette.MutedText,
                FontStyle = FontStyle.Italic,
                FontSize = 13,
                Margin = new Thickness(2, 4, 2, 6),
            });
        }

        if (finished.Count > 0)
        {
            var subhead = new Button
            {
                Content = $"{(group.FoldDone ? "▸" : "▾")}  finished · {finished.Count}",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = palette.SurfaceBorder,
                Foreground = palette.MutedText,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(2, 7, 2, 5),
            };
            subhead.Click += (_, _) => Document.Execute(BoardCommands.SetFoldDone(group, !group.FoldDone));
            cards.Children.Add(subhead);

            if (!group.FoldDone)
            {
                foreach (var task in finished)
                {
                    cards.Children.Add(BuildCard(group, task));
                }
            }
        }

        stack.Children.Add(AddTaskRow.Build(Document, group, _add));
        return stack;
    }

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
        var card = new TaskCard(Document.Board, task, Today, flat: true);
        card.DoneToggled += (_, done) => Document.Execute(BoardCommands.SetDone(task, done));
        card.StarToggled += (_, important) => Document.Execute(BoardCommands.SetImportant(task, important));
        card.Opened += (_, _) => TaskOpened?.Invoke(this, task);

        // The list drags too. The prototype makes both views drop targets for the same reason it renders
        // the same card in both: they are two readings of one board, not a board and a report of it.
        _drag.Attach(card, group);
        card.KeyDown += OnCardKey;
        return card;
    }
}

