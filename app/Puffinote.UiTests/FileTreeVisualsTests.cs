using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Controls;
using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// What a file tree row actually says at a glance: its type (glyph + extension pill) and where it sits (indent
/// guides tracing the branch you are in).
/// </summary>
public sealed class FileTreeVisualsTests : IDisposable
{
    private readonly DirectoryInfo _notebook = Directory.CreateTempSubdirectory("puffinote-visuals");

    public void Dispose() => _notebook.Delete(recursive: true);

    private string Path(string relative) => System.IO.Path.Combine(_notebook.FullName, relative);

    private void Touch(string relative)
    {
        var path = Path(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    private (FileTreeView Tree, Window Window) Open()
    {
        // root/{ Projects/Neural Nets/Attention.md, Inbox.md, cover.png }
        Touch("Projects/Neural Nets/Attention.md");
        Touch("Inbox.md");
        Touch("cover.png");

        var tree = new FileTreeView();
        var window = new Window { Width = 320, Height = 500, Content = tree };
        window.Show();
        tree.SetRoot(new NotebookNode(_notebook.FullName, isDirectory: true));
        Settle(window);
        return (tree, window);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The row for the entry called <paramref name="name"/>, by its accessible name.</summary>
    private static ListBoxItem Row(Window window, string name) =>
        window.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Single(i => i.GetVisualDescendants().OfType<Control>().Any(c => AutomationProperties.GetName(c) == name));

    private static IEnumerable<T> In<T>(Visual row) where T : Visual => row.GetVisualDescendants().OfType<T>();

    [AvaloniaFact]
    public void Every_glyph_the_sidebar_asks_for_is_vendored()
    {
        // A missing SVG resolves to null and draws *nothing* — silently. That is how the tree shipped with an
        // invisible chevron on every collapsed folder: the key was referenced, the file was never vendored, and
        // nothing failed. This walks the whole type map plus the sidebar's chrome keys, so a new file type
        // cannot be added without its glyph.
        string[] chrome =
        [
            "chevron-down", "chevron-right", // the disclosure twisty — the one that was missing
            "arrow-left",                    // the "In Folder" bar's back arrow
            "plus",                          // the header's New menu
            "chevrons-up",                   // the header's Collapse all
        ];

        var missing = FileGlyph.AllIconKeys.Concat(chrome)
            .Where(key => LucideIcons.Get(key) is not { } geometry || geometry.Bounds.Width <= 0)
            .ToList();

        Assert.Empty(missing);
    }

    [AvaloniaFact]
    public void A_file_row_shows_its_type_glyph_and_its_extension_in_a_pill()
    {
        var (_, window) = Open();
        var row = Row(window, "cover.png");

        // The glyph follows the type...
        var glyph = In<LucideIcon>(row).Single();
        Assert.Equal(FileGlyph.ForFile("cover.png").IconKey, glyph.IconKey);
        Assert.Equal(FileGlyph.ForFile("cover.png").Color, glyph.Foreground);

        // ...the name sheds its extension...
        var texts = In<TextBlock>(row).Select(t => t.Text).ToList();
        Assert.Contains("cover", texts);
        Assert.DoesNotContain("cover.png", texts);

        // ...and the extension is spelled out in the pill, which is the channel that survives a reader who
        // cannot tell the glyph colours apart.
        Assert.Contains("PNG", texts);
    }

    [AvaloniaFact]
    public void Every_row_shows_its_name_in_ink_that_exists()
    {
        // Regression: a folder's name was built with `Foreground = isDirectory ? null : muted`, meaning to say
        // "folders keep the default brush". Avalonia read the null as a *local* value, so every folder in the
        // tree rendered as a blank row — chevron, glyph, and no name at all. Every model-level test still passed,
        // because the model still knew the name; only a rendered frame showed it missing.
        var (_, window) = Open();

        foreach (var (row, text) in new[] { ("Projects", "Projects"), ("Inbox.md", "Inbox"), ("cover.png", "cover") })
        {
            var name = In<TextBlock>(Row(window, row)).Single(t => t.Text == text);
            Assert.NotNull(name.Foreground); // a null brush paints nothing
        }
    }

    [AvaloniaFact]
    public void A_folder_row_is_not_coloured_like_a_file()
    {
        var (_, window) = Open();
        var folder = In<LucideIcon>(Row(window, "Projects")).First(i => i.IconKey is "folder" or "folder-open");

        Assert.Equal(FileGlyph.Folder, folder.Foreground);
        Assert.NotEqual(Tokens.Dark.Accent, folder.Foreground); // folders never wear the "current thing" hue
    }

    [AvaloniaFact]
    public void A_collapsed_folders_chevron_actually_draws()
    {
        // The regression this whole guard exists for: the tree asked for "chevron-right", nothing was vendored
        // under that name, and every collapsed folder rendered a blank 12px gap.
        var (_, window) = Open();
        var chevron = In<LucideIcon>(Row(window, "Projects")).Single(i => i.IconKey!.StartsWith("chevron", StringComparison.Ordinal));

        Assert.Equal("chevron-right", chevron.IconKey);
        Assert.NotNull(LucideIcons.Get(chevron.IconKey));
    }

    /// <summary>The weight of the row named <paramref name="name"/>'s own name text.</summary>
    private static FontWeight Weight(Window window, string name, string stem) =>
        In<TextBlock>(Row(window, name)).Single(t => t.Text == stem).FontWeight;

    private static IBrush? Ink(Window window, string name, string stem) =>
        In<TextBlock>(Row(window, name)).Single(t => t.Text == stem).Foreground;

    [AvaloniaFact]
    public void Unselected_folders_and_files_are_written_in_the_same_ink()
    {
        // Folders used to keep the brighter inherited brush while files were muted, so a list of folders read
        // as a wall of highlights — the loudest thing on screen was the scaffolding, not the note being read.
        var (tree, window) = Open();
        tree.SetActiveDocument(Path("Inbox.md"));
        Settle(window);

        Assert.Equal(Ink(window, "cover.png", "cover"), Ink(window, "Projects", "Projects"));

        // ...and muted is the level they meet at, not the bright one.
        Assert.Equal(Tokens.Dark.MutedText, Ink(window, "Projects", "Projects"));

        // Every row, the open one included: what marks it is the wash behind it, not its ink.
        Assert.Equal(Tokens.Dark.MutedText, Ink(window, "Inbox.md", "Inbox"));
    }

    /// <summary>The wash painted behind a row, which is what "highlighted" means here.</summary>
    private static IBrush? Wash(Window window, string name) =>
        In<DockPanel>(Row(window, name)).First().Background;

    [AvaloniaFact]
    public void The_document_you_are_reading_is_highlighted_and_never_bold()
    {
        // Weight is deliberately not a signal: bold made the active row wider than the same row inactive, so the
        // mark that moves as you switch notes also shifted its neighbours sideways.
        var (tree, window) = Open();
        tree.SetActiveDocument(Path("Inbox.md"));
        Settle(window);

        Assert.Equal(Tokens.Dark.AccentSubtle, Wash(window, "Inbox.md"));
        Assert.Equal(FontWeight.Normal, Weight(window, "Inbox.md", "Inbox"));

        // Nothing is bold anywhere, so no row's width depends on which note is open.
        Assert.Equal(FontWeight.Normal, Weight(window, "Projects", "Projects"));
        Assert.Equal(FontWeight.Normal, Weight(window, "cover.png", "cover"));

        // The highlight follows the tab you switch to; it never smears across two rows.
        tree.SetActiveDocument(Path("cover.png"));
        Settle(window);

        Assert.Equal(Tokens.Dark.AccentSubtle, Wash(window, "cover.png"));
        Assert.Equal(Brushes.Transparent, Wash(window, "Inbox.md"));

        // Close the last tab and nothing is being read, so nothing is highlighted.
        tree.SetActiveDocument(null);
        Settle(window);

        Assert.Equal(Brushes.Transparent, Wash(window, "cover.png"));
    }

    [AvaloniaFact]
    public void The_highlight_survives_clicking_a_folder_elsewhere_in_the_tree()
    {
        // The highlight is painted from the active document, not from the ListBox selection. Driving it off
        // selection instead would look identical until you clicked anything else — and then the row telling you
        // which note is open would go dark, which is exactly when you want it.
        var (tree, window) = Open();
        tree.SetActiveDocument(Path("Inbox.md"));
        Settle(window);

        tree.SelectPath(Path("Projects"));
        Settle(window);

        Assert.Equal(Tokens.Dark.AccentSubtle, Wash(window, "Inbox.md"));
    }

    /// <summary>What the <see cref="ListBoxItem"/> itself paints — the selection's channel, behind the row.</summary>
    private static (IBrush? Wash, IBrush? Bar) Selection(Window window, string name)
    {
        var presenter = In<ContentPresenter>(Row(window, name)).First(p => p.Name == "PART_ContentPresenter");
        return (presenter.Background, presenter.BorderBrush);
    }

    [AvaloniaFact]
    public void Selecting_one_row_while_reading_another_still_highlights_only_the_open_one()
    {
        // Regression: selection and "open in a tab" both wore the accent wash, so clicking a row in the tree and
        // then switching tabs from the strip above left *two* rows highlighted — the one you last clicked and the
        // one you were actually reading. The tree highlights the active tab's file and nothing else; selection
        // has its own channel, the bar down the leading edge.
        var (tree, window) = Open();
        tree.SelectPath(Path("cover.png"));
        tree.SetActiveDocument(Path("Inbox.md"));
        Settle(window);

        Assert.Equal(Tokens.Dark.AccentSubtle, Wash(window, "Inbox.md"));

        var selected = Selection(window, "cover.png");
        Assert.Equal(Brushes.Transparent, selected.Wash); // no second highlight...
        Assert.Equal(Tokens.Dark.Accent, selected.Bar);   // ...but you can still see where the keyboard is

        // And nothing else in the tree — the ListBox's own selected row included — wears the open document's wash.
        var washed = window.GetVisualDescendants().OfType<ListBoxItem>()
            .Count(row => Equals(In<DockPanel>(row).First().Background, Tokens.Dark.AccentSubtle));
        Assert.Equal(1, washed);
    }

    [AvaloniaFact]
    public void The_open_document_announces_itself_to_a_screen_reader()
    {
        // The wash is the only visual mark left, and colour alone is not a signal (WCAG 2.2 §1.4.1).
        var (tree, window) = Open();
        tree.SetActiveDocument(Path("Inbox.md"));
        Settle(window);

        var active = In<DockPanel>(Row(window, "Inbox.md")).First();
        Assert.Equal("open", AutomationProperties.GetHelpText(active));

        Assert.Null(AutomationProperties.GetHelpText(In<DockPanel>(Row(window, "cover.png")).First()));
    }

    [AvaloniaFact]
    public void Switching_to_a_document_inside_a_shut_folder_opens_the_folder_to_show_it()
    {
        // A bold row inside a collapsed folder is a mark nobody can see. Switching tabs is exactly when you have
        // lost your place, so the tree opens the way down to the document — without moving the selection, which
        // still answers the other question ("what will Rename act on?").
        var (tree, window) = Open();
        Assert.DoesNotContain("Attention.md", tree.VisibleNamesForTest); // Projects/Neural Nets start shut

        tree.SelectPath(Path("Inbox.md"));
        tree.SetActiveDocument(Path("Projects/Neural Nets/Attention.md"));
        Settle(window);

        Assert.Contains("Attention.md", tree.VisibleNamesForTest);
        Assert.Equal(Tokens.Dark.AccentSubtle, Wash(window, "Attention.md"));
        Assert.Equal(Path("Inbox.md"), tree.SelectedNode?.Path); // the reveal did not hijack the selection
    }

    [AvaloniaFact]
    public void The_document_you_switch_to_is_scrolled_to_the_middle_of_the_panel()
    {
        var (tree, window) = OpenLongNotebook();

        // A note far down the list: the panel scrolls it to the middle, where you can see the notes around it.
        tree.SetActiveDocument(Path("note-30.md"));
        Settle(window);

        var scroller = In<ScrollViewer>(window).First();
        var offset = CentreOffset(window, "note-30.md");
        Assert.True(Math.Abs(offset) <= RowHeight, $"row sat {offset}px from the middle of the panel");
        Assert.True(scroller.Offset.Y > 0, "the panel never scrolled");

        // ...and it follows the tab you switch to, back up the list.
        tree.SetActiveDocument(Path("note-20.md"));
        Settle(window);

        Assert.True(Math.Abs(CentreOffset(window, "note-20.md")) <= RowHeight);
    }

    [AvaloniaFact]
    public void A_document_near_the_top_is_shown_without_scrolling_into_empty_space()
    {
        // "Centred where possible" — there is no half-screenful above note-01 to centre it against, so the panel
        // stays at the top rather than dragging the list down past its own beginning.
        var (tree, window) = OpenLongNotebook();
        tree.SetActiveDocument(Path("note-30.md")); // somewhere else first, so this is a real scroll *back*
        Settle(window);

        tree.SetActiveDocument(Path("note-01.md"));
        Settle(window);

        var scroller = In<ScrollViewer>(window).First();
        Assert.Equal(0, scroller.Offset.Y);
        Assert.Equal(Tokens.Dark.AccentSubtle, Wash(window, "note-01.md")); // and it is on screen, highlighted
    }

    /// <summary>How far the row named <paramref name="name"/> sits from the vertical middle of the panel, in px.</summary>
    private static double CentreOffset(Window window, string name)
    {
        var scroller = In<ScrollViewer>(window).First();
        var row = Row(window, name);
        var top = row.TranslatePoint(default, scroller)!.Value.Y;
        return top + (row.Bounds.Height / 2) - (scroller.Viewport.Height / 2);
    }

    /// <summary>One row's height, as the tree lays it out (content plus the container's vertical margin).</summary>
    private const double RowHeight = 28;

    /// <summary>A notebook with far more notes than fit the panel, so scrolling is what's under test.</summary>
    private (FileTreeView Tree, Window Window) OpenLongNotebook()
    {
        for (var i = 0; i < 40; i++)
        {
            Touch($"note-{i:00}.md");
        }

        var tree = new FileTreeView();
        var window = new Window { Width = 320, Height = 300, Content = tree };
        window.Show();
        tree.SetRoot(new NotebookNode(_notebook.FullName, isDirectory: true));
        Settle(window);
        return (tree, window);
    }

    [AvaloniaFact]
    public void Selecting_a_row_is_not_the_same_as_opening_it()
    {
        // Two different questions, tracked separately: selection says "Rename/Delete will act on this", the wash
        // says "this is what you are reading". Clicking a note to rename it must not claim to have opened it —
        // and the note you *are* in must stay highlighted while you do.
        var (tree, window) = Open();
        tree.SetActiveDocument(Path("Inbox.md"));
        tree.SelectPath(Path("cover.png"));
        Settle(window);

        Assert.Equal(Path("cover.png"), tree.SelectedNode?.Path);
        Assert.Equal(FontWeight.Normal, Weight(window, "cover.png", "cover")); // selected, but not open
        Assert.Equal(Tokens.Dark.AccentSubtle, Wash(window, "Inbox.md")); // open, though not selected
    }

    [AvaloniaFact]
    public void The_branch_the_selection_sits_in_is_traced_in_the_accent()
    {
        // The trace is the accent *hue*, dimmed (a full-strength bar at every level of a deep selection
        // shouts down the names), so this compares colour rather than brush identity.
        var accent = ((ISolidColorBrush)Tokens.Dark.Accent).Color;
        static Color? Hue(Border guide) => (guide.BorderBrush as ISolidColorBrush)?.Color;

        var (tree, window) = Open();
        tree.SelectPath(Path("Projects/Neural Nets/Attention.md"));
        Settle(window);

        // Attention.md is two levels deep, so it has a guide per ancestor — and the selection is inside both.
        var guides = In<Border>(Row(window, "Attention.md")).Where(b => b.Name == "PART_TreeGuide").ToList();
        Assert.Equal(2, guides.Count);
        Assert.All(guides, g => Assert.Equal(accent, Hue(g)));

        // Move the selection out of that branch and the trace goes with it — the guides stay, but go quiet.
        tree.SelectPath(Path("Inbox.md"));
        Settle(window);

        var after = In<Border>(Row(window, "Attention.md")).Where(b => b.Name == "PART_TreeGuide").ToList();
        Assert.NotEmpty(after);
        Assert.All(after, g => Assert.NotEqual(accent, Hue(g)));
    }

    [AvaloniaFact]
    public void Collapse_all_shuts_every_folder_but_keeps_the_top_level()
    {
        var (tree, window) = Open();
        tree.SelectPath(Path("Projects/Neural Nets/Attention.md"));
        Settle(window);
        Assert.Contains("Attention.md", tree.VisibleNamesForTest);

        tree.CollapseAll();
        Settle(window);

        // Everything nested is gone; the notebook's own top level is still listed (collapsing to an empty
        // panel would just look broken).
        Assert.DoesNotContain("Attention.md", tree.VisibleNamesForTest);
        Assert.DoesNotContain("Neural Nets", tree.VisibleNamesForTest);
        Assert.Contains("Projects", tree.VisibleNamesForTest);
        Assert.Contains("Inbox.md", tree.VisibleNamesForTest);
    }

    /// <summary>
    /// Expand all is Collapse all's inverse, and reaches folders that are not on screen to be clicked — the whole
    /// point of the command is the ones you cannot see.
    /// </summary>
    [AvaloniaFact]
    public void Expand_all_opens_folders_the_collapsed_tree_was_not_even_showing()
    {
        var (tree, window) = Open();
        tree.CollapseAll();
        Settle(window);
        Assert.DoesNotContain("Neural Nets", tree.VisibleNamesForTest); // nested, and its parent is shut

        tree.ExpandAll();
        Settle(window);

        Assert.Contains("Projects", tree.VisibleNamesForTest);
        Assert.Contains("Neural Nets", tree.VisibleNamesForTest);
        Assert.Contains("Attention.md", tree.VisibleNamesForTest); // two levels down, never once clicked
    }

    /// <summary>
    /// The tree must land in exactly the same place every time it is rebuilt — and it did not.
    ///
    /// <para>A vertical margin on the row <em>container</em> is accumulated by Avalonia's virtualizing panel each
    /// time the items are replaced: every rebuild added one row's top margin to the extent and slid the whole
    /// tree a pixel further down, for good. Collapse all four times and the tree sat four pixels lower than it
    /// started, with a growing band of dead space above the first row. The spacing now lives on the presenter
    /// inside the container, which measures the same and does not accumulate.</para>
    ///
    /// <para>This pins both the header's button and the plain rebuilds — refresh, tab switch — that drifted just
    /// as badly and are much easier to regress.</para>
    /// </summary>
    [AvaloniaFact]
    public void Rebuilding_the_tree_never_drifts_it_downwards()
    {
        for (var i = 0; i < 14; i++)
        {
            Touch($"Projects/Neural Nets/Note{i:00}.md"); // enough rows that the panel really has to scroll
        }

        var (tree, window) = Open();

        double TopRowY() => window.GetVisualDescendants().OfType<ListBoxItem>()
            .Min(item => item.TranslatePoint(default, window)!.Value.Y);
        double Extent() => window.GetVisualDescendants().OfType<ScrollViewer>().First().Extent.Height;

        // Open the deep folders and scroll away from the top — the state the drift used to be seeded from.
        tree.SelectPath(Path("Projects/Neural Nets/Note09.md"));
        Settle(window);

        tree.CollapseAll();
        Settle(window);
        var top = TopRowY();
        var extent = Extent();

        for (var click = 0; click < 5; click++)
        {
            tree.CollapseAll();
            Settle(window);
            Assert.Equal(top, TopRowY(), 3);
            Assert.Equal(extent, Extent(), 3);
        }

        for (var refresh = 0; refresh < 5; refresh++)
        {
            tree.Refresh();
            Settle(window);
            Assert.Equal(top, TopRowY(), 3);
            Assert.Equal(extent, Extent(), 3);
        }

        // Round-tripping through a fully expanded tree must also come back to the same top.
        tree.ExpandAll();
        Settle(window);
        tree.CollapseAll();
        Settle(window);
        Assert.Equal(top, TopRowY(), 3);
        Assert.Equal(extent, Extent(), 3);
    }
}
