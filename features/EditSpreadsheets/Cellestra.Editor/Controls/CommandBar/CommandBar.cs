using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls;
using Puffinote.Components.Controls.Icons;

namespace Cellestra.Desktop.Controls.CommandBar;

/// <summary>
/// The adaptive command bar (PRD §6.3): one compact, always-present row of icon tools grouped by
/// task, every one bound to the single <see cref="AppCommands"/> catalog. Each group shows its 2–3
/// most-used tools and tucks the rest into a drawer behind a chevron (progressive disclosure).
/// Tools reflect the selection — disabled when they don't apply, lit when their toggle is on — and
/// the whole bar collapses to a slim handle to give the grid more room. The drawer is enriched with
/// swatches and a size stepper by <see cref="CommandBarDrawer"/>.
/// When the window is too narrow to fit every group, the rightmost groups fold (in reading order)
/// into a single overflow drawer behind a double-chevron button — each folded group shown as its
/// header over a grid of icon buttons (its whole toolset, no nested submenu) rather than scrolling
/// off behind a scrollbar.
/// </summary>
public sealed class CommandBar : UserControl
{
    // Emerald accent (from the prototype) for "on" tools — legible on both light and dark chrome.
    internal static readonly IBrush ActiveBackground = new SolidColorBrush(Color.FromArgb(0x33, 0x15, 0x73, 0x4F));
    internal static readonly IBrush ActiveForeground = new SolidColorBrush(Color.FromRgb(0x1A, 0x8A, 0x5E));
    // A subtle, theme-neutral grey wash for pointer-over feedback — readable on both light and dark
    // chrome, and deliberately lighter than the emerald "on" wash so an on-and-hovered tool still reads as on.
    internal static readonly IBrush HoverBackground = new SolidColorBrush(Color.FromArgb(0x1A, 0x88, 0x88, 0x88));
    private static readonly IBrush DividerBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x88, 0x88));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    /// <summary>The expanded bar's row height — groups are measured against it to size the reflow.</summary>
    private const double BarHeight = 34;

    // The order folded sections appear in the overflow drawer — by everyday usefulness, NOT the bar's
    // left-to-right order. Numbers/Data/Insert lead (the user's call); the rest follow by how often
    // they're reached day to day: borders and AutoSum over the clipboard/text/align tools that are
    // mostly already muscle-memory shortcuts (⌘Z/⌘C/⌘B). Names not listed sort last, in bar order.
    private static readonly string[] OverflowOrder = { "Numbers", "Data", "Insert", "Border", "Formula", "Edit", "Text", "Align" };

    private readonly IAppEnvironment _env;
    private readonly List<CommandBarButton> _buttons = new();
    // The group controls and their natural widths, in left-to-right priority order: when the window
    // is too narrow, the rightmost groups are the first to fold into the overflow drawer.
    private readonly List<Control> _groupControls = new();
    private readonly List<double> _groupWidths = new();
    // Each group's display rank in the overflow drawer (see OverflowOrder), parallel to _groupControls.
    private readonly List<int> _overflowRank = new();
    // The parallel sections shown for folded groups in the overflow drawer: each is the group's header
    // over a wrap of icon buttons for its whole toolset (no nested submenu). The buttons are added to
    // _buttons, so RefreshState keeps them in sync just like the bar's own.
    private readonly List<Control> _overflowPanels = new();
    private readonly Panel _root;
    private readonly Control _full;
    private readonly Control _slim;
    private bool _collapsed;

    // Adaptive overflow: groups that don't fit on the strip move into a drop-down drawer behind a
    // double-chevron button (Google-Sheets style) rather than scrolling off behind a scrollbar.
    private StackPanel _strip = null!;
    private StackPanel _overflowHost = null!;
    private Button _overflowButton = null!;
    private Flyout _overflowFlyout = null!;
    private LucideIcon _overflowIcon = null!;
    private int _currentFit = -1;
    private bool _reflowing;

    /// <summary>Raised when the user collapses or expands the bar, so the host can persist the choice.</summary>
    public event EventHandler? CollapsedChanged;

    public CommandBar(IAppEnvironment environment)
    {
        _env = environment;
        _full = BuildFull();
        _slim = BuildSlim();
        _root = new Panel { Children = { _full, _slim } };

        var border = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = _root,
        };
        // Follow the same themed chrome brushes as the formula bar / status bar, reactive to light/dark.
        border.Bind(Border.BackgroundProperty, this.GetResourceObservable("ChromeBackground"));
        border.Bind(Border.BorderBrushProperty, this.GetResourceObservable("ChromeBorder"));
        Content = border;

        ApplyCollapsed();
        RefreshState();
    }

    /// <summary>Whether the bar is folded down to its slim handle (persisted by the host).</summary>
    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value)
            {
                return;
            }

            _collapsed = value;
            ApplyCollapsed();
            CollapsedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Test hook: whether any groups are currently folded into the overflow drawer.</summary>
    internal bool IsOverflowing => _overflowButton.IsVisible;

    /// <summary>Test hook: the number of groups currently shown inline on the strip.</summary>
    internal int InlineGroupCount => _strip.Children.Count;

    /// <summary>Test hook: the number of flattened group panels currently mounted in the overflow drawer.</summary>
    internal int OverflowGroupCount => _overflowHost.Children.Count(c => _overflowPanels.Contains(c));

    /// <summary>Test hook: the flattened group panels currently mounted in the overflow drawer.</summary>
    internal IReadOnlyList<Control> OverflowDrawerPanels =>
        _overflowHost.Children.Where(c => _overflowPanels.Contains(c)).ToList();

    /// <summary>Re-evaluate every tool's enabled and toggle state against the current selection.</summary>
    public void RefreshState()
    {
        foreach (var button in _buttons)
        {
            button.UpdateState(_env);
        }
    }

    private void ApplyCollapsed()
    {
        _full.IsVisible = !_collapsed;
        _slim.IsVisible = _collapsed;
    }

    // ---- layout -----------------------------------------------------------

    private Control BuildFull()
    {
        // The inline strip holds the groups that currently fit; ClipToBounds keeps a group that's
        // momentarily too wide (mid-resize, before the reflow fires) from drawing over the search pill.
        _strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
        };
        // A folded group drops down as its header over a grid of icon buttons — the toolbar, dropped
        // down — so the overflow drawer is a vertical stack of those sections (scrolled if it gets tall).
        _overflowHost = new StackPanel { Orientation = Orientation.Vertical, Spacing = 14, Margin = new Thickness(10, 8) };

        foreach (var group in CommandBarModel.For(CommandContext.Cell))
        {
            var control = BuildGroup(group);
            // A group's width is fixed (icon buttons have fixed sizes), so measure it once up front
            // and drive the reflow off the cached widths — no per-resize remeasuring of the children.
            control.Measure(new Size(double.PositiveInfinity, BarHeight));
            _groupControls.Add(control);
            _groupWidths.Add(control.DesiredSize.Width);
            _strip.Children.Add(control);

            int rank = Array.IndexOf(OverflowOrder, group.Name);
            _overflowRank.Add(rank < 0 ? int.MaxValue : rank);

            // The overflow counterpart used when this group folds away — built once, mounted on demand.
            _overflowPanels.Add(BuildOverflowGroup(group));
        }

        _overflowButton = BuildOverflowButton();
        _overflowButton.IsVisible = false;

        var dock = new DockPanel { Margin = new Thickness(6, 4) };
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _overflowButton, BuildSearchPill(), BuildCollapseButton(collapse: true) },
        };
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(_strip); // fills the space left of the right cluster
        // The strip is the DockPanel's fill child, so its width is exactly the room available for
        // tools; recompute the split whenever that changes (window resize, overflow button toggling).
        _strip.SizeChanged += (_, _) => Reflow();
        return dock;
    }

    private Button BuildOverflowButton()
    {
        _overflowIcon = new LucideIcon
        {
            IconKey = "chevrons-down",
            Size = 16,
            Foreground = MutedBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = default,
            Padding = new Thickness(2),
            Width = 30,
            Height = 34,
            Focusable = false,
            Content = _overflowIcon,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6),
        };
        AddHover(button);
        ToolTip.SetTip(button, "More tools");

        var scroller = new ScrollViewer
        {
            Content = _overflowHost,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 320,
        };
        _overflowFlyout = new Flyout
        {
            Content = scroller,
            // Centre the drawer under the button's icon (Avalonia nudges it back on-screen near the edge).
            Placement = PlacementMode.Bottom,
            ShowMode = FlyoutShowMode.Standard,
        };
        // Mirror the open/closed state in the glyph: double-chevron-down to open, up to close. The
        // drawer's buttons are in _buttons, so RefreshState syncs them to the selection on open.
        _overflowFlyout.Opened += (_, _) =>
        {
            _overflowIcon.IconKey = "chevrons-up";
            RefreshState();
        };
        _overflowFlyout.Closed += (_, _) => _overflowIcon.IconKey = "chevrons-down";
        button.Flyout = _overflowFlyout;
        return button;
    }

    /// <summary>
    /// Split the groups between the inline strip and the overflow drawer to fit the current width:
    /// fill the strip left-to-right with as many whole groups as fit, fold the rest into the drawer,
    /// and show the double-chevron button only when something was folded away. Idempotent and guarded
    /// against re-entrancy, so the layout settles after the strip's width stabilises.
    /// </summary>
    private void Reflow()
    {
        if (_reflowing)
        {
            return;
        }

        double available = _strip.Bounds.Width;
        if (available <= 0 || _groupControls.Count == 0)
        {
            return;
        }

        int fit = 0;
        double sum = 0;
        foreach (double width in _groupWidths)
        {
            if (sum + width > available)
            {
                break;
            }

            sum += width;
            fit++;
        }

        bool overflow = fit < _groupControls.Count;
        if (fit == _currentFit && overflow == _overflowButton.IsVisible)
        {
            return; // already in the right shape — avoid needless reparenting (and a layout loop)
        }

        _reflowing = true;
        try
        {
            _strip.Children.Clear();
            _overflowHost.Children.Clear();

            // The strip keeps bar order (left-to-right reading order); whichever groups fit stay put.
            for (int i = 0; i < fit; i++)
            {
                _strip.Children.Add(_groupControls[i]);
            }

            // The overflow drawer orders its sections by everyday usefulness (OverflowOrder), not bar
            // order — the most-reached folded tools sit at the top of the drawer.
            var folded = new List<int>();
            for (int i = fit; i < _groupControls.Count; i++)
            {
                folded.Add(i);
            }

            folded.Sort((a, b) => _overflowRank[a].CompareTo(_overflowRank[b]));
            foreach (int i in folded)
            {
                _overflowHost.Children.Add(_overflowPanels[i]);
            }

            _overflowButton.IsVisible = overflow;
            _currentFit = fit;
            if (!overflow)
            {
                _overflowFlyout.Hide(); // nothing left to show; close the drawer if it was open
            }
        }
        finally
        {
            _reflowing = false;
        }
    }

    private Control BuildSlim()
    {
        var expand = BuildCollapseButton(collapse: false);
        DockPanel.SetDock(expand, Dock.Right);
        // LastChildFill defaults to true, which would make the lone expand button the fill element and
        // ignore its Dock.Right — pinning it to the left. Turn it off so the button stays top-right,
        // exactly where the collapse button sat in the full bar.
        return new DockPanel
        {
            Margin = new Thickness(6, 1),
            LastChildFill = false,
            Children = { expand },
        };
    }

    private Control BuildGroup(CommandBarGroup group)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (string id in group.Primary)
        {
            if (_env.Commands.ById(id) is { } command)
            {
                var button = new CommandBarButton(command, _env);
                _buttons.Add(button);
                row.Children.Add(button);
            }
        }

        if (group.HasDrawer)
        {
            row.Children.Add(BuildChevron(group));
        }

        return new Border
        {
            BorderBrush = DividerBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(3, 0),
            Child = row,
        };
    }

    /// <summary>
    /// A folded group's section in the overflow drawer: the group name over a wrapping grid of icon
    /// buttons covering its WHOLE toolset (primary + overflow), each clicking through to the command and
    /// dismissing the drawer. No chevron, no submenu — recognition by icon, the same buttons as the bar.
    /// </summary>
    private Control BuildOverflowGroup(CommandBarGroup group)
    {
        var grid = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 248 };
        foreach (string id in group.Primary.Concat(group.Overflow))
        {
            if (_env.Commands.ById(id) is { } command)
            {
                var button = new CommandBarButton(command, _env, () => _overflowFlyout.Hide());
                _buttons.Add(button);
                grid.Children.Add(button);
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
            Content = new LucideIcon { IconKey = "chevron-down", Size = 13, Foreground = MutedBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6),
        };
        AddHover(chevron);
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

    private Control BuildSearchPill()
    {
        string shortcut = _env.Commands.ById("view.commandPalette")?.MacShortcut() ?? string.Empty;
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new LucideIcon { IconKey = "search", Size = 14, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = "Find a tool…", Foreground = MutedBrush, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = shortcut, Foreground = MutedBrush, FontSize = 11, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
            },
        };

        var pill = new Button
        {
            Content = content,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0x88, 0x88, 0x88)),
            BorderThickness = default,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 6),
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var pillRest = pill.Background;
        AddHover(pill, pillRest);
        ToolTip.SetTip(pill, "Search and run any command");
        pill.Click += (_, _) => _env.ShowCommandPalette();
        return pill;
    }

    private Button BuildCollapseButton(bool collapse)
    {
        var button = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = default,
            Padding = new Thickness(2),
            Width = 30,
            Height = collapse ? 34 : 18,
            Focusable = false,
            Content = new LucideIcon
            {
                IconKey = collapse ? "chevron-up" : "chevron-down",
                Size = collapse ? 16 : 14,
                Foreground = MutedBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6),
        };
        AddHover(button);
        ToolTip.SetTip(button, collapse ? "Collapse the bar — more room for your data" : "Show the command bar");
        button.Click += (_, _) => Collapsed = collapse;
        return button;
    }

    // ---- the icon tool button --------------------------------------------

    /// <summary>A single primary icon tool: shows enabled/disabled state and lights up (with a dot) when its toggle is on.</summary>
    private sealed class CommandBarButton : Button
    {
        private readonly AppCommand _command;
        private readonly LucideIcon _icon;
        private readonly Ellipse _dot;
        private bool _on;
        private bool _hover;

        public CommandBarButton(AppCommand command, IAppEnvironment env, Action? onClicked = null)
        {
            _command = command;
            Background = Brushes.Transparent;
            BorderThickness = default;
            Padding = new Thickness(7);
            Width = 34;
            Height = 34;
            CornerRadius = new CornerRadius(6);
            Margin = new Thickness(1, 0);
            // A toolbar must not steal keyboard focus from the grid — clicking Bold then typing must
            // still type into the selected cell. Commands stay reachable via menu, ⌘K, and shortcuts.
            Focusable = false;

            _icon = new LucideIcon { IconKey = command.Icon, Size = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _dot = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = ActiveForeground,
                IsVisible = false,
                Margin = new Thickness(0, 0, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            Content = new Panel { Children = { _icon, _dot } };

            ToolTip.SetTip(this, BuildTip(command));
            PointerEntered += (_, _) => { _hover = true; ApplyBackground(); };
            PointerExited += (_, _) => { _hover = false; ApplyBackground(); };
            Click += (_, _) =>
            {
                if (_command.IsEnabled)
                {
                    _command.Execute();
                    env.Narrate(Describe(_command, env));
                }

                // In the overflow drawer, clicking a tool dismisses it (like Google Sheets); on the bar
                // itself there's nothing to close, so onClicked is null there.
                onClicked?.Invoke();
            };
        }

        public void UpdateState(IAppEnvironment env)
        {
            IsEnabled = _command.IsEnabled;
            _on = _command.IsToggleActive;
            _dot.IsVisible = _on;
            ApplyBackground();
            if (_on)
            {
                _icon.Foreground = ActiveForeground;
            }
            else
            {
                _icon.ClearValue(LucideIcon.ForegroundProperty);
            }
        }

        // "On" wins over hover so a lit toggle keeps its emerald wash; hover supplies a subtle highlight otherwise.
        private void ApplyBackground() =>
            Background = _on ? ActiveBackground : _hover ? HoverBackground : Brushes.Transparent;
    }

    /// <summary>
    /// Give a transparent-backed control a brief, subtle pointer-over highlight that respects the theme.
    /// On exit it returns to <paramref name="rest"/> (Transparent by default), so callers that carry their
    /// own "on"/"resting" wash pass that brush to avoid clobbering it.
    /// </summary>
    internal static void AddHover(TemplatedControl control, IBrush? rest = null)
    {
        rest ??= Brushes.Transparent;
        control.PointerEntered += (_, _) => control.Background = HoverBackground;
        control.PointerExited += (_, _) => control.Background = rest;
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
