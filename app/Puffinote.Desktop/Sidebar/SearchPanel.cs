using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Search;

namespace Puffinote.Desktop.Sidebar;

/// <summary>
/// The notebook-wide search panel (PRD §7.2.3): a query box over a keyboard-navigable result list, with a
/// status line showing background-index progress. Typing re-runs the search against the shell's
/// <see cref="SearchFunc"/>; Down moves from the box into the results; Enter opens the selected hit.
/// </summary>
internal sealed class SearchPanel : ISidebarPanel
{
    private readonly TextBox _search;
    private readonly TextBlock _status;
    private readonly KeyboardListView _results;
    private readonly Control _root;
    private string _indexStatus = string.Empty;
    private IReadOnlyList<SearchHit> _lastHits = Array.Empty<SearchHit>();

    public SearchPanel()
    {
        var palette = Tokens.Dark;

        _search = new TextBox { PlaceholderText = "Search the notebook…", Margin = new Thickness(8, 0, 8, 6) };
        _search.TextChanged += (_, _) => RunSearch();
        _search.KeyDown += OnSearchKeyDown;

        _status = new TextBlock
        {
            Foreground = palette.MutedText,
            FontSize = 11,
            Margin = new Thickness(12, 0, 12, 6),
        };

        _results = new KeyboardListView(new FuncDataTemplate<SearchHit>((hit, _) => BuildRow(hit, palette), supportsRecycling: true));
        _results.Activated += item => FileActivated?.Invoke(((SearchHit)item).Path);

        var body = new DockPanel();
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(_status, Dock.Top);
        body.Children.Add(_search);
        body.Children.Add(_status);
        body.Children.Add(_results);
        _root = body;
    }

    public string Id => "search";

    public string Title => "Search";

    public Control View => _root;

    /// <summary>Raised when a result is activated (Enter / double-click), to open it in a tab.</summary>
    public event Action<string>? FileActivated;

    /// <summary>The shell's search function (queries the background index).</summary>
    public Func<string, IReadOnlyList<SearchHit>>? SearchFunc { get; set; }

    public void Focus() => _search.Focus();

    /// <summary>The current result set (test seam).</summary>
    internal IReadOnlyList<SearchHit> ResultsForTest => _lastHits;

    /// <summary>Set the background-index status line (e.g. "Indexing…" / "142 files indexed").</summary>
    public void SetIndexStatus(string text)
    {
        _indexStatus = text;
        RunSearch(); // re-query (the index may have changed) and repaint the status
    }

    private void RunSearch()
    {
        var query = (_search.Text ?? string.Empty).Trim();
        var hits = query.Length == 0
            ? Array.Empty<SearchHit>()
            : SearchFunc?.Invoke(query) ?? Array.Empty<SearchHit>();
        _lastHits = hits;
        _results.SetItems(hits);

        _status.Text = query.Length == 0
            ? _indexStatus
            : hits.Count == 0 ? "No matches" : $"{hits.Count} result{(hits.Count == 1 ? "" : "s")}";
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _results.FocusList(); // step from the box into the results
                e.Handled = true;
                break;
            case Key.Enter:
                if (_results.SelectedItem is SearchHit hit)
                {
                    FileActivated?.Invoke(hit.Path);
                }

                e.Handled = true;
                break;
        }
    }

    private static Control BuildRow(SearchHit? hit, ThemePalette palette)
    {
        if (hit is null)
        {
            return new Control();
        }

        return new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = hit.RelativePath,
                    FontWeight = FontWeight.Medium,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                new TextBlock
                {
                    Text = hit.Snippet,
                    Foreground = palette.MutedText,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    IsVisible = !string.IsNullOrEmpty(hit.Snippet),
                },
            },
        };
    }
}
