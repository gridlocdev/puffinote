using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Shell;

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

    /// <summary>The open file's absolute path. Settable so the host can rebind it after an external rename.</summary>
    public string Path { get; set; }

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
    private readonly List<TabItem> _tabs = [];
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

    private NoteConfig _noteConfig = NoteConfig.Resolve(new PuffinoteSettings());

    /// <summary>
    /// Puffinote's override settings, layered over each editor's own config (PRD §7.5). Setting this resolves
    /// the note config once (reading Typage's own settings file), so opening notes doesn't re-read it per tab
    /// (P7); MainWindow assigns it per notebook.
    /// </summary>
    public PuffinoteSettings Settings
    {
        get;
        set
        {
            field = value;
            _noteConfig = NoteConfig.Resolve(value);
        }
    } = new();

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

        var pane = EditorPaneFactory.Create(path, _noteConfig);
        if (pane is null)
        {
            return; // unsupported type — Phase 3 preview/external
        }

        try
        {
            await pane.LoadAsync(path);
        }
        catch (Exception e)
        {
            pane.Dispose();

            // A file that's simply gone — a stale recent entry, or one deleted externally — leaves the open
            // documents untouched; it shouldn't replace your work with an error. A file that EXISTS but can't
            // be parsed (an empty/corrupt .ods that isn't a real workbook) shows the reason in the editor area
            // instead of a blank/broken editor. Catching broadly also stops a non-IO parse error propagating
            // out of this fire-and-forget open and going unobserved.
            if (File.Exists(full))
            {
                AddTab(new ErrorPane(path, $"Couldn’t open {System.IO.Path.GetFileName(path)}.\n\n{e.Message}"), full);
            }

            return;
        }

        AddTab(pane, full);
    }

    /// <summary>Wire a loaded pane into a new tab, subscribe its dirty handler, and activate it.</summary>
    private TabItem AddTab(IEditorPane pane, string fullPath)
    {
        var tab = new TabItem(pane, fullPath);
        tab.DirtyHandler = (_, _) => OnPaneDirtyChanged(tab);
        pane.DirtyChanged += tab.DirtyHandler;
        _tabs.Add(tab);
        Activate(tab);
        return tab;
    }

    /// <summary>Test seam: open a pre-built pane (bypassing the factory/disk) so tests can inject a fake editor.</summary>
    internal TabItem OpenForTest(IEditorPane pane, string path) =>
        AddTab(pane, System.IO.Path.GetFullPath(path));

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

        if (tab.Pane.IsDirty)
        {
            // A dirty tab needs the shell's Save/Discard/Cancel prompt. With no confirmer wired up, refuse
            // to close rather than silently discarding the edits — ConfirmClose is always set in-app, so this
            // only guards a mis-wired host, but a silent data loss there would be the worst outcome.
            if (ConfirmClose is not { } confirm)
            {
                return false;
            }

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
                catch (Exception)
                {
                    // Any save failure keeps the tab open rather than losing the edits. A missing/locked
                    // file is the common (IO) case; anything else is an unexpected editor bug — but even so
                    // this must not propagate, because CloseAsync is awaited from async-void callers (window
                    // close, ⌘W, the tab's ✕ button) where an escaping exception crashes the app.
                    return false;
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

    /// <summary>
    /// Reconcile open tabs after a rename in the tree: any tab whose file is <paramref name="oldPath"/> — or
    /// lives under it, when a folder was renamed — is rebound to its new path (content + dirty state kept).
    /// </summary>
    public void RebindPaths(string oldPath, string newPath)
    {
        var old = System.IO.Path.GetFullPath(oldPath);
        var updated = System.IO.Path.GetFullPath(newPath);
        var changed = false;
        foreach (var tab in _tabs)
        {
            if (PathScope.Remap(tab.Path, old, updated) is { } remapped)
            {
                tab.Path = remapped;
                tab.Pane.Rebind(remapped);
                changed = true;
            }
        }

        if (changed)
        {
            RebuildStrip();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Close (without a save prompt) every tab whose file is <paramref name="path"/> or lives under it —
    /// used when the tree deletes a file or folder, so the just-deleted document can't be re-saved.
    /// </summary>
    public void CloseUnder(string path)
    {
        var target = System.IO.Path.GetFullPath(path);
        foreach (var tab in _tabs.Where(t => PathScope.IsUnderOrEqual(t.Path, target)).ToList())
        {
            RemoveTab(tab);
        }
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
            Padding = new Avalonia.Thickness(10, 5),
            MinHeight = 28,
            Foreground = active ? palette.Accent : palette.MutedText,
        };
        label.Click += (_, _) => Activate(tab);
        ToolTip.SetTip(label, tab.Pane.Title);

        // The close button was a bare "✕" glyph with 4px of horizontal padding and *no* vertical padding — its
        // hit box was barely larger than the ink of the character. It is also the one control on the strip where
        // a misclick is destructive (it can trigger a save prompt, or discard), so it gets a real target: 22×22,
        // which clears the WCAG 2.2 §2.5.8 minimum with room to spare, plus a tooltip saying what it does.
        var close = new Button
        {
            Content = "✕",
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(0),
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 4, 0),
            FontSize = 11,
            Foreground = palette.MutedText,
        };
        close.Click += (_, _) => _ = CloseAsync(tab);
        ToolTip.SetTip(close, $"Close {tab.Pane.Title}");
        AutomationProperties.SetName(close, $"Close {tab.Pane.Title}");

        return new Border
        {
            Background = active ? palette.AccentSubtle : Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            // The active tab is already marked by colour *and* a background tint *and* the dirty dot; the
            // underline makes it a fourth, non-colour channel, and matches how the sidebar's nav marks itself.
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 2),
            BorderBrush = active ? palette.Accent : Brushes.Transparent,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { label, close },
            },
        };
    }
}
