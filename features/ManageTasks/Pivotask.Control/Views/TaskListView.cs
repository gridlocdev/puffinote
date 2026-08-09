using Avalonia;
using Avalonia.Controls;
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
/// The same tasks as rows under group headings, for when a board's columns are more layout than you
/// want. It consumes the same filter/search seam the board does — which is the whole reason Phase 5 was
/// an addition rather than a rework (proposal 0004 §10.6).
/// </summary>
public sealed class TaskListView : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly AddTaskState _add;
    private readonly CardDrag _drag;

    public TaskListView()
    {
        _add = new AddTaskState { Rebuild = Rebuild };
        _drag = new CardDrag(this, () => Document);
        Content = new ScrollViewer { Padding = new Thickness(12), Content = _rows };
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
        _rows.Children.Clear();
        _drag.Zones.Clear();

        foreach (var group in Document.Board.Groups)
        {
            _rows.Children.Add(BuildSection(group));
        }

        // The list's own trailing "add a group", full width — the prototype's `.ghost` button.
        var add = new Button { Content = "＋ Add a group", HorizontalAlignment = HorizontalAlignment.Stretch };
        add.Click += (_, _) => Document.Execute(BoardCommands.AddGroup("New list"));
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
        body.Children.Add(GroupHeader.Build(
            Document, group, visible.Count, caret: true, () => DeleteGroupRequested?.Invoke(this, group)));

        var frame = new Border { Margin = new Thickness(0, 0, 0, 14), Child = body };

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

    private TaskCard BuildCard(TaskGroup group, TaskItem task)
    {
        var card = new TaskCard(Document.Board, task, Today);
        card.DoneToggled += (_, done) => Document.Execute(BoardCommands.SetDone(task, done));
        card.StarToggled += (_, important) => Document.Execute(BoardCommands.SetImportant(task, important));
        card.Opened += (_, _) => TaskOpened?.Invoke(this, task);

        // The list drags too. The prototype makes both views drop targets for the same reason it renders
        // the same card in both: they are two readings of one board, not a board and a report of it.
        _drag.Attach(card, group);
        return card;
    }
}

