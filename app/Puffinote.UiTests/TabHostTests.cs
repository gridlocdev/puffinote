using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        public FakePane(string path) => Path = path;

        public Control View { get; } = new Border();
        public void Focus() => View.Focus();
        public string Title => System.IO.Path.GetFileName(Path ?? string.Empty);
        public string? Path { get; private set; }
        public Exception? SaveThrows { get; set; }
        public bool Saved { get; private set; }

        public bool IsDirty
        {
            get;
            set
            {
                field = value;
                DirtyChanged?.Invoke(this, EventArgs.Empty);
            }
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
        public IReadOnlyList<AppCommand> Commands => [];
        public IReadOnlyList<OutlineItem> GetOutline() => [];
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

    [AvaloniaFact]
    public async Task Close_all_closes_every_tab()
    {
        var host = new TabHost();
        host.OpenForTest(new FakePane("/notebook/one.md"), "/notebook/one.md");
        host.OpenForTest(new FakePane("/notebook/two.md"), "/notebook/two.md");

        Assert.True(await host.CloseAllAsync());
        Assert.Equal(0, host.Count);
        Assert.Null(host.ActivePane);
    }

    [AvaloniaFact]
    public async Task Close_all_prompts_per_dirty_tab_and_a_cancel_stops_the_run()
    {
        // Close-all is close-one, N times — so a dirty tab gets the same Save/Don't-Save/Cancel prompt, and
        // Cancel must abort the whole run rather than discarding it (or ploughing on through the rest).
        var host = new TabHost();
        host.OpenForTest(new FakePane("/notebook/clean.md"), "/notebook/clean.md");
        var dirty = new FakePane("/notebook/dirty.md") { IsDirty = true };
        host.OpenForTest(dirty, "/notebook/dirty.md");
        host.OpenForTest(new FakePane("/notebook/last.md"), "/notebook/last.md");

        var prompted = 0;
        host.ConfirmClose = _ =>
        {
            prompted++;
            return Task.FromResult(CloseDecision.Cancel);
        };

        Assert.False(await host.CloseAllAsync());
        Assert.Equal(1, prompted);           // only the dirty tab prompts
        Assert.False(dirty.Saved);
        Assert.Equal(2, host.Count);         // clean.md closed; dirty.md and the untouched last.md remain

        // Answering Save instead writes the dirty document and finishes the run.
        host.ConfirmClose = _ => Task.FromResult(CloseDecision.Save);
        Assert.True(await host.CloseAllAsync());
        Assert.True(dirty.Saved);
        Assert.Equal(0, host.Count);
    }

    [AvaloniaFact]
    public async Task Close_others_keeps_the_clicked_tab_and_makes_it_active()
    {
        var host = new TabHost();
        host.OpenForTest(new FakePane("/notebook/one.md"), "/notebook/one.md");
        var keep = host.OpenForTest(new FakePane("/notebook/two.md"), "/notebook/two.md");
        host.OpenForTest(new FakePane("/notebook/three.md"), "/notebook/three.md");

        // three.md is active (opened last) — closing the others is scoped to the tab that was *clicked*,
        // which then becomes the active one.
        Assert.True(await host.CloseOthersAsync(keep));
        Assert.Equal(1, host.Count);
        Assert.Same(keep.Pane, host.ActivePane);
    }

    [AvaloniaFact]
    public async Task Close_others_prompts_per_dirty_tab_and_a_cancel_stops_the_run()
    {
        var host = new TabHost();
        var dirty = new FakePane("/notebook/dirty.md") { IsDirty = true };
        host.OpenForTest(dirty, "/notebook/dirty.md");
        host.OpenForTest(new FakePane("/notebook/clean.md"), "/notebook/clean.md");
        var keep = host.OpenForTest(new FakePane("/notebook/keep.md"), "/notebook/keep.md");

        var prompted = 0;
        host.ConfirmClose = _ =>
        {
            prompted++;
            return Task.FromResult(CloseDecision.Cancel);
        };

        // Same per-tab close path as close-all: only the dirty tab prompts, and Cancel aborts the run before
        // it reaches clean.md — which stays open alongside the tab we were keeping.
        Assert.False(await host.CloseOthersAsync(keep));
        Assert.Equal(1, prompted);
        Assert.False(dirty.Saved);
        Assert.Equal(3, host.Count);
        Assert.Same(keep.Pane, host.ActivePane);

        host.ConfirmClose = _ => Task.FromResult(CloseDecision.Save);
        Assert.True(await host.CloseOthersAsync(keep));
        Assert.True(dirty.Saved);
        Assert.Equal(1, host.Count);
    }

    [AvaloniaFact]
    public async Task Close_others_on_the_only_tab_closes_nothing()
    {
        var host = new TabHost();
        var only = host.OpenForTest(new FakePane("/notebook/one.md"), "/notebook/one.md");

        Assert.True(await host.CloseOthersAsync(only));
        Assert.Equal(1, host.Count);
        Assert.Same(only.Pane, host.ActivePane);
    }

    [AvaloniaFact]
    public async Task A_tabs_context_menu_offers_the_three_closing_verbs_scoped_to_that_tab()
    {
        var host = new TabHost();
        var first = host.OpenForTest(new FakePane("/notebook/one.md"), "/notebook/one.md");

        // With one tab open, "close the others" has nothing to close — offered, but greyed.
        var solo = host.TabMenuForTest(first);
        Assert.Equal(["Close Tab", "Close Other Tabs", "Close All Tabs"], solo.Select(i => i.Header as string));
        Assert.False(solo.Single(i => (string?)i.Header == "Close Other Tabs").IsEnabled);

        host.OpenForTest(new FakePane("/notebook/two.md"), "/notebook/two.md");
        host.OpenForTest(new FakePane("/notebook/three.md"), "/notebook/three.md");

        // Picking it from the *first* tab's menu keeps that tab, not the active one (three.md).
        var menu = host.TabMenuForTest(first);
        var closeOthers = menu.Single(i => (string?)i.Header == "Close Other Tabs");
        Assert.True(closeOthers.IsEnabled);
        closeOthers.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Yield(); // the item's handler is fire-and-forget; let the close run

        Assert.Equal(1, host.Count);
        Assert.Same(first.Pane, host.ActivePane);
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

    // ---- Keeping the active tab on screen ------------------------------------------------------------

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>A strip with more tabs than fit, in a window narrow enough that it has to scroll.</summary>
    private static (TabHost Host, Window Window) OpenCrowdedStrip()
    {
        var host = new TabHost();
        var window = new Window { Width = 420, Height = 300, Content = host };
        window.Show();

        for (var i = 0; i < 12; i++)
        {
            host.OpenForTest(new FakePane($"/notebook/note-{i:00}.md"), $"/notebook/note-{i:00}.md");
        }

        Settle(window);
        return (host, window);
    }

    private static ScrollViewer Strip(Window window) =>
        window.GetVisualDescendants().OfType<ScrollViewer>().First();

    /// <summary>Where the tab titled <paramref name="title"/> sits, in viewport coordinates (0 = left edge).</summary>
    private static (double Left, double Right) TabEdges(Window window, string title)
    {
        var strip = Strip(window);
        var label = window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == title);
        var tab = label.GetVisualAncestors().OfType<Border>().First();
        var left = tab.TranslatePoint(default, strip)!.Value.X;
        return (left, left + tab.Bounds.Width);
    }

    [AvaloniaFact]
    public void The_tab_you_open_is_scrolled_into_view_when_it_is_off_the_end_of_the_strip()
    {
        // Twelve documents open in a 420px window: the newest tab is well past the right edge, so the one
        // thing that says which document you're looking at would otherwise be the one thing you can't see.
        var (_, window) = OpenCrowdedStrip();

        var strip = Strip(window);
        Assert.True(strip.Extent.Width > strip.Viewport.Width, "the strip never overflowed — widen the test");
        Assert.True(strip.Offset.X > 0, "the strip never scrolled to the tab that was just opened");

        var (left, right) = TabEdges(window, "note-11.md");
        Assert.True(left >= -0.5 && right <= strip.Viewport.Width + 0.5, $"active tab sat at {left}…{right} of a {strip.Viewport.Width}px strip");
    }

    [AvaloniaFact]
    public void Activating_a_tab_behind_the_left_edge_scrolls_back_to_it()
    {
        // The sidebar reopening an early document — or ⌘⇧[ wrapping round to it — scrolls the other way.
        var (host, window) = OpenCrowdedStrip();

        host.Activate(host.Tabs[0]);
        Settle(window);

        var strip = Strip(window);

        // Back at the beginning of the strip: the first tab starts 4px in (the strip's margin), and the
        // shortest scroll that shows it is exactly that far — it never overshoots into empty space.
        Assert.True(strip.Offset.X <= 4, $"the strip stopped {strip.Offset.X}px short of the first tab");
        var (left, right) = TabEdges(window, "note-00.md");
        Assert.True(left >= -0.5 && right <= strip.Viewport.Width + 0.5, $"first tab sat at {left}…{right}");
    }

    [AvaloniaFact]
    public void A_tab_already_on_screen_does_not_move_the_strip()
    {
        // Re-opening the file you're already looking at (a double-click in the sidebar) must not shuffle the
        // strip under the cursor — the reveal only ever scrolls when it has to.
        var (host, window) = OpenCrowdedStrip();
        var settled = Strip(window).Offset.X;

        host.Activate(host.Tabs[^1]);
        Settle(window);

        Assert.Equal(settled, Strip(window).Offset.X);
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
