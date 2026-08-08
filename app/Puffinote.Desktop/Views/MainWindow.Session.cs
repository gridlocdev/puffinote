using Puffinote.Components.Overlays;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Views;

public partial class MainWindow
{
    /// <summary>The open notebook's root path, or null when none is open (test seam).</summary>
    internal string? OpenNotebookPathForTest => _notebook.RootPath;

    /// <summary>
    /// On a plain launch (no notebook on the command line), reopen the notebook from the last session (PRD §5).
    /// If that folder has since moved or been deleted, show a modal offering to open another instead. A launch
    /// where nothing was ever opened simply starts on the empty shell.
    /// </summary>
    public void RestoreLastNotebook()
    {
        if (AppState.Load().LastNotebookPath is not { } path)
        {
            return;
        }

        if (Directory.Exists(path))
        {
            OpenNotebook(path);
            return;
        }

        // The remembered notebook is gone. Defer the prompt until the window is shown — both the overlay
        // (it reads the live theme) and the folder picker (it needs a TopLevel) require a shown window.
        void PromptWhenShown(object? sender, EventArgs e)
        {
            Opened -= PromptWhenShown;
            _ = ShowMissingNotebookPromptAsync();
        }

        Opened += PromptWhenShown;
    }

    /// <summary>Tell the user the remembered notebook has moved, and offer to open one via the folder picker.</summary>
    private async Task ShowMissingNotebookPromptAsync()
    {
        if (_overlayHost.IsOpen)
        {
            return;
        }

        var open = await _overlayHost.ShowModalAsync(new ConfirmDialog(
            "Notebook Moved",
            "The previous notebook has moved or is no longer available. Click below to open a notebook.",
            confirmLabel: "Open Notebook…",
            denyLabel: "Cancel"));

        if (open)
        {
            await OpenNotebookInteractiveAsync();
        }
    }

    private void OnNotebookOpened()
    {
        SidebarForTest.SetRoot(_notebook.Root);

        // Remember this as the notebook to reopen on the next launch (PRD §5). Global, per-user state — kept
        // apart from the per-notebook session below — so a plain launch (no CLI path) comes back here.
        if (_notebook.RootPath is { } opened)
        {
            new AppState { LastNotebookPath = opened }.Save();
        }

        // Resolve Puffinote's overrides for this notebook (per-user + the notebook's own .puffinote/settings.json)
        // before any file opens, so panes inherit the editor's config with these layered on top (PRD §7.5) — and
        // so the sidebar's own chrome settings follow the notebook you just opened.
        ApplySettings();

        // Restore the per-notebook session (PRD §7.1): recent files + sidebar layout. Recency is per-notebook, so
        // a switch loads the new notebook's list (empty when it has none); drop recent entries whose file is
        // now gone so a stale path never shows.
        var session = _notebook.RootPath is { } root ? SessionStore.Load(root) : new NotebookSession();
        _recent.Load(session.Recent);
        SidebarForTest.SetRecent(_notebook.RootPath, _recent.Paths);
        _sidebarLayout.Apply(session.SidebarCollapsed, session.SidebarWidth);

        StartSearchIndex();
        StartWatching();
        UpdatePaneChrome();

        // Reopen the tabs from the last session (PRD §7.3), preserving order + which one was active. Fire-and-forget:
        // opening a pane is async (it loads the file), and the tabs restore independently of the rest of the shell.
        RestoringTabs = RestoreOpenTabsAsync(session);
    }

    /// <summary>
    /// Reopen the tabs saved for this notebook in their strip order, then re-focus the one that was active.
    /// A tab whose file has since been deleted is silently skipped — the same forgiveness the recent list gets.
    /// </summary>
    private async Task RestoreOpenTabsAsync(NotebookSession session)
    {
        foreach (var path in session.OpenTabs)
        {
            if (File.Exists(path))
            {
                await _tabHost.OpenAsync(path); // no-ops on an unsupported type; each open activates its own tab
            }
        }

        // OpenAsync leaves the last-opened tab active, so restore the saved active tab explicitly (when it survived).
        if (session.ActiveTab is { } active &&
            _tabHost.Tabs.FirstOrDefault(t => string.Equals(t.Path, active, StringComparison.OrdinalIgnoreCase)) is { } tab)
        {
            _tabHost.Activate(tab);
        }
    }

    /// <summary>Write the current recent list, open tabs, and sidebar layout to the notebook's session file (best-effort).</summary>
    private void PersistSession()
    {
        if (_notebook.RootPath is not { } root)
        {
            return;
        }

        var width = _sidebarLayout.PersistedWidth;
        SessionStore.Save(root, new NotebookSession
        {
            Recent = _recent.Paths,
            OpenTabs = [.. _tabHost.Tabs.Select(t => t.Path)],
            ActiveTab = _tabHost.ActivePane?.Path,
            SidebarCollapsed = _sidebarLayout.IsCollapsed,
            SidebarWidth = width > 0 ? width : SessionStore.DefaultSidebarWidth,
        });
    }

