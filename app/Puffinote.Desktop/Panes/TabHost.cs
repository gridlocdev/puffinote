using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Theming;

namespace Puffinote.Desktop.Panes;

/// <summary>How the user resolved a dirty-close prompt.</summary>
internal enum CloseDecision
{
    Save,
    Discard,
    Cancel,
}

/// <summary>One open document in the <see cref="TabHost"/>: its pane, its path, and its dirty handler.</summary>
internal sealed class TabItem
{
    public TabItem(IEditorPane pane, string path)
    {
        Pane = pane;
        Path = path;
    }

    public IEditorPane Pane { get; }

    public string Path { get; }

    /// <summary>The pane's <see cref="IEditorPane.DirtyChanged"/> subscription, kept so it can be detached on close.</summary>
    public EventHandler? DirtyHandler { get; set; }

    /// <summary>The dirty state last painted into the tab strip, so a keystroke that doesn't flip it skips a rebuild.</summary>
    public bool RenderedDirty { get; set; }
}

/// <summary>
/// The editor area's tab host: N open documents (PRD §7.3), each an <see cref="IEditorPane"/> with its own
/// dirty/undo state, freely mixing notes and sheets. Owns the document lifecycle — create on open, activate,
/// and dispose-on-close with a dirty-close prompt (delegated to <see cref="ConfirmClose"/> so this control
/// stays independent of the overlay/dialog stack and remains headless-testable).
/// </summary>
internal sealed class TabHost : UserControl
{
    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Avalonia.Thickness(4, 2) };
    private readonly Border _stripBar;
    private readonly Border _content = new();
    private readonly Control _placeholder;
    private readonly List<TabItem> _tabs = new();
    private TabItem? _active;

    public TabHost()
    {
        var palette = Tokens.Dark;
        _placeholder = new TextBlock
        {
            Text = "Open a file from the sidebar to start.\nNotes and sheets can be open in tabs together.",
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = palette.MutedText,
        };
        _content.Child = _placeholder;

        _stripBar = new Border
        {
            Background = palette.Surface,
            BorderBrush = palette.SurfaceBorder,
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = _strip,
            },
            IsVisible = false, // hidden until the first tab opens
        };

        var root = new DockPanel();
        DockPanel.SetDock(_stripBar, Dock.Top);
        root.Children.Add(_stripBar);
        root.Children.Add(_content);
        Content = root;
    }

    /// <summary>Prompts the user to Save / Don't-Save / Cancel a dirty tab on close. Injected by the shell.</summary>
    public Func<IEditorPane, Task<CloseDecision>>? ConfirmClose { get; set; }

    /// <summary>Raised on any change to the tabs or the active document's dirty state (for the window title/Save).</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<TabItem> Tabs => _tabs;

    public IEditorPane? ActivePane => _active?.Pane;

    public int Count => _tabs.Count;

    public bool HasDirtyTabs => _tabs.Any(t => t.Pane.IsDirty);

    /// <summary>
    /// Open <paramref name="path"/>: focus its existing tab if already open, else create a pane, load it,
    /// and add a new tab. A failed load adds no tab (and never disturbs the open documents).
    /// </summary>
    public async Task OpenAsync(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (_tabs.FirstOrDefault(t => string.Equals(t.Path, full, StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            Activate(open);
            return;
        }

        var pane = EditorPaneFactory.Create(path);
        if (pane is null)
        {
            return; // unsupported type — Phase 3 preview/external
        }

        try
        {
            await pane.LoadAsync(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            pane.Dispose();
            return;
        }

        var tab = new TabItem(pane, full);
        tab.DirtyHandler = (_, _) => OnPaneDirtyChanged(tab);
        pane.DirtyChanged += tab.DirtyHandler;
        _tabs.Add(tab);
        Activate(tab);
    }

    public void Activate(TabItem tab)
    {
        if (!_tabs.Contains(tab))
        {
            return;
        }

        _active = tab;
        _content.Child = tab.Pane.View;
        RebuildStrip();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Close <paramref name="tab"/>, prompting when it's dirty. Returns false if the user cancelled.</summary>
    public async Task<bool> CloseAsync(TabItem tab)
    {
        if (!_tabs.Contains(tab))
        {
            return true;
        }

        if (tab.Pane.IsDirty && ConfirmClose is { } confirm)
        {
            var decision = await confirm(tab.Pane);
            if (decision == CloseDecision.Cancel)
            {
                return false;
            }

            if (decision == CloseDecision.Save)
            {
                try
                {
                    await tab.Pane.SaveAsync();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return false; // save failed → keep the tab open rather than lose the edits
                }
            }
        }

        RemoveTab(tab);
        return true;
    }

    public Task<bool> CloseActiveAsync() => _active is { } a ? CloseAsync(a) : Task.FromResult(true);

    /// <summary>Close every tab, prompting for dirty ones. Returns false as soon as the user cancels one.</summary>
    public async Task<bool> CloseAllAsync()
    {
        foreach (var tab in _tabs.ToList())
        {
            if (!await CloseAsync(tab))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Activate the next (<paramref name="direction"/> = +1) or previous (-1) tab, wrapping around.</summary>
    public void ActivateRelative(int direction)
    {
        if (_active is null || _tabs.Count < 2)
        {
            return;
        }

        int index = (_tabs.IndexOf(_active) + direction + _tabs.Count) % _tabs.Count;
        Activate(_tabs[index]);
    }

    private void RemoveTab(TabItem tab)
    {
        int index = _tabs.IndexOf(tab);
        if (tab.DirtyHandler is { } handler)
        {
            tab.Pane.DirtyChanged -= handler;
        }

        _tabs.Remove(tab);
        tab.Pane.Dispose();

        if (_active == tab)
        {
            _active = _tabs.Count > 0 ? _tabs[Math.Min(index, _tabs.Count - 1)] : null;
            _content.Child = _active is { } next ? next.Pane.View : _placeholder;
        }

        RebuildStrip();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPaneDirtyChanged(TabItem tab)
    {
        // Editors raise DirtyChanged on every keystroke, but the tab strip (and the window title) only
        // change when a tab flips clean⇄dirty. Skip the rebuild and the Changed fan-out otherwise, so
        // typing doesn't rebuild the whole strip and re-query every command per character.
        if (tab.RenderedDirty == tab.Pane.IsDirty)
        {
            return;
        }

        RebuildStrip();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildStrip()
    {
        _stripBar.IsVisible = _tabs.Count > 0;
        _strip.Children.Clear();
        foreach (var tab in _tabs)
        {
            tab.RenderedDirty = tab.Pane.IsDirty;
            _strip.Children.Add(BuildTab(tab));
        }
    }

    private Control BuildTab(TabItem tab)
    {
        var palette = Tokens.Dark;
        bool active = ReferenceEquals(tab, _active);

        var label = new Button
        {
            Content = tab.Pane.Title + (tab.Pane.IsDirty ? " •" : string.Empty),
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(8, 4),
            Foreground = active ? palette.Accent : palette.MutedText,
        };
        label.Click += (_, _) => Activate(tab);

        var close = new Button
        {
            Content = "✕",
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(4, 0),
            Foreground = palette.MutedText,
        };
        close.Click += (_, _) => _ = CloseAsync(tab);

        return new Border
        {
            Background = active ? palette.AccentSubtle : Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { label, close },
            },
        };
    }
}
