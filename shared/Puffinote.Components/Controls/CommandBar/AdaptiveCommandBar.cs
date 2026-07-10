using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Controls.Icons;

namespace Puffinote.Components.Controls.CommandBar;

/// <summary>
/// The family's adaptive command-bar shell (PRD §6.3), shared by the spreadsheet and note-editor toolbars:
/// one compact row of tool <see cref="Section"/>s that reflows to the window width — the rightmost sections
/// fold (Google-Sheets style) into a double-chevron overflow drawer rather than scrolling off — plus an
/// optional search pill and a collapse-to-slim handle. It owns only <em>layout</em>: each app builds its own
/// sections (tools, per-group drawers) and its folded counterparts, and refreshes command state itself.
/// </summary>
public sealed class AdaptiveCommandBar : UserControl
{
    /// <summary>
    /// One tool group. <paramref name="Inline"/> is the strip control (primary tools + any chevron drawer);
    /// <paramref name="Folded"/> is its flattened stand-in shown when the section folds into the overflow
    /// drawer; <paramref name="OverflowRank"/> orders the folded sections by usefulness (lower = higher),
    /// independent of left-to-right bar order (negative sorts last).
    /// </summary>
    public sealed record Section(string Name, Control Inline, Control Folded, int OverflowRank);

    /// <summary>
    /// The optional right-hand "search a tool" pill: its placeholder, the palette shortcut it shows, and its
    /// tooltip. Give it either an <paramref name="OnClick"/> (opens the host's own palette) or a
    /// <paramref name="Flyout"/> (a self-contained palette that drops from the pill) — the flyout wins.
    /// </summary>
    public sealed record SearchPill(string Placeholder, string Shortcut, string Tooltip, Action? OnClick = null, Flyout? Flyout = null);

    /// <summary>The expanded bar's row height — sections are measured against it to size the reflow.</summary>
    private const double BarHeight = 34;

    private readonly SearchPill? _search;
    private readonly List<Control> _inline = [];
    private readonly List<double> _widths = [];
    private readonly List<Control> _folded = [];
    private readonly List<int> _rank = [];

    private readonly Control _full;
    private readonly Control _slim;
    private StackPanel _strip = null!;
    private StackPanel _overflowHost = null!;
    private Button _overflowButton = null!;
    private Flyout _overflowFlyout = null!;
    private LucideIcon _overflowIcon = null!;
    private int _currentFit = -1;
    private bool _reflowing;

    /// <summary>Raised when the user collapses or expands the bar, so the host can persist the choice.</summary>
    public event EventHandler? CollapsedChanged;

    /// <summary>Raised when the overflow drawer opens, so the host can refresh the folded tools' state first.</summary>
    public event EventHandler? OverflowOpened;

    public AdaptiveCommandBar(IReadOnlyList<Section> sections, SearchPill? search = null)
    {
        _search = search;
        _full = BuildFull(sections);
        _slim = BuildSlim();

        var border = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Panel { Children = { _full, _slim } },
        };
        border.Bind(Border.BackgroundProperty, this.GetResourceObservable("ChromeBackground"));
        border.Bind(Border.BorderBrushProperty, this.GetResourceObservable("ChromeBorder"));
        Content = border;

