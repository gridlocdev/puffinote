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