    /// <summary>Watch the notebook for external changes, refreshing the tree + search index when they land.</summary>
    private void StartWatching()
    {
        _watcher?.Dispose();
        _watcher = _notebook.RootPath is { } root ? new Shell.NotebookWatcher(root, OnNotebookFilesChanged) : null;
    }

    private void OnNotebookFilesChanged()
    {
        SidebarForTest.RefreshTree();
        StartIndexRebuild();
    }

    /// <summary>
    /// Kick a background index rebuild — the one and only way the shell starts one, from all three triggers
    /// (opening a notebook, an external change, a mutation of ours).
    ///
    /// <para>The shell never awaits it: indexing must not block the UI. The task is kept only so a <em>test</em>
    /// can wait for it, because the rebuild ends by writing the index into the notebook's <c>.puffinote/</c>
    /// (off the UI thread, after the rebuild has already published) — and a test that deletes its temp notebook
    /// the moment its assertions pass is otherwise racing that write for the directory.</para>
    /// </summary>
    private void StartIndexRebuild() => IndexRebuildForTest = SearchForTest?.RebuildAsync() ?? Task.CompletedTask;

    /// <summary>The background index rebuild now in flight, to be awaited before a test tears its notebook down.</summary>
    internal Task IndexRebuildForTest { get; private set; } = Task.CompletedTask;

    // ---- Notebook search (PRD §7.2.3) ----------------------------------------------------------------

    /// <summary>Create the per-notebook search service and kick off its first background index build.</summary>
    private void StartSearchIndex()
    {
        SearchForTest?.IndexUpdated -= OnSearchIndexUpdated;

        SearchForTest = new Search.NotebookSearchService(_notebook);
        SearchForTest.IndexUpdated += OnSearchIndexUpdated;
        SidebarForTest.ConfigureSearch(SearchForTest.Search);
        StartIndexRebuild();
    }

    private void OnSearchIndexUpdated() =>
        SidebarForTest.SetSearchStatus(SearchForTest is { IsIndexing: false } s ? $"{s.FileCount} files indexed" : "Indexing…");

    private void ToggleSidebar() => SetSidebarCollapsed(!_sidebarLayout.IsCollapsed);

    /// <summary>
    /// The one road in and out of the collapsed state, whichever affordance asked (⌘\, the menu, the palette,
    /// the header's collapse button, its handle while hidden, or a two-finger swipe). Idempotent, so a swipe
    /// in the direction the sidebar is already in costs nothing — not a session write, and not a focus jump.
    /// </summary>
    private void SetSidebarCollapsed(bool collapsed)
    {
        if (collapsed == _sidebarLayout.IsCollapsed)
        {
            return;
        }

        if (collapsed)
        {
            _sidebarLayout.Collapse();

            // Hiding the pane you were in shouldn't leave focus nowhere (PRD §7.2.1) — hand it to the document.
            _tabHost.ActivePane?.Focus();
        }
        else
        {
            _sidebarLayout.Expand();
        }

        PersistSession(); // remember the collapsed/expanded state per notebook (PRD §7.1)
    }

    /// <summary>Pull the active document's outline into the sidebar's Outline panel (empty when no tab).</summary>
    private void RefreshOutline() =>
        SidebarForTest.SetOutline(_tabHost.ActivePane?.GetOutline() ?? []);

    private void UpdatePaneChrome()
    {
        RefreshCommandStates();
        ApplyShellShortcuts(); // the editor's own gestures come from the active pane's catalog — rebind them
        RefreshOutline();
        SidebarForTest.SetActiveDocument(_tabHost.ActivePane?.Path); // the tree bolds the document you're reading
        ScanForShortcutConflicts();

        var notebookName = _notebook.Root?.Name;
        string title;
        if (_tabHost.ActivePane is { } pane)
        {
            var dirty = pane.IsDirty ? " •" : string.Empty;
            title = notebookName is null
                ? $"Puffinote — {pane.Title}{dirty}"
                : $"Puffinote — {notebookName} — {pane.Title}{dirty}";
        }
        else
        {
            title = notebookName is null ? "Puffinote" : $"Puffinote — {notebookName}";
        }

        // Guard the assignment: Changed fires on every keystroke, but Window.Title is an OS call —
        // only touch it when the text actually changes (e.g. the dirty dot appears/clears).
        if (Title != title)
        {
            Title = title;
        }

        // The title *bar* shows the open document alone — no app name, no notebook — because the notebook
        // is already named at the top of the sidebar and the tab strip is right below. With no tab open
        // there is no document to name, so it falls back to the notebook, then to the app.
        TitleBarForTest.DocumentName = _tabHost.ActivePane?.Title ?? notebookName ?? "Puffinote";
        TitleBarForTest.IsDirty = _tabHost.ActivePane?.IsDirty ?? false;
    }
}
