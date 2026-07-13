using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
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
/// What a file tree row actually says at a glance: its type (glyph + extension pill), where it sits (indent
/// guides tracing the branch you are in), and whether its folder is pinned (the star).
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
            "star",                          // pin a folder
            "x",                             // unpin a chip
            "layers",                        // the "All notes" chip
            "file-plus",                     // the header's New note
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

    [AvaloniaFact]
    public void Only_the_document_you_are_reading_is_bold_and_folders_never_are()
    {
        // Bold used to mean "is a folder", which put the tree's loudest mark on its most numerous rows and left
        // the one row you actually care about — the note in front of you — indistinguishable from every other file.
        var (tree, window) = Open();
        tree.SetActiveDocument(Path("Inbox.md"));
        Settle(window);

        Assert.Equal(FontWeight.SemiBold, Weight(window, "Inbox.md", "Inbox"));
        Assert.Equal(FontWeight.Normal, Weight(window, "Projects", "Projects")); // a folder is a folder, not a shout
        Assert.Equal(FontWeight.Normal, Weight(window, "cover.png", "cover"));

        // The bold follows the tab you switch to; it never smears across two rows.
        tree.SetActiveDocument(Path("cover.png"));
        Settle(window);

        Assert.Equal(FontWeight.SemiBold, Weight(window, "cover.png", "cover"));
        Assert.Equal(FontWeight.Normal, Weight(window, "Inbox.md", "Inbox"));

        // Close the last tab and nothing is being read, so nothing is bold.
        tree.SetActiveDocument(null);
        Settle(window);

        Assert.Equal(FontWeight.Normal, Weight(window, "cover.png", "cover"));
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
        Assert.Equal(FontWeight.SemiBold, Weight(window, "Attention.md", "Attention"));
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
        Assert.Equal(FontWeight.SemiBold, Weight(window, "note-01.md", "note-01")); // and it is on screen, bold
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
    public void Selecting_a_row_is_not_the_same_as_opening_it_and_only_one_of_them_is_bold()
    {
        // Two different questions get two different marks: the accent bar says "Rename/Delete will act on this",
        // weight says "this is what you are reading". ⌘-clicking a note to rename it must not claim to have
        // opened it — and the note you *are* in must stay bold while you do.
        var (tree, window) = Open();
        tree.SetActiveDocument(Path("Inbox.md"));
        tree.SelectPath(Path("cover.png"));
        Settle(window);

        Assert.Equal(Path("cover.png"), tree.SelectedNode?.Path);
        Assert.Equal(FontWeight.Normal, Weight(window, "cover.png", "cover")); // selected, but not open
        Assert.Equal(FontWeight.SemiBold, Weight(window, "Inbox.md", "Inbox")); // open, though not selected
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
    public void A_folders_star_pins_it_and_then_reads_as_pinned_without_relying_on_colour()
    {
        var (tree, window) = Open();
        NotebookNode? pinned = null;
        tree.PinRequested += node => pinned = node;

        var star = In<Button>(Row(window, "Projects")).Single();
        Assert.False(In<LucideIcon>(star).Single().IsFilled); // unpinned: a hollow star
        star.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(Path("Projects"), pinned?.Path);

        // Once the shell reports it back as pinned, the star fills in — a solid shape, not merely a warmer
        // colour, so the state survives a reader who cannot see the accent (WCAG 2.2 §1.4.1).
        tree.SetPinnedPaths(new HashSet<string>([Path("Projects")], StringComparer.OrdinalIgnoreCase));
        Settle(window);

        var after = In<LucideIcon>(In<Button>(Row(window, "Projects")).Single()).Single();
        Assert.True(after.IsFilled);
        Assert.Equal(1, In<Button>(Row(window, "Projects")).Single().Opacity); // and it stays visible unhovered
    }

    [AvaloniaFact]
    public void The_star_appears_on_hover_and_survives_the_pointer_reaching_it()
    {
        // The star is revealed by the *row's* PointerEntered/Exited. If those routed up from the star itself,
        // moving onto the star would fire the row's Exited and hide the very control under the cursor — a target
        // you can see but never quite reach.
        var (_, window) = Open();
        var row = Row(window, "Projects");
        var star = In<Button>(row).Single();
        Assert.Equal(0, star.Opacity); // at rest, an unpinned folder shows no star

        Point Centre(Visual v) => v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), window)!.Value;

        window.MouseMove(Centre(row));
        Settle(window);
        Assert.Equal(1, star.Opacity); // hovering the row reveals it

        window.MouseMove(Centre(star));
        Settle(window);
        Assert.Equal(1, star.Opacity); // ...and it is still there when the pointer arrives

        window.MouseMove(new Point(1, window.Height - 1)); // leave the row entirely
        Settle(window);
        Assert.Equal(0, star.Opacity);
    }

    [AvaloniaFact]
    public void Clicking_the_star_does_not_also_open_the_folder()
    {
        // The star sits inside the row. Without a guard the same press would toggle the folder underneath it.
        var (tree, window) = Open();
        tree.SelectPath(Path("Projects/Neural Nets/Attention.md"));
        Settle(window);
        var before = tree.VisibleNamesForTest.ToList();

        In<Button>(Row(window, "Projects")).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        Assert.Equal(before, tree.VisibleNamesForTest);
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
}
