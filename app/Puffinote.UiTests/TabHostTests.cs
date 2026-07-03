using Avalonia.Headless.XUnit;
using Cellestra.Desktop.Services;
using Puffinote.Desktop.Panes;
using Typage.Control.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>Phase 2B: N-document tab lifecycle — open/focus/close, mixing notes and sheets, dirty prompts.</summary>
public sealed class TabHostTests
{
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
}
