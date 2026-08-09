using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

public sealed class SessionStoreTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("puffinote-session");

    public void Dispose() => _root.Delete(recursive: true);

    private string At(string relative) => System.IO.Path.Combine(_root.FullName, relative);

    [Fact]
    public void Save_then_load_round_trips_recent_and_sidebar_state_and_writes_a_gitignore()
    {
        var session = new NotebookSession
        {
            Recent = [At("note.md"), At(System.IO.Path.Combine("sub", "deep.md"))],
            SidebarCollapsed = true,
            SidebarWidth = 320,
        };

        SessionStore.Save(_root.FullName, session);

        // Volatile state lands in .puffinote/, ignored by git (the same list the search index writes).
        var stateDir = System.IO.Path.Combine(_root.FullName, NotebookNode.StateDirectoryName);
        Assert.True(File.Exists(System.IO.Path.Combine(stateDir, "session.json")));
        Assert.Contains("session.json", File.ReadAllText(System.IO.Path.Combine(stateDir, ".gitignore")));

        var loaded = SessionStore.Load(_root.FullName);
        Assert.Equal([At("note.md"), At(System.IO.Path.Combine("sub", "deep.md"))], loaded.Recent);
        Assert.True(loaded.SidebarCollapsed);
        Assert.Equal(320, loaded.SidebarWidth);
    }

    [Fact]
    public void Save_then_load_round_trips_open_tabs_in_order_and_the_active_tab()
    {
        SessionStore.Save(_root.FullName, new NotebookSession
        {
            OpenTabs = [At("budget.ods"), At(System.IO.Path.Combine("sub", "notes.md"))],
            ActiveTab = At(System.IO.Path.Combine("sub", "notes.md")),
        });

        var loaded = SessionStore.Load(_root.FullName);
        Assert.Equal([At("budget.ods"), At(System.IO.Path.Combine("sub", "notes.md"))], loaded.OpenTabs);
        Assert.Equal(At(System.IO.Path.Combine("sub", "notes.md")), loaded.ActiveTab);
    }

    [Fact]
    public void Open_tab_paths_are_stored_relative_so_a_moved_notebook_still_resolves_them()
    {
        SessionStore.Save(_root.FullName, new NotebookSession
        {
            OpenTabs = [At("budget.ods")],
            ActiveTab = At("budget.ods"),
        });

        var json = File.ReadAllText(System.IO.Path.Combine(_root.FullName, NotebookNode.StateDirectoryName, "session.json"));
        Assert.Contains("budget.ods", json);
        Assert.DoesNotContain(_root.FullName, json); // no absolute path leaks into the file
    }

    [Fact]
    public void A_session_written_before_open_tabs_existed_still_loads()
    {
        // A v1 file with no "OpenTabs"/"ActiveTab" fields must load (as no tabs), not be discarded as unreadable.
        var stateDir = Directory.CreateDirectory(System.IO.Path.Combine(_root.FullName, NotebookNode.StateDirectoryName));
        File.WriteAllText(
            System.IO.Path.Combine(stateDir.FullName, "session.json"),
            """{ "Version": 1, "Recent": ["note.md"], "SidebarCollapsed": false, "SidebarWidth": 260 }""");

        var loaded = SessionStore.Load(_root.FullName);
        Assert.Equal([At("note.md")], loaded.Recent);
        Assert.Empty(loaded.OpenTabs);
        Assert.Null(loaded.ActiveTab);
    }

    [Fact]
    public void A_session_written_by_an_older_sidebar_still_loads()
    {
        // Two shapes at once: a v1 file with no "OpenTabs" (written before tabs were remembered) and with a
        // leftover "Pinned" (written while the sidebar still had pinned folders). A missing member means
        // "nothing remembered here yet" and an unknown one is ignored — neither may discard the file.
        var stateDir = Directory.CreateDirectory(System.IO.Path.Combine(_root.FullName, NotebookNode.StateDirectoryName));
        File.WriteAllText(
            System.IO.Path.Combine(stateDir.FullName, "session.json"),
            """{ "Version": 1, "Recent": ["note.md"], "SidebarCollapsed": false, "SidebarWidth": 260, "Pinned": ["Projects"] }""");

        var loaded = SessionStore.Load(_root.FullName);
        Assert.Equal([At("note.md")], loaded.Recent);
        Assert.Empty(loaded.OpenTabs);
    }

    [Fact]
    public void Recent_paths_are_stored_relative_so_a_moved_notebook_still_resolves_them()
    {
        SessionStore.Save(_root.FullName, new NotebookSession { Recent = [At("note.md")] });

        // The on-disk form is relative to the notebook root — no absolute path leaks into the file.
        var json = File.ReadAllText(System.IO.Path.Combine(_root.FullName, NotebookNode.StateDirectoryName, "session.json"));
        Assert.Contains("note.md", json);
        Assert.DoesNotContain(_root.FullName, json);
    }

    [Fact]
    public void Load_returns_the_default_session_when_absent_or_corrupt()
    {
        var fresh = SessionStore.Load(_root.FullName); // nothing written yet
        Assert.Empty(fresh.Recent);
        Assert.False(fresh.SidebarCollapsed);
        Assert.Equal(SessionStore.DefaultSidebarWidth, fresh.SidebarWidth);

        var stateDir = Directory.CreateDirectory(System.IO.Path.Combine(_root.FullName, NotebookNode.StateDirectoryName));
        File.WriteAllText(System.IO.Path.Combine(stateDir.FullName, "session.json"), "{ not valid json");
        Assert.Empty(SessionStore.Load(_root.FullName).Recent); // corrupt → default, not a throw
    }

    /// <summary>
    /// The zoom a tab was left at travels with the tab list, keyed by path and stored relative to the
    /// notebook root like everything else here — so a notebook that is moved or renamed still resolves it.
    /// </summary>
    [Fact]
    public void Save_then_load_round_trips_each_tabs_zoom()
    {
        SessionStore.Save(_root.FullName, new NotebookSession
        {
            OpenTabs = [At("note.md"), At(System.IO.Path.Combine("sub", "budget.ods"))],
            TabZoom = new Dictionary<string, double>
            {
                [At("note.md")] = 1.25,
                [At(System.IO.Path.Combine("sub", "budget.ods"))] = 2.0,
            },
        });

        var loaded = SessionStore.Load(_root.FullName);
        Assert.Equal(1.25, loaded.TabZoom[At("note.md")]);
        Assert.Equal(2.0, loaded.TabZoom[At(System.IO.Path.Combine("sub", "budget.ods"))]);
    }

    /// <summary>
    /// A session written before tab zoom existed still loads — an absent map means "nothing remembered",
    /// not a session to throw away — and a factor a hand-edit put outside the ladder is clamped onto it
    /// rather than handed to an editor that would have to clamp it anyway.
    /// </summary>
    [Fact]
    public void An_older_session_loads_and_a_wild_zoom_is_clamped_onto_the_ladder()
    {
        var stateDir = Directory.CreateDirectory(System.IO.Path.Combine(_root.FullName, NotebookNode.StateDirectoryName));
        File.WriteAllText(
            System.IO.Path.Combine(stateDir.FullName, "session.json"),
            """
            { "Version": 1, "Recent": [], "SidebarCollapsed": false, "SidebarWidth": 260,
              "OpenTabs": ["note.md"], "ActiveTab": "note.md" }
            """);

        var older = SessionStore.Load(_root.FullName);
        Assert.Equal([At("note.md")], older.OpenTabs);
        Assert.Empty(older.TabZoom);

        SessionStore.Save(_root.FullName, new NotebookSession
        {
            TabZoom = new Dictionary<string, double> { [At("note.md")] = 99 },
        });
        Assert.Equal(Puffinote.Components.Theming.ZoomScale.Max, SessionStore.Load(_root.FullName).TabZoom[At("note.md")]);
    }
}
