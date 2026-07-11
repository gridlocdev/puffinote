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
    public void Save_then_load_round_trips_pinned_folders_in_order()
    {
        SessionStore.Save(_root.FullName, new NotebookSession
        {
            Pinned = [At("Projects"), At(System.IO.Path.Combine("Projects", "Neural Nets"))],
        });

        var loaded = SessionStore.Load(_root.FullName);
        Assert.Equal([At("Projects"), At(System.IO.Path.Combine("Projects", "Neural Nets"))], loaded.Pinned);
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
    public void A_session_written_before_pinned_folders_existed_still_loads()
    {
        // A v1 file with no "Pinned" field must load (as no pins), not be discarded as unreadable.
        var stateDir = Directory.CreateDirectory(System.IO.Path.Combine(_root.FullName, NotebookNode.StateDirectoryName));
        File.WriteAllText(
            System.IO.Path.Combine(stateDir.FullName, "session.json"),
            """{ "Version": 1, "Recent": ["note.md"], "SidebarCollapsed": false, "SidebarWidth": 260 }""");

        var loaded = SessionStore.Load(_root.FullName);
        Assert.Equal([At("note.md")], loaded.Recent);
        Assert.Empty(loaded.Pinned);
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
}
