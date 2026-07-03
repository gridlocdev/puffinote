using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
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

    private readonly ListBox _list;
    private readonly TextBlock _empty;
    private readonly Panel _root;
    private string? _notebookRoot;

    public RecentPanel()
    {
        var palette = Tokens.Dark;

        _list = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            ItemTemplate = new FuncDataTemplate<Entry>((entry, _) => BuildRow(entry, palette), supportsRecycling: true),
        };
        _list.DoubleTapped += (_, _) => ActivateSelected();
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ActivateSelected();
                e.Handled = true;
            }
        };

        _empty = new TextBlock
        {
            Text = "No recent files yet.",
            Foreground = palette.MutedText,
            Margin = new Thickness(12, 8),
            IsVisible = true,
        };

        _root = new Panel { Children = { _list, _empty } };
    }

    public string Id => "recent";

    public string Title => "Recent";

    public Control View => _root;

    /// <summary>Raised when a recent entry is activated (double-click / Enter), to open it in a tab.</summary>
    public event Action<string>? FileActivated;

    public void Focus() => _list.Focus();

    /// <summary>
    /// Show <paramref name="paths"/> (already ordered most-recent first), rendered relative to
    /// <paramref name="notebookRoot"/> when possible.
    /// </summary>
    public void SetItems(string? notebookRoot, IReadOnlyList<string> paths)
    {
        _notebookRoot = notebookRoot;
        _list.ItemsSource = paths.Select(ToEntry).ToList();
        _empty.IsVisible = paths.Count == 0;
        _list.IsVisible = paths.Count > 0;
    }

    private Entry ToEntry(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        if (_notebookRoot is { } root && folder.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            folder = folder[root.Length..].TrimStart(System.IO.Path.DirectorySeparatorChar, '/');
        }

        return new Entry(path, name, folder);
    }

    private void ActivateSelected()
    {
        if (_list.SelectedItem is Entry entry)
        {
            FileActivated?.Invoke(entry.Path);
        }
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
