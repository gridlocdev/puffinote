using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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

    public TaskListView()
    {
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

    internal DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    internal int RowCountForTest => _rows.Children.OfType<DockPanel>().Count();

    private void OnChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        _rows.Children.Clear();
        var palette = Tokens.Active;
        bool any = false;

        foreach (var group in Document.Board.Groups)
        {
            var matches = group.Tasks
                .Where(task => BoardQuery.Matches(task, Filter, Today) && BoardQuery.Matches(task, Query))
                .ToList();

            if (matches.Count == 0)
            {
                continue; // an empty group is a column on the board, but noise in a list
            }

            any = true;
            _rows.Children.Add(new TextBlock
            {
                Text = group.Name,
                FontWeight = FontWeight.SemiBold,
                Foreground = palette.MutedText,
                Margin = new Thickness(0, 10, 0, 4),
            });

            foreach (var task in matches)
            {
                _rows.Children.Add(BuildRow(task));
            }
        }

        if (!any)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = Query.Length > 0 ? $"Nothing matches “{Query}”." : "Nothing here.",
                Foreground = palette.MutedText,
            });
        }
    }

    private DockPanel BuildRow(TaskItem task)
    {
        var palette = Tokens.Active;
        var check = new CheckBox { IsChecked = task.Done, VerticalAlignment = VerticalAlignment.Center };
        check.IsCheckedChanged += (_, _) =>
        {
            if ((check.IsChecked == true) != task.Done)
            {
                Document.Execute(BoardCommands.SetDone(task, check.IsChecked == true));
            }
        };

        var title = new TextBlock
        {
            Text = task.Title,
            VerticalAlignment = VerticalAlignment.Center,
            TextDecorations = task.Done ? Avalonia.Media.TextDecorations.Strikethrough : null,
            Foreground = task.Done ? palette.MutedText : null,
        };

        var trailing = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (task.Important)
        {
            trailing.Children.Add(new TextBlock { Text = "★", Foreground = Brushes.Goldenrod, Margin = new Thickness(0, 0, 6, 0) });
        }

        if (task.Due is { } due)
        {
            bool late = !task.Done && due < Today;
            trailing.Children.Add(TaskCard.Chip(
                BoardQuery.DescribeDue(due, Today),
                late ? Color.FromRgb(0xBE, 0x12, 0x3C) : null));
        }

        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 1) };
        DockPanel.SetDock(check, Dock.Left);
        DockPanel.SetDock(trailing, Dock.Right);
        row.Children.Add(check);
        row.Children.Add(trailing);
        row.Children.Add(title);

        row.DoubleTapped += (_, e) =>
        {
            TaskOpened?.Invoke(this, task);
            e.Handled = true;
        };

        return row;
    }
}
