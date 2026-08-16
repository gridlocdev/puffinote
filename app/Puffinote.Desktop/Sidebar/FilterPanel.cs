using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Sidebar;

/// <summary>
/// The Filter panel (PRD §7.2.3): a box under the switcher row, and beneath it <b>the file tree</b> — the same
/// tree the Files panel shows, narrowed to the entries whose names contain what you have typed
/// (<see cref="FileTreeFilter"/>, which is where the matching rule is written down). Rows, glyphs, indent
/// guides, the keyboard map, the context menu and drag are all the tree's own, because it <em>is</em> the
/// tree: this panel filters it rather than reimplementing it, so anything you can do to a file in Files you
/// can do to it here.
///
/// <para>Why a panel of its own rather than a box bolted to the Files panel: filtering is a way of looking at
/// the notebook, which is what the switcher row is for, and it means the unfiltered tree is still one click
/// away with its scroll position and its open folders intact. It is <em>not</em> notebook search — that reads
/// the insides of your files and lists hits. This reads names, and what it gives back is a tree.</para>
///
/// <para>Empty box, whole notebook: a filter nobody has typed into hides nothing, so the panel opens showing
/// exactly what Files shows.</para>
/// </summary>
internal sealed class FilterPanel : IFileTreePanel
{
    /// <summary>Long enough to swallow a fast typist's keystrokes, short enough that the tree feels live.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    private readonly FilesPanel _files = new();
    private readonly TextBox _query;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _debounce;

    /// <summary>Guards against a slow walk landing after a newer one has already answered.</summary>
    private int _generation;

    public FilterPanel()
    {
        var palette = Tokens.Active;

        _query = new TextBox { PlaceholderText = "Filter by name…", Margin = new Thickness(8, 0, 8, 6) };
        _query.TextChanged += (_, _) => ScheduleFilter();
        _query.KeyDown += OnQueryKeyDown;

        // Hidden while it has nothing to say, rather than reserving a blank line above the tree: with no
        // query there is no count, and the tree should start where the Files panel's does.
        _status = new TextBlock
        {
            Foreground = palette.MutedText,
            FontSize = 11,
            Margin = new Thickness(12, 0, 12, 6),
            IsVisible = false,
        };

        // Coalesce keystrokes: each one costs a walk of the whole notebook (below), and a filter that
        // re-walked on every character would make a large notebook type like treacle.
        _debounce = new DispatcherTimer { Interval = Debounce };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = ApplyAsync();
        };

