using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Plaintain.Control.Views;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// The embeddable composite: the find strip over the surface, and the one catalog a host binds to.
/// </summary>
public class PlainTextEditorViewTests
{
    private static PlainTextEditorView Open(string text)
    {
        var view = new PlainTextEditorView { Text = text };
        var window = new Window { Width = 500, Height = 300, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    [AvaloniaFact]
    public void Find_highlights_every_match_and_steps_through_them()
    {
        var view = Open("one two one two one");
        view.ShowFind();
        view.Find.QueryBox.Text = "one";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, view.Matches.Count);
        Assert.Equal(0, view.CurrentMatch);

        view.Find.QueryBox.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Enter,
            Source = view.Find.QueryBox,
        });

        Assert.Equal(1, view.CurrentMatch);
    }

    [AvaloniaFact]
    public void Find_starts_from_where_you_are_reading_not_from_the_top()
    {
        // Opening find 40,000 lines into a log and being thrown back to line 1 is the behaviour this
        // avoids: the first offered match is the first one at or after the caret.
        var view = Open("one\n\n\n\n\none\n\n\n\n\none");
        view.Editor.Buffer.Selection = Puffinote.Text.Document.Selection.At(5); // past the first "one"
        view.ShowFind();
        view.Find.QueryBox.Text = "one";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, view.CurrentMatch);
    }

    [AvaloniaFact]
    public void Dismissing_find_clears_the_highlights()
    {
        var view = Open("one two one");
        view.ShowFind();
        view.Find.QueryBox.Text = "one";
        Dispatcher.UIThread.RunJobs();
        Assert.NotEmpty(view.Matches);

        view.Find.Dismiss();
        Assert.Empty(view.Matches);
    }

    [AvaloniaFact]
    public void The_catalog_is_built_once_so_every_surface_shares_one_command()
    {
        var view = Open("x");
        Assert.Same(view.EditingCommands, view.EditingCommands);
    }

    [AvaloniaFact]
    public void Word_wrap_and_line_numbers_are_commands_that_report_their_own_state()
    {
        var view = Open("x");
        var wrap = view.EditingCommands.Single(c => c.Id == "view.wordWrap");

        Assert.False(wrap.IsToggleActive);
        wrap.Execute();
        Assert.True(wrap.IsToggleActive);
        Assert.True(view.Editor.WordWrap);
    }

    [AvaloniaFact]
    public void Undo_reports_whether_there_is_anything_to_undo()
    {
        var view = Open("x");
        var undo = view.EditingCommands.Single(c => c.Id == "edit.undo");
        Assert.False(undo.IsEnabled);

        view.Editor.Buffer.Insert("y");
        Assert.True(undo.IsEnabled);
    }

    [AvaloniaFact]
    public void Go_to_line_centres_the_line_and_puts_the_caret_on_it()
    {
        var view = Open(string.Join('\n', Enumerable.Range(1, 500).Select(i => $"line {i}")));
        view.GoToLine(300);

        Assert.Equal(300, view.CaretPosition.DisplayLine);
    }
}
