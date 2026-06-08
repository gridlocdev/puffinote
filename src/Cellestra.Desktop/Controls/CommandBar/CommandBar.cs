using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.Icons;

namespace Cellestra.Desktop.Controls.CommandBar;

/// <summary>
/// The adaptive command bar (PRD §6.3): one compact, always-present row of icon tools grouped by
/// task, every one bound to the single <see cref="AppCommands"/> catalog. Each group shows its 2–3
/// most-used tools and tucks the rest into a drawer behind a chevron (progressive disclosure).
/// Tools reflect the selection — disabled when they don't apply, lit when their toggle is on — and
/// the whole bar collapses to a slim handle to give the grid more room. The drawer is enriched with
/// swatches and a size stepper by <see cref="CommandBarDrawer"/>.
/// </summary>
public sealed class CommandBar : UserControl
{
    // Emerald accent (from the prototype) for "on" tools — legible on both light and dark chrome.
    internal static readonly IBrush ActiveBackground = new SolidColorBrush(Color.FromArgb(0x33, 0x15, 0x73, 0x4F));
    internal static readonly IBrush ActiveForeground = new SolidColorBrush(Color.FromRgb(0x1A, 0x8A, 0x5E));
    private static readonly IBrush DividerBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x88, 0x88));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    private readonly IAppEnvironment _env;
    private readonly List<CommandBarButton> _buttons = new();
    private readonly Panel _root;
    private readonly Control _full;
    private readonly Control _slim;
    private bool _collapsed;

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
        var groups = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var group in CommandBarModel.For(CommandContext.Cell))
        {
            groups.Children.Add(BuildGroup(group));
        }

        var scroller = new ScrollViewer
        {
            Content = groups,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var dock = new DockPanel { Margin = new Thickness(6, 4) };
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { BuildSearchPill(), BuildCollapseButton(collapse: true) },
        };
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(scroller);
        return dock;
    }

    private Control BuildSlim()
    {
        var expand = BuildCollapseButton(collapse: false);
        return new DockPanel
        {
            Margin = new Thickness(6, 1),
            Children = { Right(expand) },
        };

        static Control Right(Control c)
        {
            DockPanel.SetDock(c, Dock.Right);
            return c;
        }
    }

    private Control BuildGroup(CommandBarGroup group)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (string id in group.Primary)
        {
            if (AppCommands.ById(id) is { } command)
            {
                var button = new CommandBarButton(command, _env);
                _buttons.Add(button);
                row.Children.Add(button);
            }
        }

        if (group.Overflow.Count > 0 || group.HasStyleDrawer)
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

    private Control BuildChevron(CommandBarGroup group)
    {
        var chevron = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = default,
            Padding = new Thickness(2),
            Width = 18,
            Height = 34,
            Content = new LucideIcon { IconKey = "chevron-down", Size = 13, Foreground = MutedBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Center,
        };
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
        string shortcut = AppCommands.ById("view.commandPalette")?.ShortcutText ?? string.Empty;
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
            VerticalAlignment = VerticalAlignment.Center,
        };
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
            Content = new LucideIcon
            {
                IconKey = collapse ? "chevron-up" : "chevron-down",
                Size = collapse ? 16 : 14,
                Foreground = MutedBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            VerticalAlignment = VerticalAlignment.Center,
        };
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

        public CommandBarButton(AppCommand command, IAppEnvironment env)
        {
            _command = command;
            Background = Brushes.Transparent;
            BorderThickness = default;
            Padding = new Thickness(7);
            Width = 34;
            Height = 34;
            CornerRadius = new CornerRadius(6);
            Margin = new Thickness(1, 0);

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
            Click += (_, _) =>
            {
                if (_command.IsEnabled(env))
                {
                    _command.Execute(env);
                }
            };
        }

        public void UpdateState(IAppEnvironment env)
        {
            IsEnabled = _command.IsEnabled(env);
            bool on = _command.IsToggleActive(env);
            Background = on ? ActiveBackground : Brushes.Transparent;
            _dot.IsVisible = on;
            if (on)
            {
                _icon.Foreground = ActiveForeground;
            }
            else
            {
                _icon.ClearValue(LucideIcon.ForegroundProperty);
            }
        }
    }

    /// <summary>Tooltip body: name + shortcut on top, the plain-language description beneath.</summary>
    internal static Control BuildTip(AppCommand command)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock { Text = command.Label, FontWeight = FontWeight.SemiBold });
        if (!string.IsNullOrEmpty(command.ShortcutText))
        {
            header.Children.Add(new TextBlock { Text = command.ShortcutText, Foreground = MutedBrush });
        }

        var panel = new StackPanel { Spacing = 2, MaxWidth = 240 };
        panel.Children.Add(header);
        if (!string.IsNullOrEmpty(command.Description))
        {
            panel.Children.Add(new TextBlock { Text = command.Description, Foreground = MutedBrush, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
        }

        return panel;
    }
}
