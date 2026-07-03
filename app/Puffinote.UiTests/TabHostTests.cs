using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Services;
using Puffinote.Components.Commands;
using Puffinote.Desktop.Panes;
using Typage.Control.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>Phase 2B: N-document tab lifecycle — open/focus/close, mixing notes and sheets, dirty prompts.</summary>
public sealed class TabHostTests
{
    /// <summary>A minimal in-memory <see cref="IEditorPane"/> whose dirty state and save behaviour are driven
    /// by the test — used to exercise close paths a real editor can't easily reach (a save that throws).</summary>
    private sealed class FakePane : IEditorPane
    {
        private bool _dirty;

        public FakePane(string path) => Path = path;

        public Control View { get; } = new Border();
        public string Title => System.IO.Path.GetFileName(Path ?? string.Empty);
        public string? Path { get; private set; }
        public Exception? SaveThrows { get; set; }
        public bool Saved { get; private set; }

        public bool IsDirty
        {
            get => _dirty;
            set { _dirty = value; DirtyChanged?.Invoke(this, EventArgs.Empty); }
        }

        public event EventHandler? DirtyChanged;

        public Task LoadAsync(string path) => Task.CompletedTask;

        public Task SaveAsync()
        {
            if (SaveThrows is { } ex)
            {
                throw ex;
            }

            Saved = true;
            IsDirty = false;
            return Task.CompletedTask;
        }

        public void Rebind(string newPath) => Path = newPath;
        public IReadOnlyList<AppCommand> Commands => Array.Empty<AppCommand>();
        public IReadOnlyList<OutlineItem> GetOutline() => Array.Empty<OutlineItem>();
        public void Dispose() { }
    }

    [AvaloniaFact]
    public async Task Closing_a_dirty_tab_with_no_confirmer_refuses_rather_than_discarding()
    {
        var host = new TabHost();
        host.OpenForTest(new FakePane("/notebook/note.md") { IsDirty = true }, "/notebook/note.md");

        // ConfirmClose left null (a mis-wired host): the dirty tab must be kept, not silently discarded.
        Assert.False(await host.CloseActiveAsync());
        Assert.Equal(1, host.Count);
    }

    [AvaloniaFact]
    public async Task A_non_IO_save_failure_keeps_the_tab_open_and_does_not_propagate()
    {
        var host = new TabHost { ConfirmClose = _ => Task.FromResult(CloseDecision.Save) };
        host.OpenForTest(
            new FakePane("/notebook/note.md") { IsDirty = true, SaveThrows = new InvalidOperationException("boom") },
            "/notebook/note.md");

        // A non-IO exception from SaveAsync must not escape (it would crash the async-void window-close
        // handler); CloseAsync swallows it, keeps the tab, and reports the close as cancelled.
        Assert.False(await host.CloseActiveAsync());
        Assert.Equal(1, host.Count);
    }

    private static (string md, string ods) Seed(DirectoryInfo dir)
    {
        var md = Path.Combine(dir.FullName, "note.md");
        File.WriteAllText(md, "# note");
        var ods = Path.Combine(dir.FullName, "book.ods");
        File.WriteAllBytes(ods, WorkbookSession.NewBlank().SerializeToBytes());
        return (md, ods);
    }

