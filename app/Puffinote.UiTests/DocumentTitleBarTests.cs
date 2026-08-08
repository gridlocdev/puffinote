using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Puffinote.Components.Shell;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The family's unified title bar. Every app in the repo mounts this one control, so the guarantee it has
/// to keep is a single one: <b>the document's name is centred in the window</b> — not in whatever space is
/// left over beside the traffic lights, and not pushed off-centre by the dot or the "Saved" badge.
/// </summary>
public sealed class DocumentTitleBarTests
{
    private const double WindowWidth = 800;

    /// <summary>Mount the bar in a realised window so the row actually measures and arranges.</summary>
    private static DocumentTitleBar Mounted(string name, bool dirty = false, string? badge = null)
    {
        var bar = new DocumentTitleBar { DocumentName = name, IsDirty = dirty, Badge = badge };
        var window = new Window { Width = WindowWidth, Height = 300, Content = bar };
        window.Show();
        window.UpdateLayout();
        return bar;
    }

    private static double CentreOf(Avalonia.Controls.Control control) =>
        control.Bounds.Left + (control.Bounds.Width / 2);

    [AvaloniaFact]
    public void The_document_name_is_centred_in_the_bar()
    {
        var bar = Mounted("Budget.ods");

        // The whole point of the item: the name sits on the bar's centre line, not at its left edge.
        Assert.InRange(Math.Abs(CentreOf(bar.RowForTest) - (bar.Bounds.Width / 2)), 0, 1);
    }

    [AvaloniaFact]
    public void The_edited_dot_does_not_shove_the_name_off_centre()
    {
        // The dot is part of the centred row, so the row's centre — not the name's left edge — is what
        // holds still. A dirty document must not make the title visibly jump sideways from a clean one.
        double clean = CentreOf(Mounted("Budget.ods").RowForTest);
        double dirty = CentreOf(Mounted("Budget.ods", dirty: true).RowForTest);

        Assert.InRange(Math.Abs(clean - (WindowWidth / 2)), 0, 1);
        Assert.InRange(Math.Abs(dirty - (WindowWidth / 2)), 0, 1);
    }

    [AvaloniaFact]
    public void A_long_name_is_trimmed_rather_than_run_under_the_traffic_lights()
    {
        var bar = Mounted(new string('W', 400) + ".md");

        // A horizontal StackPanel arranges each child at its *desired* width, so without the measure-time
        // cap the text block keeps its full natural width and paints out past both ends of the window —
        // under the close/minimise/zoom buttons on macOS. The row's own Bounds are clamped by the layout
        // and so say nothing about this; the text block's are what overflow.
        double inset = bar.Inset;
        double allowed = bar.Bounds.Width - (2 * inset);
        double actual = bar.NameBlockForTest.Bounds.Width;
        Assert.True(actual <= allowed + 0.5, $"the name occupies {actual:0.#}px of the {allowed:0.#}px left between the {inset:0.#}px reserves");
    }

    [AvaloniaFact]
    public void Space_is_reserved_at_both_ends_so_the_name_stays_window_centred()
    {
        var row = Mounted("Budget.ods").RowForTest;

        // Reserving only the left (where the traffic lights actually are) would centre the name in the
        // remaining space and push it visibly right of the window's centre. Both ends, or neither.
        Assert.Equal(row.Margin.Left, row.Margin.Right);
    }

    [AvaloniaFact]
    public void The_dot_shows_only_while_there_are_unsaved_changes()
    {
        Assert.False(Mounted("Budget.ods").ContentForTest.Dot);
        Assert.True(Mounted("Budget.ods", dirty: true).ContentForTest.Dot);
    }

    [AvaloniaFact]
    public void A_badge_shows_when_the_document_is_clean_and_the_dot_wins_when_it_is_not()
    {
        Assert.Equal("— Saved", Mounted("Budget.ods", badge: "Saved").ContentForTest.Badge);

        // A dot and a "Saved" say opposite things: by the time there are unsaved changes again, the save
        // the badge refers to is out of date. Cellestra's title string already followed this rule.
        var dirty = Mounted("Budget.ods", dirty: true, badge: "Saved").ContentForTest;
        Assert.Null(dirty.Badge);
        Assert.True(dirty.Dot);
    }

    [AvaloniaFact]
    public void A_document_with_no_name_reads_as_Untitled_rather_than_an_empty_bar()
    {
        Assert.Equal("Untitled", Mounted(string.Empty).ContentForTest.Name);
    }

    [AvaloniaFact]
    public async Task The_shell_title_bar_names_the_active_tab_not_the_app()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-titlebar-ui");
        try
        {
            var note = Path.Combine(dir.FullName, "notes.md");
            File.WriteAllText(note, "# hello");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);

            // With no tab open there is no document to name, so the notebook stands in for one.
            Assert.Equal(dir.Name, window.TitleBarForTest.DocumentName);

            await window.OpenFileForTest(note);

            // Window.Title still carries the app name (it is what the app switcher shows); the bar does not.
            Assert.Equal("notes.md", window.TitleBarForTest.DocumentName);
            Assert.Contains("Puffinote", window.Title, StringComparison.Ordinal);
            Assert.DoesNotContain("Puffinote", window.TitleBarForTest.DocumentName, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
