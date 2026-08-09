using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Controls.Icons;

namespace Typage.Control.Controls;

/// <summary>
/// The Markdown editor's formatting toolbar, on the family's shared <see cref="AdaptiveCommandBar"/> shell —
/// the same reflow/overflow, collapse handle, and search pill the spreadsheet uses. Every formatting command
/// in the editor's <see cref="AppCommand"/> catalog is surfaced: the common ones as icon tools, the rest as
/// labeled rows behind each group's dropdown (headings and insert are dropdowns in full). Chrome brushes come
/// from the host's <c>ChromeBackground</c>/<c>ChromeBorder</c> resources, so it follows the shell's theme.
/// </summary>
internal sealed class MarkdownCommandBar : UserControl
{
    /// <summary>
    /// The command palette's chord, rendered for this platform (⌘K / Ctrl+K). It is the family's one reserved
    /// global key rather than a command of ours, so it isn't in the editor's catalog to look up — both shells
    /// bind it, and <see cref="Commands.MarkdownEditingCommands"/> is written around leaving it to them.
    /// </summary>
    private static readonly string PaletteShortcut = ShortcutFormatter.Format(Shortcut.Cmd(Key.K));

    private readonly Dictionary<string, AppCommand> _byId;
    private readonly Dictionary<string, string> _sectionOf;
    private readonly List<CommandTool> _tools = [];
    private readonly AdaptiveCommandBar _bar;

    /// <summary>Raised when the user shows, collapses, or hides the bar, so the host can persist the choice.</summary>
    public event EventHandler? VisibilityChanged;

    public MarkdownCommandBar(IReadOnlyList<AppCommand> commands)
    {
        _byId = commands.ToDictionary(c => c.Id);

        // Each group's whole toolset — the inline primary icons are the first three; the rest fall into the
        // group's dropdown. Headings and Insert stay pure dropdowns: a labelled picker names what you're
        // choosing, which an icon row can't do for six near-identical headings or a handful of one-shot
        // inserts. The union across groups is every formatting command the bar surfaces.
        string[] text = ["format.bold", "format.italic", "format.strike", "format.highlight", "format.code"];
        string[] headings = ["format.h1", "format.h2", "format.h3", "format.h4", "format.h5", "format.h6", "format.paragraph"];
        // The three list *kinds* are the primary icons; the box's status picker and its removal fall into the
        // dropdown — you reach for "make this a task" far more often than for "this one is blocked".
        string[] lists = ["format.bulletList", "format.numberedList", "format.task", "format.taskStatus", "format.removeTask"];

        // Blockquote sits with the inserts, not the lists. It shares nothing with a list but the fact that it
        // prefixes lines: it doesn't nest, doesn't renumber, and has no item to add — it wraps a passage.
        string[] insert = ["format.link", "format.editLink", "insert.image", "insert.codeBlock", "insert.hr", "format.quote"];

        _sectionOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (group, ids) in new[] { ("Text", text), ("Headings", headings), ("Lists", lists), ("Insert", insert) })
        {
            foreach (var id in ids)
            {
                _sectionOf[id] = group;
            }
        }

        SurfacedIdsForTest = new HashSet<string>(_sectionOf.Keys);

        var sections = new List<AdaptiveCommandBar.Section>
        {
            Section("Text", 0, IconGroup("Text", text[..3], text[3..]), text),
            Section("Headings", 1, Picker("Heading", headings), headings),
            Section("Lists", 2, IconGroup("Lists", lists[..3], lists[3..]), lists),
            Section("Insert", 3, Picker("Insert", insert), insert),
        };

