using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Pivotask.Control.Commands;
using Pivotask.Core;
using Pivotask.Core.Model;
using Pivotask.Core.Query;
using Puffinote.Components.Commands;
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
    private readonly TextBox _search = new() { PlaceholderText = "Search", Width = 200 };
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, Spacing = 4 };

    public TaskBoardView()
    {
        BoardForTest.TaskOpened += (_, task) => _ = OpenTaskAsync(task);
        ListForTest.TaskOpened += (_, task) => _ = OpenTaskAsync(task);

        _search.TextChanged += (_, _) =>
        {
            BoardForTest.Query = _search.Text ?? string.Empty;
            ListForTest.Query = _search.Text ?? string.Empty;
        };

        var strip = new DockPanel { LastChildFill = false, Margin = new Thickness(12, 8) };
        DockPanel.SetDock(_filters, Dock.Left);
        DockPanel.SetDock(_search, Dock.Right);
        strip.Children.Add(_filters);
        strip.Children.Add(_search);
        strip.Children.Add(BuildLayoutSwitch());

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(strip, Dock.Top);
        root.Children.Add(strip);
        root.Children.Add(_surface);

        Content = new Panel { Children = { root, OverlayForTest } };
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

    private void ApplyLayout()
    {
        _surface.Content = Layout == BoardLayout.Board ? BoardForTest : ListForTest;
        BuildFilters();
    }

    private void BuildFilters()
    {
        _filters.Children.Clear();
        foreach (var (filter, label) in new[]
                 {
                     (TaskFilter.All, "All"),
                     (TaskFilter.Starred, "Starred"),
                     (TaskFilter.DueSoon, "Due soon"),
                     (TaskFilter.Unfinished, "Unfinished"),
                 })
        {
            var button = new Button
            {
                Content = label,

                // The active filter carries the accent underline the family uses for "this is the
                // current one" — the same mark Cellestra's active sheet tab wears.
                BorderThickness = new Thickness(0, 0, 0, 2),
                BorderBrush = Filter == filter ? Tokens.Dark.Accent : Brushes.Transparent,
                Background = Brushes.Transparent,
            };

            var captured = filter;
            button.Click += (_, _) => Filter = captured;
            _filters.Children.Add(button);
        }
    }

    private Avalonia.Controls.Control BuildLayoutSwitch()
    {
        var toggle = new Button { Content = "Board / List", Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(toggle, Dock.Left);
        toggle.Click += (_, _) => Layout = Layout == BoardLayout.Board ? BoardLayout.List : BoardLayout.Board;
        return toggle;
    }
}