        var body = new DockPanel();
        DockPanel.SetDock(_query, Dock.Top);
        DockPanel.SetDock(_status, Dock.Top);
        body.Children.Add(_query);
        body.Children.Add(_status);
        body.Children.Add(_files.View);
        View = body;
    }

    public string Id => "filter";

    public string Title => "Filter";

    public string Icon => "filter";

    public Control View { get; }

    // The tree's own events, straight through: this panel owns the box, not the tree's behaviour. Delegated
    // rather than re-raised so a handler added here is added to the tree itself — there is no second list of
    // subscribers to keep in step, and nothing to forget to forward.
    public event Action<string>? FileActivated
    {
        add => _files.FileActivated += value;
        remove => _files.FileActivated -= value;
    }

    public event Action<NotebookNode?>? SelectionChanged
    {
        add => _files.SelectionChanged += value;
        remove => _files.SelectionChanged -= value;
    }

    public event Action<NotebookNode>? RenameRequested
    {
        add => _files.RenameRequested += value;
        remove => _files.RenameRequested -= value;
    }

    public event Action<NotebookNode>? DeleteRequested
    {
        add => _files.DeleteRequested += value;
        remove => _files.DeleteRequested -= value;
    }

    public event Action<NotebookNode?>? NewNoteRequested
    {
        add => _files.NewNoteRequested += value;
        remove => _files.NewNoteRequested -= value;
    }

    public event Action<NotebookNode?>? NewSpreadsheetRequested
    {
        add => _files.NewSpreadsheetRequested += value;
        remove => _files.NewSpreadsheetRequested -= value;
    }

    public event Action<NotebookNode?>? NewTaskboardRequested
    {
        add => _files.NewTaskboardRequested += value;
        remove => _files.NewTaskboardRequested -= value;
    }

    public event Action<NotebookNode?>? NewFileRequested
    {
        add => _files.NewFileRequested += value;
        remove => _files.NewFileRequested -= value;
    }

    public event Action<NotebookNode?>? NewFolderRequested
    {
        add => _files.NewFolderRequested += value;
        remove => _files.NewFolderRequested -= value;
    }

    public event Action<NotebookNode, string>? MoveRequested
    {
        add => _files.MoveRequested += value;
        remove => _files.MoveRequested -= value;
    }

    public event Action<NotebookNode>? RevealRequested
    {
        add => _files.RevealRequested += value;
        remove => _files.RevealRequested -= value;
    }

    public event Action<NotebookNode>? OpenExternallyRequested
    {
        add => _files.OpenExternallyRequested += value;
        remove => _files.OpenExternallyRequested -= value;
    }

    public event Action? ExpansionChanged
    {
        add => _files.ExpansionChanged += value;
        remove => _files.ExpansionChanged -= value;
    }

    public NotebookNode? SelectedNode => _files.SelectedNode;

    public bool AnyFolderExpanded => _files.AnyFolderExpanded;

    public bool HasExpandableFolder => _files.HasExpandableFolder;

    public bool ShowExtensions
    {
        get => _files.ShowExtensions;
        set => _files.ShowExtensions = value;
    }

    /// <summary>The box takes focus, not the tree: you came here to type.</summary>
    public void Focus() => _query.Focus();

    /// <summary>
    /// A new notebook empties the box. The filter was typed about the notebook you had open, and carrying it
    /// into the next one would open it on a tree with most of itself missing for reasons off screen.
    /// </summary>
    public void SetRoot(NotebookNode? root)
    {
        _query.Text = string.Empty;
        _debounce.Stop();
        _generation++;
        _files.SetRoot(root);
        _files.ApplyFilter(null);
        RenderStatus(null);
    }

    /// <summary>
    /// Re-read the tree, then re-run the filter over it: a file that has just been created, renamed or moved
    /// has a new name to be judged by, and one that was deleted must stop being kept.
    /// </summary>
    public void Refresh()
    {
        _files.Refresh();
        if (CurrentQuery.Length > 0)
        {
            ScheduleFilter();
        }
    }

    public void SetActiveDocument(string? path) => _files.SetActiveDocument(path);

    public void CollapseAll() => _files.CollapseAll();

    public void ExpandAll() => _files.ExpandAll();

    /// <summary>The filter box's text (test seam — and where the query is read from, trimmed).</summary>
    internal string QueryForTest
    {
        get => _query.Text ?? string.Empty;
        set => _query.Text = value;
    }

    /// <summary>The line under the box: the match count, or nothing at all (test seam).</summary>
    internal string StatusForTest => _status.IsVisible ? _status.Text ?? string.Empty : string.Empty;

    /// <summary>The names of the rows the filtered tree is showing, top to bottom (test seam).</summary>
    internal IReadOnlyList<string> VisibleNamesForTest => _files.TreeVisibleNamesForTest;

    /// <summary>Apply the typed filter now, without waiting out the debounce (test seam).</summary>
    internal Task ApplyFilterNowForTest()
    {
        _debounce.Stop();
        return ApplyAsync();
    }

    private string CurrentQuery => (_query.Text ?? string.Empty).Trim();

    private void ScheduleFilter()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>
    /// Walk the notebook off the UI thread and hand the answer to the tree on it. The walk is a recursive,
    /// stat-per-entry read of every folder under the scope — the same shape of work the search index does off
    /// the thread for the same reason — while <see cref="FileTreeFilter"/> itself touches nothing but paths,
    /// which is what makes moving it safe.
    /// </summary>
    private async Task ApplyAsync()
    {
        var query = CurrentQuery;
        var generation = ++_generation;

        if (query.Length == 0)
        {
            _files.ApplyFilter(null);
            RenderStatus(null);
            return;
        }

        var root = _files.DisplayRoot;
        var filter = await Task.Run(() => FileTreeFilter.Build(root, query));
        if (generation != _generation)
        {
            return; // a later keystroke has already asked a newer question
        }

        _files.ApplyFilter(filter);
        RenderStatus(filter);
    }

    /// <summary>
    /// What the count line says. "No matches" earns its line: an empty tree with nothing above it reads as a
    /// notebook that has lost its files, and this is the one thing that says otherwise.
    /// </summary>
    private void RenderStatus(FileTreeFilter? filter)
    {
        if (filter is null)
        {
            _status.IsVisible = false;
            _status.Text = string.Empty;
            return;
        }

        _status.Text = filter.MatchCount switch
        {
            0 => "No matches",
            1 => "1 match",
            var n => $"{n} matches",
        };
        _status.IsVisible = true;
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _files.Focus(); // step from the box into the rows it filtered
                e.Handled = true;
                break;

            // Clear the filter without reaching for the mouse or holding Backspace down — and only that: the
            // sidebar is not a dialog, so there is nothing here for Escape to close.
            case Key.Escape when _query.Text is { Length: > 0 }:
                _query.Text = string.Empty;
                e.Handled = true;
                break;
        }
    }
}
