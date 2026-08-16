using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Desktop.Panes;

namespace Puffinote.Desktop.Sidebar;

/// <summary>
/// The document-outline panel (PRD §7.2.3): a keyboard-navigable, indented view of the <em>active</em>
/// document's structure — Markdown headings for a note, the sheet/chart list for a workbook. Selecting an
/// entry (Enter / a single click) navigates the editor to it. The shell refreshes the items from the active
/// pane; this panel only presents them.
/// </summary>
internal sealed class OutlinePanel : ISidebarPanel
{
    private readonly KeyboardListView _list;

    public OutlinePanel()
    {
        _list = new KeyboardListView(new FuncDataTemplate<OutlineItem>((item, _) => BuildRow(item), supportsRecycling: true))
        {
            EmptyText = "No outline for this document.",
        };
        _list.Activated += item => ((OutlineItem)item).Reveal();
    }

    public string Id => "outline";

    public string Title => "Outline";

    public string Icon => "list";

    public Control View => _list;

    /// <summary>False: an outline is a thing you look at, and Down from the tree reaches it anyway.</summary>
    public bool FocusOnActivate => false;

    public void Focus() => _list.FocusList();

    /// <summary>Show <paramref name="items"/> (the active document's structure, or empty).</summary>
    public void SetItems(IReadOnlyList<OutlineItem> items)
    {
        ItemsForTest = items;
        _list.SetItems(items);
    }

    /// <summary>The items currently shown (test seam).</summary>
    internal IReadOnlyList<OutlineItem> ItemsForTest { get; private set; } = [];

    private static Control BuildRow(OutlineItem? item)
    {
        if (item is null)
        {
            return new Control();
        }

        // Indent by heading depth (level 1 = flush left); a flat sheet list (level 0) stays flush too.
        return new TextBlock
        {
            Text = item.Title,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(Math.Max(0, item.Level - 1) * 12, 0, 0, 0),
        };
    }
}