        // Worded exactly as the spreadsheet's pill, down to the chord beside it — it is the same affordance in
        // the same corner of the same bar, so it should read the same. The chord is the Command Menu's: ⌘K
        // opens the host's merged one, a superset of the flyout this pill drops (see PaletteShortcut).
        var search = new AdaptiveCommandBar.SearchPill(
            "Search commands…", PaletteShortcut, "Open the Command Menu — search and run any command",
            Flyout: BuildSearchFlyout(commands));
        _bar = new AdaptiveCommandBar(sections, search);
        _bar.VisibilityChanged += (_, _) => VisibilityChanged?.Invoke(this, EventArgs.Empty);
        Content = _bar;
    }

    /// <summary>How much of the bar shows: full, slim handle, or nothing (persisted by the host).</summary>
    public CommandBarVisibility BarVisibility
    {
        get => _bar.BarVisibility;
        set => _bar.BarVisibility = value;
    }

    /// <summary>Every command id the bar surfaces (inline or in a dropdown), across all groups (test seam).</summary>
    internal IReadOnlySet<string> SurfacedIdsForTest { get; }

    /// <summary>The group a surfaced command belongs to ("Text" / "Headings" / "Lists" / "Insert"), or null (test seam).</summary>
    internal string? SectionOfForTest(string id) => _sectionOf.GetValueOrDefault(id);

    /// <summary>Re-evaluate each inline tool's lit (toggle-active) state — the host calls this as the caret moves.</summary>
    public void RefreshState()
    {
        if (BarVisibility != CommandBarVisibility.Visible)
        {
            return; // collapsed or hidden — no tool is on screen to light, so don't scan tokens for nobody
        }

        foreach (var tool in _tools)
        {
            tool.UpdateState();
        }
    }

    private AppCommand[] Resolve(IEnumerable<string> ids) => [.. ids.Where(_byId.ContainsKey).Select(id => _byId[id])];

    private AdaptiveCommandBar.Section Section(string name, int rank, Avalonia.Controls.Control inline, string[] foldedIds) =>
        new(name, inline, FoldedSection(name, Resolve(foldedIds)), rank);

    /// <summary>An inline group of primary icon tools, plus a chevron dropdown for its overflow commands.</summary>
    private Avalonia.Controls.Control IconGroup(string name, string[] primaryIds, string[] drawerIds)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var command in Resolve(primaryIds))
        {
            var tool = new CommandTool(command);
            _tools.Add(tool); // tracked so RefreshState can light it when its format is active at the caret
            row.Children.Add(tool);
        }

        var drawer = Resolve(drawerIds);
        if (drawer.Length > 0)
        {
            row.Children.Add(Chevron(DrawerFlyout(drawer), $"More {name.ToLowerInvariant()} tools"));
        }

        return Divider(row);
    }

    /// <summary>A labeled dropdown (e.g. "Heading ▾") whose whole toolset lives behind it — for icon-less command families.</summary>
    private Avalonia.Controls.Control Picker(string label, string[] drawerIds)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center },
                new LucideIcon { IconKey = "chevron-down", Size = 13, Foreground = CommandBarChrome.MutedBrush, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderThickness = default,
            Padding = new Thickness(8, 4),
            Height = 34,
            Focusable = false,
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center,
            Flyout = DrawerFlyout(Resolve(drawerIds)),
        };
        CommandBarChrome.AddHover(button);
        ToolTip.SetTip(button, $"{label}…");
        return Divider(button);
    }

    private static Button Chevron(Flyout flyout, string tip)
    {
        var button = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = default,
            Padding = new Thickness(2),
            Width = 18,
            Height = 34,
            Focusable = false,
            Content = new LucideIcon { IconKey = "chevron-down", Size = 13, Foreground = CommandBarChrome.MutedBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6),
            Flyout = flyout,
        };
        CommandBarChrome.AddHover(button);
        ToolTip.SetTip(button, tip);
        return button;
    }

    /// <summary>A dropdown of labeled command rows (icon + title + shortcut), dismissing itself on a pick.</summary>
    private static Flyout DrawerFlyout(IReadOnlyList<AppCommand> commands)
    {
        var stack = new StackPanel { Margin = new Thickness(4), MinWidth = 220 };
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Standard };
        foreach (var command in commands)
        {
            stack.Children.Add(LabeledRow(command, () => flyout.Hide()));
        }

        flyout.Content = stack;
        return flyout;
    }

    /// <summary>A folded group's section in the overflow drawer: its name over labeled rows of its whole toolset.</summary>
    private static Avalonia.Controls.Control FoldedSection(string name, IReadOnlyList<AppCommand> commands)
    {
        var stack = new StackPanel();
        foreach (var command in commands)
        {
            // No explicit close: the shared shell dismisses the overflow drawer on any tool click.
            stack.Children.Add(LabeledRow(command, static () => { }));
        }

        var header = new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, FontSize = 12.5, Margin = new Thickness(3, 0, 0, 5) };
        return new StackPanel { Children = { header, stack } };
    }

    private static Avalonia.Controls.Control LabeledRow(AppCommand command, Action close)
    {
        Avalonia.Controls.Control glyph = command.Icon is { Length: > 0 } icon
            ? new LucideIcon { IconKey = icon, Size = 15, Foreground = CommandBarChrome.MutedBrush, VerticalAlignment = VerticalAlignment.Center }
            : new Border { Width = 15 };
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { glyph, new TextBlock { Text = command.Title, VerticalAlignment = VerticalAlignment.Center } },
        };
        var shortcut = new TextBlock
        {
            Text = command.ShortcutText,
            Foreground = CommandBarChrome.MutedBrush,
            FontSize = 11,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(shortcut, Dock.Right);
        row.Children.Add(shortcut);
        row.Children.Add(left);

        var button = new Button
        {
            Content = row,
            Background = Brushes.Transparent,
            BorderThickness = default,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 6),
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        CommandBarChrome.AddHover(button);
        button.Click += (_, _) =>
        {
            command.Execute();
            close();
        };
        return button;
    }

    private static Border Divider(Avalonia.Controls.Control child) => new()
    {
        BorderBrush = CommandBarChrome.DividerBrush,
        BorderThickness = new Thickness(0, 0, 1, 0),
        Padding = new Thickness(3, 0),
        Child = child,
    };

    /// <summary>The search pill's self-contained palette: a fuzzy list of every editor command, dropped from the pill.</summary>
    private static Flyout BuildSearchFlyout(IReadOnlyList<AppCommand> commands) =>
        PaletteFlyout.Anchored(
            () => [.. commands.Select(c => new PaletteEntry(c.Title, c.Execute, c.Description, c.ShortcutText, c.Icon))],
            "Search commands…",
            LucideIcon.PaletteGlyph);
}
