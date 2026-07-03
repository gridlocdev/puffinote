using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Puffinote.Desktop.Search;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Sidebar;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>Phase 4C: the notebook-wide search panel (PRD §7.2.3).</summary>
public sealed class SearchTests
{
    [AvaloniaFact]
    public void Search_panel_is_registered_in_the_sidebar()
    {
        var sidebar = new SidebarView();
        var window = new Window { Content = sidebar };
        window.Show();

        sidebar.Activate("search");
        Assert.Equal("search", sidebar.ActivePanelId);
    }

    [AvaloniaFact]
    public async Task Typing_shows_results_and_Enter_opens_the_selected_hit()
    {
        var panel = new SearchPanel();
        var hit = new SearchHit("/v/a.md", "a.md", 1, "…a match…");
        panel.SearchFunc = q => q == "foo" ? new[] { hit } : Array.Empty<SearchHit>();
        string? opened = null;
        panel.FileActivated += path => opened = path;

        var window = new Window { Content = panel.View };
        window.Show();
        panel.Focus(); // focus the search box

        window.KeyTextInput("foo"); // type the query
        await panel.RunSearchNowForTest(); // flush the debounce + off-thread scan deterministically
        Assert.Single(panel.ResultsForTest);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); // Enter opens the top hit
        Assert.Equal("/v/a.md", opened);
    }

    [AvaloniaFact]
    public async Task The_shell_indexes_the_notebook_and_a_hit_can_be_opened()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-search-e2e");
        try
        {
            var note = Path.Combine(dir.FullName, "note.md");
            File.WriteAllText(note, "alpha beta gamma");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);

            await window.SearchForTest!.RebuildAsync(); // deterministic rebuild (open kicked one off too)
            var hits = window.SearchForTest.Search("beta");
            Assert.Single(hits);

            await window.OpenFileAsync(hits[0].Path);
            Assert.Contains("note.md", window.Title);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
