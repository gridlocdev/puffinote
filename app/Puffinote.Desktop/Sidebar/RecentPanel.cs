using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Theming;

namespace Puffinote.Desktop.Sidebar;

/// <summary>
/// The recent-files panel (PRD §7.2.3): a keyboard-navigable list of the notebook's recently opened documents,
/// most-recent first. Selecting one (Enter / double-click) opens it into a tab. The shell owns the recency
/// list (via the shared <c>RecentFiles</c> rule); this panel only presents it.
/// </summary>
internal sealed class RecentPanel : ISidebarPanel
{
    private sealed record Entry(string Path, string Name, string Folder);

    private readonly KeyboardListView _list;
    private string? _notebookRoot;

    public RecentPanel()
    {
        var palette = Tokens.Active;
        _list = new KeyboardListView(new FuncDataTemplate<Entry>((entry, _) => BuildRow(entry, palette), supportsRecycling: true))
        {
            EmptyText = "No recent files yet.",
        };
        _list.Activated += item => FileActivated?.Invoke(((Entry)item).Path);
    }

    public string Id => "recent";

    public string Title => "Recent";

    public string Icon => "history";

    public Control View => _list;

    /// <summary>Raised when a recent entry is activated (double-click / Enter), to open it in a tab.</summary>
    public event Action<string>? FileActivated;

    public void Focus() => _list.FocusList();

    /// <summary>
    /// Show <paramref name="paths"/> (already ordered most-recent first), rendered relative to
    /// <paramref name="notebookRoot"/> when possible.
    /// </summary>
    public void SetItems(string? notebookRoot, IReadOnlyList<string> paths)
    {
        _notebookRoot = notebookRoot;
        _list.SetItems(paths.Select(ToEntry).ToList());
    }

    private Entry ToEntry(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        return new Entry(path, name, Shell.NotebookPaths.RelativeTo(_notebookRoot, folder));
    }

    private static Control BuildRow(Entry? entry, ThemePalette palette)
    {
        if (entry is null)
        {
            return new Control();
        }

        return new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = entry.Name, FontWeight = FontWeight.Medium },
                new TextBlock
                {
                    Text = entry.Folder,
                    Foreground = palette.MutedText,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    IsVisible = !string.IsNullOrEmpty(entry.Folder),
                },
            },
        };
    }
}
