using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Views;

public partial class MainWindow
{
    private void OnNotebookOpened()
    {
        SidebarForTest.SetRoot(_notebook.Root);

        // Resolve Puffinote's overrides for this notebook (per-user + the notebook's own .puffinote/settings.json)
        // before any file opens, so panes inherit the editor's config with these layered on top (PRD §7.5).
        _tabHost.Settings = PuffinoteSettings.Resolve(_settings, _notebook.RootPath);

        // Restore the per-notebook session (PRD §7.1): recent files + sidebar layout. Recency is per-notebook, so
        // a switch loads the new notebook's list (empty when it has none); drop recent entries whose file is
        // now gone so a stale path never shows.
        var session = _notebook.RootPath is { } root ? SessionStore.Load(root) : new NotebookSession();
        _recent.Load(session.Recent);
        SidebarForTest.SetRecent(_notebook.RootPath, _recent.Paths);
        SidebarForTest.SetPinnedFolders(session.Pinned);
        _sidebarLayout.Apply(session.SidebarCollapsed, session.SidebarWidth);

        StartSearchIndex();
        StartWatching();
        UpdatePaneChrome();
    }

    /// <summary>Write the current recent list + sidebar layout to the notebook's session file (best-effort).</summary>
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
            Pinned = SidebarForTest.PinnedFolders,
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
        _ = SearchForTest?.RebuildAsync();
    }

    // ---- Notebook search (PRD §7.2.3) ----------------------------------------------------------------

    /// <summary>Create the per-notebook search service and kick off its first background index build.</summary>
    private void StartSearchIndex()
    {
        SearchForTest?.IndexUpdated -= OnSearchIndexUpdated;

        SearchForTest = new Search.NotebookSearchService(_notebook);
        SearchForTest.IndexUpdated += OnSearchIndexUpdated;
        SidebarForTest.ConfigureSearch(SearchForTest.Search);
        _ = SearchForTest.RebuildAsync();
    }

    private void OnSearchIndexUpdated() =>
        SidebarForTest.SetSearchStatus(SearchForTest is { IsIndexing: false } s ? $"{s.FileCount} files indexed" : "Indexing…");

    private void ToggleSidebar()
    {
        _sidebarLayout.Toggle();
        PersistSession(); // remember the collapsed/expanded state per notebook (PRD §7.1)
    }

    /// <summary>Pull the active document's outline into the sidebar's Outline panel (empty when no tab).</summary>
    private void RefreshOutline() =>
        SidebarForTest.SetOutline(_tabHost.ActivePane?.GetOutline() ?? []);

    private void UpdatePaneChrome()
    {
        RefreshCommandStates();
        RefreshOutline();
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
    }
}
