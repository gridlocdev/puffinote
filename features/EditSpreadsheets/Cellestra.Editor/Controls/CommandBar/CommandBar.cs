using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Controls.Icons;

namespace Cellestra.Desktop.Controls.CommandBar;

/// <summary>
/// The spreadsheet's adaptive command bar (PRD §6.3): the sheet-specific groups — every tool bound to the
/// single <see cref="AppCommands"/> catalog, with bespoke swatch/size/format drawers per group — mounted on
/// the shared <see cref="AdaptiveCommandBar"/> shell (which owns the reflow/overflow, collapse handle, and
/// search pill). Tools reflect the selection (disabled when they don't apply, lit when their toggle is on),
/// narrating each action; the whole bar collapses to a slim handle to give the grid more room.
/// </summary>
public sealed class CommandBar : UserControl
{
    // The order folded sections appear in the overflow drawer — by everyday usefulness, NOT the bar's
    // left-to-right order. Numbers/Data/Insert lead; the rest follow by how often they're reached day to day.
    private static readonly string[] OverflowOrder = ["Numbers", "Data", "Insert", "Border", "Formula", "Edit", "Text", "Align"];

    private readonly IAppEnvironment _env;
    private readonly List<CommandTool> _buttons = [];
    private readonly AdaptiveCommandBar _bar;

    /// <summary>Raised when the user collapses or expands the bar, so the host can persist the choice.</summary>
    public event EventHandler? CollapsedChanged;

    public CommandBar(IAppEnvironment environment)
    {
        _env = environment;

        var sections = new List<AdaptiveCommandBar.Section>();
        foreach (var group in CommandBarModel.For(CommandContext.Cell))
        {
            int rank = Array.IndexOf(OverflowOrder, group.Name);
            sections.Add(new AdaptiveCommandBar.Section(group.Name, BuildGroup(group), BuildOverflowGroup(group), rank));
        }

        // The pill drops its palette from itself rather than opening the centred overlay: a click starts with the
        // eye already in the top-right corner, on the pointer, so that is where the results should appear. The
        // chord beside it still opens the centred palette (see IAppEnvironment.ShowCommandPalette) — a keystroke
        // comes from a neutral, centred gaze, and the two distances are the point.
        string shortcut = _env.Commands.ById("view.commandPalette")?.MacShortcut() ?? string.Empty;
        var search = new AdaptiveCommandBar.SearchPill(
            "Find a tool…",
            shortcut,
            "Search and run any command",
            Flyout: PaletteFlyout.Anchored(() => PaletteCatalog.Entries(_env.Commands), PaletteCatalog.Placeholder, LucideIcon.PaletteGlyph));

        _bar = new AdaptiveCommandBar(sections, search);
        _bar.CollapsedChanged += (_, _) => CollapsedChanged?.Invoke(this, EventArgs.Empty);
        _bar.OverflowOpened += (_, _) => RefreshState(); // sync folded tools to the selection as the drawer opens
        Content = _bar;

        RefreshState();
    }

    /// <summary>Whether the bar is folded down to its slim handle (persisted by the host).</summary>
    public bool Collapsed
    {
        get => _bar.Collapsed;
        set => _bar.Collapsed = value;
    }

    /// <summary>Test hook: whether any groups are currently folded into the overflow drawer.</summary>
    internal bool IsOverflowing => _bar.IsOverflowing;

    /// <summary>Test hook: the number of groups currently shown inline on the strip.</summary>
    internal int InlineGroupCount => _bar.InlineGroupCount;

    /// <summary>Test hook: the number of flattened group panels currently mounted in the overflow drawer.</summary>
    internal int OverflowGroupCount => _bar.OverflowGroupCount;

    /// <summary>Test hook: the flattened group panels currently mounted in the overflow drawer.</summary>
    internal IReadOnlyList<Control> OverflowDrawerPanels => _bar.OverflowDrawerPanels;

    /// <summary>Re-evaluate every tool's enabled and toggle state against the current selection.</summary>
    public void RefreshState()
    {
        foreach (var button in _buttons)
        {
            button.UpdateState();
        }
    }

    private Control BuildGroup(CommandBarGroup group)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (string id in group.Primary)
        {
            if (_env.Commands.ById(id) is { } command)
            {
                row.Children.Add(Tool(command));
            }
        }

        if (group.HasDrawer)
        {
            row.Children.Add(BuildChevron(group));
        }

        return new Border
        {
            BorderBrush = CommandBarChrome.DividerBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(3, 0),
            Child = row,
        };
    }

    /// <summary>
    /// A folded group's section in the overflow drawer: the group name over a wrapping grid of icon buttons
    /// covering its WHOLE toolset (primary + overflow). No chevron, no submenu — the shared shell dismisses
    /// the drawer when any of these tools is clicked.
    /// </summary>
    private Control BuildOverflowGroup(CommandBarGroup group)
    {
        var grid = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 248 };
        foreach (string id in group.Primary.Concat(group.Overflow))
        {
            if (_env.Commands.ById(id) is { } command)
            {
                grid.Children.Add(Tool(command));
            }
        }

        var header = new TextBlock
        {
            Text = group.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12.5,
            Margin = new Thickness(3, 0, 0, 5),
        };
        return new StackPanel { Children = { header, grid } };
    }

    /// <summary>A primary tool bound to <paramref name="command"/>, narrating on execute and tracked for state refresh.</summary>
    private CommandTool Tool(AppCommand command)
    {
        var button = new CommandTool(command, onExecuted: c => _env.Narrate(Describe(c, _env)));
        _buttons.Add(button);
        return button;
    }

    private Control BuildChevron(CommandBarGroup group)
    {
        var chevron = new Button
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
        };
        CommandBarChrome.AddHover(chevron);
        ToolTip.SetTip(chevron, $"More {group.Name.ToLowerInvariant()} tools");

        var (content, refresh) = CommandBarDrawer.Build(group, _env, () => (chevron.Flyout as Flyout)?.Hide());
        var flyout = new Flyout
        {
            Content = content,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Standard,
        };
        flyout.Opened += (_, _) => refresh();
        // Belt-and-braces: if the drawer closes while a swatch is hovered, drop any live preview.
        flyout.Closed += (_, _) => _env.PreviewCellStyle(null);
        chevron.Flyout = flyout;
        return chevron;
    }

    /// <summary>A short status-line sentence for an action: toggles report on/off, others just name themselves.</summary>
    internal static string Describe(AppCommand command, IAppEnvironment env)
    {
        string address = env.Selection.ActiveAddress.ToString();
        return command.IsActive is null
            ? $"{command.Title} · {address}"
            : $"{command.Title} {(command.IsToggleActive ? "on" : "off")} · {address}";
    }

    /// <summary>
    /// Tooltip body: name + shortcut on top, the plain-language description beneath. Adapts this app's
    /// <see cref="AppCommand"/> onto the shared <see cref="TooltipCard"/> (which themes muted text).
    /// </summary>
    internal static Control BuildTip(AppCommand command) =>
        new TooltipCard(command.Title, command.MacShortcut(), command.Description);
}
