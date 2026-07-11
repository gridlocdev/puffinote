using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Controls.Icons;
using Typage.Control.Controls;
using Typage.Control.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The Markdown formatting toolbar rides the family's shared <see cref="AdaptiveCommandBar"/> shell and
/// surfaces every formatting command (no more "some but not all") — the common ones inline, the rest behind
/// each group's dropdown.
/// </summary>
public class MarkdownCommandBarTests
{
    [AvaloniaFact]
    public void Surfaces_every_formatting_command_in_the_editors_catalog()
    {
        var view = new MarkdownEditorView();
        var bar = new MarkdownCommandBar(view.EditingCommands);

        // Every format.*/insert.* command the editor publishes must be reachable somewhere on the bar.
        var expected = view.EditingCommands
            .Select(c => c.Id)
            .Where(id => id.StartsWith("format.", StringComparison.Ordinal) || id.StartsWith("insert.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(expected);
        Assert.All(expected, id => Assert.Contains(id, bar.SurfacedIdsForTest));

        // Including the newly-added ones that the old flat bar omitted.
        Assert.Contains("format.h4", bar.SurfacedIdsForTest);
        Assert.Contains("format.h6", bar.SurfacedIdsForTest);
        Assert.Contains("format.removeTask", bar.SurfacedIdsForTest);

        // …and the ones the embeddable bar never had at all, because they used to live in the standalone
        // shell — which is why Puffinote had no way to insert a link or an image.
        Assert.Contains("format.link", bar.SurfacedIdsForTest);
        Assert.Contains("insert.image", bar.SurfacedIdsForTest);
    }

    [AvaloniaFact]
    public void Blockquote_is_an_insert_not_a_list()
    {
        // It shares nothing with a list but the fact that it prefixes lines: no items, no nesting, no
        // renumbering. It sat under Lists, between "Numbered List" and "Remove Checkbox".
        var bar = new MarkdownCommandBar(new MarkdownEditorView().EditingCommands);
        Assert.Equal("Insert", bar.SectionOfForTest("format.quote"));
        Assert.Equal("Lists", bar.SectionOfForTest("format.bulletList"));
        Assert.Equal("Insert", bar.SectionOfForTest("format.link"));
    }

    [AvaloniaFact] // LucideIcons.Get resolves an avares:// resource, so it needs the Avalonia app (no window needed).
    public void Every_command_icon_resolves_to_a_vendored_glyph()
    {
        // An unvendored (or misspelled) key doesn't throw — it draws nothing, leaving a blank slot in the
        // dropdown. That silence is what let Code Block and Horizontal Rule ship icon-less, so pin it.
        foreach (var command in new MarkdownEditorView().EditingCommands)
        {
            if (command.Icon is null)
            {
                continue;
            }

            Assert.True(LucideIcons.Get(command.Icon) is not null, $"{command.Id} references missing icon '{command.Icon}'");
        }
    }

    [AvaloniaFact]
    public void Every_surfaced_command_has_an_icon()
    {
        // A command with no icon still renders — as a blank 15px gutter beside its label — so the bar goes
        // quietly ragged rather than failing. Everything the bar shows must carry a glyph.
        var view = new MarkdownEditorView();
        var bar = new MarkdownCommandBar(view.EditingCommands);
        foreach (var command in view.EditingCommands.Where(c => bar.SurfacedIdsForTest.Contains(c.Id)))
        {
            Assert.False(string.IsNullOrEmpty(command.Icon), $"{command.Id} is surfaced on the bar with no icon");
        }
    }

    [AvaloniaFact]
    public void EachHeadingLevel_HasItsOwnNumberedGlyph()
    {
        // Not one shared "type" over three blanks: the level is the whole point of the picker.
        var byId = new MarkdownEditorView().EditingCommands.ToDictionary(c => c.Id);
        for (var level = 1; level <= 6; level++)
        {
            Assert.Equal($"heading-{level}", byId[$"format.h{level}"].Icon);
        }
    }

    [AvaloniaFact]
    public void Rides_the_shared_adaptive_shell_and_folds_when_narrow()
    {
        var bar = new MarkdownCommandBar(new MarkdownEditorView().EditingCommands);
        var host = new Window { Content = bar, Width = 900, Height = 120 };
        host.Show();
        Pump();

        var shell = bar.GetVisualDescendants().OfType<AdaptiveCommandBar>().Single();
        Assert.False(shell.IsOverflowing); // all four groups fit wide
        Assert.Equal(4, shell.InlineGroupCount);

        host.Width = 430; // squeeze it — the rightmost groups fold into the overflow drawer, none are lost
        Pump();
        Assert.True(shell.IsOverflowing);
        Assert.True(shell.InlineGroupCount < 4);
        Assert.Equal(4, shell.InlineGroupCount + shell.OverflowGroupCount);

        host.Width = 900; // widen again — they come back inline, drawer empties
        Pump();
        Assert.False(shell.IsOverflowing);
        Assert.Equal(4, shell.InlineGroupCount);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }
}
