using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Puffinote.Components.Core.Documents;
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

    /// <summary>The file as this shell last left it — the baseline an external write is noticed against.</summary>
    public FileStamp? Stamp { get; set; }

    /// <summary>
    /// Somebody else wrote this file while the pane held unsaved edits. The pane is <em>not</em> reloaded
    /// (that would throw the edits away without asking); the flag is what makes saving stop and ask first.
    /// </summary>
    public bool ExternallyChanged { get; set; }
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
    private readonly ScrollViewer _stripScroll;
    private readonly Border _stripBar;
    private readonly Border _content = new();
    private readonly Control _placeholder;
    private readonly List<TabItem> _tabs = [];

    /// <summary>The strip button built for <see cref="ActiveTab"/>, kept so the reveal pass knows what to scroll to.</summary>
    private Control? _activeTabControl;

    public TabHost()
    {
        var palette = Tokens.Active;
        _placeholder = new TextBlock
        {
            Text = "Open a file from the sidebar to start.\nNotes and sheets can be open in tabs together.",
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = palette.MutedText,
        };
        _content.Child = _placeholder;

        _stripScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _strip,
        };

        _stripBar = new Border
        {
            Background = palette.Surface,
            BorderBrush = palette.SurfaceBorder,
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Child = _stripScroll,
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

    private EditorConfig _editorConfig = EditorConfig.Resolve(new PuffinoteSettings());

    /// <summary>
    /// Puffinote's override settings, layered over each editor's own config (PRD §7.5). Setting this resolves
    /// every editor's config once (reading each editor's own settings file), so opening a document doesn't
    /// re-read them per tab (P7); MainWindow assigns it per notebook, and again when Preferences changes
    /// either Puffinote's overrides or an editor's own settings.
    /// </summary>
    public PuffinoteSettings Settings
    {
        get;
        set
        {
            field = value;
            _editorConfig = EditorConfig.Resolve(value);

            // Re-assert the preference over any session-only toggle. Assigning Settings happens on exactly
            // two occasions — opening a notebook and accepting Preferences — and on both the persisted
            // choice is the one that should be showing: the new notebook may hide the strip where the last
            // one showed it, and a preference you just set must visibly take effect.
            ShowTabBar = value.ShowTabBar ?? true;

            // Re-seed the tabs that are already open. Without this a settings change would only ever show
            // up in the next document opened, which reads as the setting not having worked.
            foreach (var tab in _tabs)
            {
                (tab.Pane as IConfigurablePane)?.ApplyConfig(_editorConfig);
            }
        }
    } = new();

    /// <summary>
    /// Whether the tab strip is shown at all. Seeded from <see cref="PuffinoteSettings.ShowTabBar"/> whenever
    /// <see cref="Settings"/> is assigned, and flipped on its own by the shell's Toggle Tab Bar command — which
    /// is why this is a property of the host rather than something read straight off the settings record: the
    /// command changes what you see for this session without writing a preference to disk.
    /// <para>
    /// Hiding the strip hides nothing else: the tabs stay open, ⌘⇧[ / ⌘⇧] still walk them, and the title bar
    /// still names the active document — so, unlike the collapsed sidebar, there's nothing here that needs a
    /// handle to get back to.
    /// </para>
    /// </summary>
    public bool ShowTabBar
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            ApplyStripVisibility();
        }
    } = true;

    /// <summary>Raised on any change to the tabs or the active document's dirty state (for the window title/Save).</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised whenever any open pane reports an edit — on every keystroke, not only when a tab flips
    /// clean⇄dirty like <see cref="Changed"/>. It exists for the shell's auto-save countdown, which has to
    /// be pushed back by each edit rather than only by the first one after a save.
    /// </summary>
    public event EventHandler? DocumentEdited;

    public IReadOnlyList<TabItem> Tabs => _tabs;

    public IEditorPane? ActivePane => ActiveTab?.Pane;

    /// <summary>The active tab itself (not just its pane) — what a tab-scoped command like close-others acts on.</summary>
    public TabItem? ActiveTab { get; private set; }

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

        var pane = EditorPaneFactory.Create(path, _editorConfig);
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
        var tab = new TabItem(pane, fullPath) { Stamp = FileStamp.For(fullPath) };
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

        ActiveTab = tab;
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

    public Task<bool> CloseActiveAsync() => ActiveTab is { } a ? CloseAsync(a) : Task.FromResult(true);

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
    /// Close every tab <em>except</em> <paramref name="keep"/> — the tab that was right-clicked, or the active
    /// one when run from the palette/menu. Same per-tab <see cref="CloseAsync"/> path as close-all, so each
    /// dirty tab gets its own Save/Don't-Save/Cancel prompt and a Cancel stops the run (leaving the tabs it
    /// hasn't reached yet open). <paramref name="keep"/> is activated first, so the document you chose to keep
    /// is the one in front whether or not the run finishes — and so a cancelled run doesn't strand you on some
    /// arbitrary survivor. Returns false if the user cancelled.
    /// </summary>
    public async Task<bool> CloseOthersAsync(TabItem keep)
    {
        if (!_tabs.Contains(keep))
        {
            return true;
        }

        Activate(keep);
        foreach (var tab in _tabs.Where(t => !ReferenceEquals(t, keep)).ToList())
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

    /// <summary>
    /// Bring the open tabs back in step with the disk after something outside Puffinote wrote to it — a
    /// <c>git pull</c>, another editor, the same notebook open on another machine's sync folder. The file
    /// tree and the search index already refreshed on this signal; a tab that kept showing the bytes from
    /// before is the one surface still lying about what is in the file, and the one that can write the old
    /// bytes back over the new ones.
    /// <para>
    /// A clean tab reloads: it has nothing of the user's to lose, and showing the current file is the whole
    /// point. A <em>dirty</em> tab is left exactly as it is and only flagged — reloading it would discard
    /// unsaved work to resolve a conflict the user has not been told about yet. <see cref="ExternallyChangedTabs"/>
    /// is how the shell finds those, so saving can stop and ask.
    /// </para>
    /// </summary>
    public async Task RefreshFromDiskAsync()
    {
        bool changed = false;
        foreach (var tab in _tabs.ToList())
        {
            // A file that vanished is not this pass's business: a delete made through the tree closes its
            // tabs (CloseUnder), and a delete from outside is more likely a rename or an atomic write
            // caught mid-flight than an instruction to throw the document away.
            if (FileStamp.For(tab.Path) is not { } stamp || stamp == tab.Stamp)
            {
                continue;
            }

            if (tab.Pane.IsDirty)
            {
                changed |= !tab.ExternallyChanged;
                tab.ExternallyChanged = true;
                continue;
            }

            await ReloadAsync(tab); // rebuilds the strip itself on success
        }

        if (changed)
        {
            RebuildStrip();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Re-read <paramref name="tab"/>'s file into its existing pane, discarding whatever the pane held.
    /// Returns false when the file could not be read — a writer may still have it open half-written, and
    /// the next watcher tick will find it settled, so a failure here leaves the tab alone rather than
    /// replacing a readable document with an error.
    /// <para>
    /// Catching broadly, for the reason <see cref="OpenAsync"/> does: a pane raises its own format
    /// exception for a file it cannot parse (a board with a syntax error, a truncated <c>.ods</c>), and a
    /// pull is exactly how a half-written or conflict-marked file arrives. This runs fire-and-forget from
    /// the watcher, so a parse error escaping here took down the rest of the pass — every tab after the
    /// bad one silently kept its stale content — and went unobserved.
    /// </para>
    /// </summary>
    public async Task<bool> ReloadAsync(TabItem tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        try
        {
            await tab.Pane.LoadAsync(tab.Path);
        }
        catch (Exception)
        {
            return false;
        }

        // Stamped from disk after the read, not from the stamp that triggered it: if the file changed again
        // while we were reading it, this pass saw the older bytes and the next one has to notice.
        tab.Stamp = FileStamp.For(tab.Path);
        tab.ExternallyChanged = false;

        // The strip has to be rebuilt even when the dirty state didn't move — the tab's tooltip was saying
        // the file had changed underneath, and it no longer has.
        RebuildStrip();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>The dirty tabs whose files were written underneath them, in tab order.</summary>
    public IReadOnlyList<TabItem> ExternallyChangedTabs => [.. _tabs.Where(t => t.ExternallyChanged)];

    /// <summary>Activate the next (<paramref name="direction"/> = +1) or previous (-1) tab, wrapping around.</summary>
    public void ActivateRelative(int direction)
    {
        if (ActiveTab is null || _tabs.Count < 2)
        {
            return;
        }

        int index = (_tabs.IndexOf(ActiveTab) + direction + _tabs.Count) % _tabs.Count;
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

        if (ActiveTab == tab)
        {
            ActiveTab = _tabs.Count > 0 ? _tabs[Math.Min(index, _tabs.Count - 1)] : null;
            _content.Child = ActiveTab is { } next ? next.Pane.View : _placeholder;
        }

        RebuildStrip();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPaneDirtyChanged(TabItem tab)
    {
        // A pane going clean means its bytes are now the file's bytes — it just saved, or we just reloaded
        // it. Either way the baseline an external write is noticed against has moved, and the conflict (if
        // there was one) is over: the user was asked before the save that resolved it.
        if (!tab.Pane.IsDirty)
        {
            tab.Stamp = FileStamp.For(tab.Path);
            tab.ExternallyChanged = false;
        }

        // Every keystroke is an edit, and the auto-save countdown is measured from the last one — so this
        // fires before the clean⇄dirty filter below, which would otherwise report only the first keystroke
        // after a save and let the countdown expire in the middle of a sentence.
        DocumentEdited?.Invoke(this, EventArgs.Empty);

        // The tab strip (and the window title), by contrast, only change when a tab flips clean⇄dirty.
        // Skip the rebuild and the Changed fan-out otherwise, so typing doesn't rebuild the whole strip
        // and re-query every command per character.
        if (tab.RenderedDirty == tab.Pane.IsDirty)
        {
            return;
        }

        RebuildStrip();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Show the strip only when it is both wanted and has something to say: an empty strip is chrome with no
    /// content, so a notebook with no tabs open looks the same either way.
    /// </summary>
    private void ApplyStripVisibility() => _stripBar.IsVisible = ShowTabBar && _tabs.Count > 0;

    private void RebuildStrip()
    {
        ApplyStripVisibility();
        _strip.Children.Clear();
        _activeTabControl = null;
        foreach (var tab in _tabs)
        {
            tab.RenderedDirty = tab.Pane.IsDirty;
            var control = BuildTab(tab);
            if (ReferenceEquals(tab, ActiveTab))
            {
                _activeTabControl = control;
            }

            _strip.Children.Add(control);
        }

        // With enough documents open the strip scrolls, and the tab you just opened from the sidebar (or
        // reached with ⌘⇧[ / ⌘⇧]) can be off the end of it — so the one thing that says which document you are
        // looking at is the one thing you can't see. Bring it back on screen. Posted because these buttons were
        // built a statement ago and have no measured bounds until the next layout pass.
        Dispatcher.UIThread.Post(RevealActiveTab, DispatcherPriority.Loaded);
    }

    /// <summary>Scroll the strip the shortest distance that puts the active tab fully on screen.</summary>
    private void RevealActiveTab()
    {
        if (_activeTabControl is not { } tab)
        {
            return;
        }

        // The strip may not have been laid out since the rebuild — callers that drive the dispatcher by hand
        // (the headless tests) can run this job before the layout pass rather than after it.
        _stripScroll.UpdateLayout();

        if (tab.Bounds.Width <= 0 || tab.TranslatePoint(default, _stripScroll) is not { } origin)
        {
            return; // not on screen at all: no window, or the editor area is collapsed
        }

        var offset = _stripScroll.Offset;
        _stripScroll.Offset = offset.WithX(TabStripScroll.Reveal(
            offset.X,
            _stripScroll.Viewport.Width,
            _stripScroll.Extent.Width,
            offset.X + origin.X, // origin is viewport-relative; the helper works in content coordinates
            tab.Bounds.Width));
    }

    private Control BuildTab(TabItem tab)
    {
        var palette = Tokens.Active;
        bool active = ReferenceEquals(tab, ActiveTab);

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

        // A conflict the user hasn't been told about is a conflict that surprises them at the save prompt.
        // Say it on the tab itself, in words rather than a second dot nobody can decode.
        ToolTip.SetTip(label, tab.ExternallyChanged
            ? $"{tab.Pane.Title}\nChanged on disk while you were editing it — saving will ask first."
            : tab.Pane.Title);

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

            // Built per tab and closed over *this* tab, so every item targets the tab that was actually
            // right-clicked rather than whichever one happens to be active. The strip is rebuilt on every
            // tab change, so the menu's enablement can be decided here and stays current.
            ContextMenu = BuildTabMenu(tab),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { label, close },
            },
        };
    }

    /// <summary>
    /// A tab's right-click menu: the three closing verbs, scoped to <paramref name="tab"/>. It is deliberately
    /// only about closing — the tab strip has no other per-tab state to offer, and the file-level commands
    /// (rename, reveal, delete) belong to the tree row that owns the file, not to a view of it.
    /// </summary>
    private ContextMenu BuildTabMenu(TabItem tab) =>
        new() { ItemsSource = BuildTabMenuItems(tab) };

    private List<MenuItem> BuildTabMenuItems(TabItem tab)
    {
        var closeOthers = Item("Close Other Tabs", () => _ = CloseOthersAsync(tab));

        // With one tab open, "close the others" has nothing to close: shown (so the menu doesn't change shape
        // as you open files) but greyed, rather than silently doing nothing when picked.
        closeOthers.IsEnabled = _tabs.Count > 1;

        return
        [
            Item("Close Tab", () => _ = CloseAsync(tab)),
            closeOthers,
            Item("Close All Tabs", () => _ = CloseAllAsync()),
        ];
    }

    private static MenuItem Item(string header, Action run)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => run();
        return item;
    }

    /// <summary>Whether the tab strip is actually on screen — wanted <em>and</em> non-empty (test seam).</summary>
    internal bool IsStripVisibleForTest => _stripBar.IsVisible;

    /// <summary>The menu a right click on <paramref name="tab"/> would show, live items (test seam).</summary>
    internal IReadOnlyList<MenuItem> TabMenuForTest(TabItem tab) => BuildTabMenuItems(tab);
}