    [AvaloniaFact]
    public async Task Opens_notes_and_sheets_in_tabs_and_focuses_an_already_open_file()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-tabs");
        try
        {
            var (md, ods) = Seed(dir);
            var host = new TabHost();

            await host.OpenAsync(md);
            await host.OpenAsync(ods);
            Assert.Equal(2, host.Count);
            Assert.Equal("book.ods", host.ActivePane!.Title); // most-recently opened is active

            await host.OpenAsync(md); // already open → focus, don't duplicate
            Assert.Equal(2, host.Count);
            Assert.Equal("note.md", host.ActivePane!.Title);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Closing_a_clean_tab_removes_it_and_activates_a_neighbour()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-tabs-close");
        try
        {
            var (md, ods) = Seed(dir);
            var host = new TabHost();
            await host.OpenAsync(md);
            var noteTab = host.Tabs[0];
            await host.OpenAsync(ods);

            Assert.True(await host.CloseActiveAsync()); // book.ods is clean → closes without a prompt
            Assert.Equal(1, host.Count);
            Assert.Same(noteTab.Pane, host.ActivePane);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Dirty_close_prompt_cancels_saves_or_discards()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-tabs-dirty");
        try
        {
            var (md, _) = Seed(dir);
            var host = new TabHost();
            await host.OpenAsync(md);

            // Make the note dirty through the embedded editor (LoadRecovered = load left unsaved).
            ((MarkdownEditorView)host.ActivePane!.View).LoadRecovered("# note\n\nunsaved edit");
            Assert.True(host.ActivePane.IsDirty);

            // Cancel keeps the tab.
            host.ConfirmClose = _ => Task.FromResult(CloseDecision.Cancel);
            Assert.False(await host.CloseActiveAsync());
            Assert.Equal(1, host.Count);

            // Save writes to disk, then closes.
            host.ConfirmClose = _ => Task.FromResult(CloseDecision.Save);
            Assert.True(await host.CloseActiveAsync());
            Assert.Equal(0, host.Count);
            Assert.Contains("unsaved edit", File.ReadAllText(md));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Discard_closes_without_writing_the_edit()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-tabs-discard");
        try
        {
            var (md, _) = Seed(dir);
            var host = new TabHost();
            await host.OpenAsync(md);
            ((MarkdownEditorView)host.ActivePane!.View).LoadRecovered("# note\n\nthrowaway");

            host.ConfirmClose = _ => Task.FromResult(CloseDecision.Discard);
            Assert.True(await host.CloseActiveAsync());
            Assert.Equal(0, host.Count);
            Assert.DoesNotContain("throwaway", File.ReadAllText(md)); // the edit was not saved
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Editing_while_already_dirty_does_not_refire_tab_changes()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-tabs-perf");
        try
        {
            var (md, _) = Seed(dir);
            var host = new TabHost();
            await host.OpenAsync(md);
            var view = (MarkdownEditorView)host.ActivePane!.View;

            var changed = 0;
            host.Changed += (_, _) => changed++;

            // Three edits that all keep the tab dirty. Only the first (clean→dirty) may refresh the strip;
            // the rest must be skipped (the guard), or a doc rebuilds the whole strip on every keystroke.
            view.LoadRecovered("# note\n\nedit one");
            view.LoadRecovered("# note\n\nedit two");
            view.LoadRecovered("# note\n\nedit three");

            Assert.True(host.ActivePane!.IsDirty);
            Assert.True(changed <= 1, $"expected ≤1 Changed for same-dirty-state edits, got {changed}");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task RebindPaths_repoints_an_open_tab_after_a_rename_preserving_edits()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-tabs-rename");
        try
        {
            var (md, _) = Seed(dir);
            var host = new TabHost();
            await host.OpenAsync(md);
            ((MarkdownEditorView)host.ActivePane!.View).LoadRecovered("# note\n\nlive edit");
            Assert.True(host.ActivePane.IsDirty);

            // Rename on disk (as the tree mutation would), then reconcile the open tab.
            var renamed = Path.Combine(dir.FullName, "renamed.md");
            File.Move(md, renamed);
            host.RebindPaths(md, renamed);

            Assert.Equal("renamed.md", host.ActivePane!.Title);
            Assert.Equal(renamed, host.ActivePane.Path);
            Assert.True(host.ActivePane.IsDirty);        // the unsaved edit survived the rename

            await host.ActivePane.SaveAsync();            // and now saves to the new path
            Assert.Contains("live edit", File.ReadAllText(renamed));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task RebindPaths_reparents_tabs_under_a_renamed_folder()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-tabs-folder-rename");
        try
        {
            var sub = Path.Combine(dir.FullName, "sub");
            Directory.CreateDirectory(sub);
            var note = Path.Combine(sub, "note.md");
            File.WriteAllText(note, "# n");
            var host = new TabHost();
            await host.OpenAsync(note);

            var renamedDir = Path.Combine(dir.FullName, "renamed");
            Directory.Move(sub, renamedDir);
            host.RebindPaths(sub, renamedDir);

            Assert.Equal(Path.Combine(renamedDir, "note.md"), host.ActivePane!.Path);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task CloseUnder_closes_the_tab_of_a_deleted_file()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-tabs-delete");
        try
        {
            var (md, ods) = Seed(dir);
            var host = new TabHost();
            await host.OpenAsync(md);
            await host.OpenAsync(ods);

            host.CloseUnder(md); // the note was deleted → its tab closes, no save prompt

            Assert.Equal(1, host.Count);
            Assert.Equal("book.ods", host.ActivePane!.Title);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