        ApplyCollapsed();
    }

    /// <summary>Whether the bar is folded down to its slim handle (persisted by the host).</summary>
    public bool Collapsed
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            ApplyCollapsed();
            CollapsedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether any sections are currently folded into the overflow drawer (test hook).</summary>
    public bool IsOverflowing => _overflowButton.IsVisible;

    /// <summary>The number of sections currently shown inline on the strip (test hook).</summary>
    public int InlineGroupCount => _strip.Children.Count;

    /// <summary>The number of folded sections currently mounted in the overflow drawer (test hook).</summary>
    public int OverflowGroupCount => _overflowHost.Children.Count(c => _folded.Contains(c));

    /// <summary>The folded section panels currently mounted in the overflow drawer (test hook).</summary>
    public IReadOnlyList<Control> OverflowDrawerPanels =>
        [.. _overflowHost.Children.Where(c => _folded.Contains(c))];

    private void ApplyCollapsed()
    {
        _full.IsVisible = !Collapsed;
        _slim.IsVisible = Collapsed;
    }

    private Control BuildFull(IReadOnlyList<Section> sections)
    {
        _strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true, // a momentarily-too-wide section (mid-resize) never draws over the right cluster
        };
        _overflowHost = new StackPanel { Orientation = Orientation.Vertical, Spacing = 14, Margin = new Thickness(10, 8) };

        foreach (var section in sections)
        {
            var control = section.Inline;
            // A section's width is fixed (icon buttons have fixed sizes), so measure it once and drive the
            // reflow off the cached widths — no per-resize remeasuring of the children.
            control.Measure(new Size(double.PositiveInfinity, BarHeight));
            _inline.Add(control);
            _widths.Add(control.DesiredSize.Width);
            _strip.Children.Add(control);
            _rank.Add(section.OverflowRank < 0 ? int.MaxValue : section.OverflowRank);
            _folded.Add(section.Folded);
        }

        _overflowButton = BuildOverflowButton();
        _overflowButton.IsVisible = false;

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_overflowButton);
        if (BuildSearchPill() is { } pill)
        {
            right.Children.Add(pill);
        }

        right.Children.Add(BuildCollapseButton(collapse: true));

        var dock = new DockPanel { Margin = new Thickness(6, 4) };
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(_strip); // fills the space left of the right cluster
        _strip.SizeChanged += (_, _) => Reflow();
        return dock;
    }

    /// <summary>
    /// Split the sections between the inline strip and the overflow drawer to fit the current width: fill the
    /// strip left-to-right with as many whole sections as fit, fold the rest into the drawer (ordered by
    /// usefulness rank), and show the double-chevron button only when something folded. Idempotent and guarded
    /// against re-entrancy so the layout settles once the strip's width stabilises.
    /// </summary>
    private void Reflow()
    {
        if (_reflowing)
        {
            return;
        }

        double available = _strip.Bounds.Width;
        if (available <= 0 || _inline.Count == 0)
        {
            return;
        }

        int fit = 0;
        double sum = 0;
        foreach (double width in _widths)
        {
            if (sum + width > available)
            {
                break;
            }

            sum += width;
            fit++;
        }

        bool overflow = fit < _inline.Count;
        if (fit == _currentFit && overflow == _overflowButton.IsVisible)
        {
            return; // already the right shape — avoid needless reparenting (and a layout loop)
        }

        _reflowing = true;
        try
        {
            _strip.Children.Clear();
            _overflowHost.Children.Clear();

            for (int i = 0; i < fit; i++)
            {
                _strip.Children.Add(_inline[i]); // the strip keeps bar order (reading order)
            }

            // The overflow drawer orders its sections by usefulness rank, not bar order.
            var folded = new List<int>();
            for (int i = fit; i < _inline.Count; i++)
            {
                folded.Add(i);
            }

            folded.Sort((a, b) => _rank[a].CompareTo(_rank[b]));
            foreach (int i in folded)
            {
                _overflowHost.Children.Add(_folded[i]);
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

    private Button BuildOverflowButton()
    {
        _overflowIcon = IconGlyph("chevrons-down", 16);
        var button = IconButton(_overflowIcon, width: 30, height: 34, "More tools");

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
            Placement = PlacementMode.Bottom,
            ShowMode = FlyoutShowMode.Standard,
        };
        _overflowFlyout.Opened += (_, _) =>
        {
            _overflowIcon.IconKey = "chevrons-up";
            OverflowOpened?.Invoke(this, EventArgs.Empty);
        };
        _overflowFlyout.Closed += (_, _) => _overflowIcon.IconKey = "chevrons-down";
        // Clicking any tool in the drawer dismisses it (Google-Sheets style) — a routed handler so the app's
        // folded controls need no back-reference to this flyout.
        _overflowHost.AddHandler(Button.ClickEvent, (_, _) => _overflowFlyout.Hide());
        button.Flyout = _overflowFlyout;
        return button;
    }

    private Control BuildSlim()
    {
        var expand = BuildCollapseButton(collapse: false);
        DockPanel.SetDock(expand, Dock.Right);
        // LastChildFill would make the lone button the fill element and ignore Dock.Right; turn it off so the
        // button stays top-right, exactly where the collapse button sat in the full bar.
        return new DockPanel { Margin = new Thickness(6, 1), LastChildFill = false, Children = { expand } };
    }

    private Control? BuildSearchPill()
    {
        if (_search is not { } spec)
        {
            return null;
        }

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new LucideIcon { IconKey = "search", Size = 14, Foreground = CommandBarChrome.MutedBrush, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = spec.Placeholder, Foreground = CommandBarChrome.MutedBrush, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = spec.Shortcut, Foreground = CommandBarChrome.MutedBrush, FontSize = 11, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
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
        CommandBarChrome.AddHover(pill, pill.Background);
        ToolTip.SetTip(pill, spec.Tooltip);
        if (spec.Flyout is { } flyout)
        {
            pill.Flyout = flyout; // a self-contained palette drops from the pill
        }
        else if (spec.OnClick is { } onClick)
        {
            pill.Click += (_, _) => onClick();
        }

        return pill;
    }

    private Button BuildCollapseButton(bool collapse)
    {
        var button = IconButton(
            IconGlyph(collapse ? "chevron-up" : "chevron-down", collapse ? 16 : 14),
            width: 30,
            height: collapse ? 34 : 18,
            collapse ? "Collapse the bar — more room for your work" : "Show the command bar");
        button.Click += (_, _) => Collapsed = collapse;
        return button;
    }

    private static LucideIcon IconGlyph(string key, double size) => new()
    {
        IconKey = key,
        Size = size,
        Foreground = CommandBarChrome.MutedBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Button IconButton(Control content, double width, double height, string tooltip)
    {
        var button = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = default,
            Padding = new Thickness(2),
            Width = width,
            Height = height,
            Focusable = false,
            Content = content,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6),
        };
        CommandBarChrome.AddHover(button);
        ToolTip.SetTip(button, tooltip);
        return button;
    }
}
