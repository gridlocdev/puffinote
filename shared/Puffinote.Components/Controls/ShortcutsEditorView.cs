using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Commands;
using Puffinote.Components.Theming;

namespace Puffinote.Components.Controls;

/// <summary>One row in the shortcuts editor: a command, its current shortcut, and how it may be changed.</summary>
public sealed record ShortcutBinding(
    string Id, string Title, string Group, Shortcut? Shortcut, bool IsReserved, bool IsRebindable, bool IsCustomized);

/// <summary>
/// The shared keyboard-shortcuts editor (PRD §7.6): lists every binding grouped by source, marks the
/// reserved global keys (which can't be reassigned from here), and lets the host rebind the rest. Rebinding
/// a row captures the next key chord; a customized row can be reset to its default. It's a
/// <c>Puffinote.Components</c> control so each standalone app can host it over its own catalog — the host
/// owns persistence and re-feeds <see cref="SetBindings"/> after a change. Key capture lives on the
/// UserControl (focusable, tunnel+bubble) so it works under the headless test environment, mirroring the
/// file tree.
/// </summary>
public sealed class ShortcutsEditorView : UserControl
{
    private readonly StackPanel _list = new() { Spacing = 1 };
    private IReadOnlyList<ShortcutBinding> _bindings = Array.Empty<ShortcutBinding>();
    private string? _capturingId;
    private string? _captureConflict;

    public ShortcutsEditorView()
    {
        Focusable = true;
        Content = new ScrollViewer
        {
            MaxHeight = 460,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _list,
        };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    }

    /// <summary>Raised when the user rebinds a command (<c>shortcut</c> = the new chord, or null to reset it).</summary>
    public event Action<string, Shortcut?>? Rebound;

    /// <summary>Whether a row is currently waiting to capture a key chord (test seam).</summary>
    public bool IsCapturing => _capturingId is not null;

    /// <summary>Replace the listed bindings (grouped in the order given) and leave capture mode.</summary>
    public void SetBindings(IEnumerable<ShortcutBinding> bindings)
    {
        _bindings = bindings.ToList();
        _capturingId = null;
        _captureConflict = null;
        Rebuild();
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        string? group = null;
        foreach (var binding in _bindings)
        {
            if (binding.Group != group)
            {
                group = binding.Group;
                _list.Children.Add(GroupHeader(group));
            }

            _list.Children.Add(Row(binding));
        }
    }

    private static Control GroupHeader(string text) => new TextBlock
    {
        Text = text,
        Margin = new Thickness(4, 10, 4, 4),
        FontWeight = FontWeight.SemiBold,
        Foreground = Tokens.Dark.MutedText,
    };

    private Control Row(ShortcutBinding binding)
    {
        var palette = Tokens.Dark;
        var title = new TextBlock { Text = binding.Title, VerticalAlignment = VerticalAlignment.Center };

        var trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (_capturingId == binding.Id)
        {
            trailing.Children.Add(new TextBlock
            {
                Text = _captureConflict ?? "Press a key…  (Esc to cancel)",
                Foreground = palette.Accent,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            trailing.Children.Add(new TextBlock
            {
                Text = binding.Shortcut is { } s ? ShortcutFormatter.Format(s) : "—",
                Foreground = palette.MutedText,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 64,
                TextAlignment = TextAlignment.Right,
            });

            if (binding.IsReserved)
            {
                trailing.Children.Add(Badge("Reserved", palette));
            }
            else if (binding.IsRebindable)
            {
                trailing.Children.Add(SmallButton("Rebind", () => BeginCapture(binding.Id)));
                if (binding.IsCustomized)
                {
                    trailing.Children.Add(SmallButton("Reset", () => Rebound?.Invoke(binding.Id, null)));
                }
            }
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(4, 3) };
        Grid.SetColumn(title, 0);
        Grid.SetColumn(trailing, 1);
        grid.Children.Add(title);
        grid.Children.Add(trailing);
        return grid;
    }

    private static Control Badge(string text, ThemePalette palette) => new Border
    {
        Background = palette.AccentSubtle,
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 1),
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = palette.MutedText },
    };

    private static Button SmallButton(string text, Action onClick)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 2), FontSize = 12 };
        button.Click += (_, _) => onClick();
        return button;
    }

    private void BeginCapture(string id)
    {
        _capturingId = id;
        _captureConflict = null;
        Rebuild();
        Focus(); // own the keyboard so the next chord lands on our handler (headless-drivable)
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturingId is not { } id)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            _capturingId = null;
            _captureConflict = null;
            Rebuild();
            e.Handled = true;
            return;
        }

        if (IsModifier(e.Key))
        {
            return; // wait for a non-modifier key so a lone Shift/Ctrl doesn't bind
        }

        if (!IsBindable(e.Key, e.KeyModifiers))
        {
            e.Handled = true;
            return; // ignore a chord a plain keypress would type (see IsBindable) — keep waiting; Esc cancels
        }

        var shortcut = ShortcutFrom(e.Key, e.KeyModifiers);
        e.Handled = true;

        // Refuse a chord already bound to another command — otherwise the new binding would silently lose to
        // the existing one (a reserved key always wins; duplicates are order-dependent). Stay in capture with
        // a message so the user can pick a different chord or Esc out (C3).
        if (_bindings.FirstOrDefault(b => b.Id != id && SameChord(b.Shortcut, shortcut)) is { } clash)
        {
            _captureConflict = $"{ShortcutFormatter.Format(shortcut)} is already used by “{clash.Title}”.";
            Rebuild();
            return;
        }

        _capturingId = null;
        _captureConflict = null;
        Rebound?.Invoke(id, shortcut);
    }

    // Two shortcuts clash when they resolve to the same platform gesture (key + modifiers).
    private static bool SameChord(Shortcut? existing, Shortcut candidate)
    {
        if (existing is not { } e)
        {
            return false;
        }

        var a = e.ToGesture();
        var b = candidate.ToGesture();
        return a.Key == b.Key && a.KeyModifiers == b.KeyModifiers;
    }

    private static bool IsModifier(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    // Reject a chord that a plain keypress would type as text — nothing, or Shift only (Shift+letter still
    // types the letter). Such a binding would fire while the user types in the editor. Require a non-Shift
    // modifier (⌘/Ctrl/Alt/Meta) or a function key (C2).
    private static bool IsBindable(Key key, KeyModifiers modifiers) =>
        (modifiers & ~KeyModifiers.Shift) != KeyModifiers.None || (key is >= Key.F1 and <= Key.F24);

    // Fold a captured chord back into a platform-agnostic Shortcut: the platform command modifier (⌘ on
    // macOS, Ctrl elsewhere) becomes UsesCommand; any other held modifiers are the Extra set.
    private static Shortcut ShortcutFrom(Key key, KeyModifiers modifiers)
    {
        var command = Shortcut.IsMac ? KeyModifiers.Meta : KeyModifiers.Control;
        var usesCommand = modifiers.HasFlag(command);
        var extra = modifiers & ~command;
        return new Shortcut(key, extra, usesCommand);
    }
}
