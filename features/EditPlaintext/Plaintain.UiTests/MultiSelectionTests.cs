using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Plaintain.Control.Controls;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// ⌘D, as VS Code spells it: with nothing selected it takes the word under the caret, and each press
/// after that adds the next occurrence of what is selected. A rename across a file becomes one gesture
/// repeated, rather than a Find-and-Replace you have to get right in one go and cannot see the effect of
/// until it is done.
/// </summary>
public class MultiSelectionTests
{
    private static PlainTextEditor Open(string text)
    {
        var editor = new PlainTextEditor { Text = text };
        var window = new Window { Width = 600, Height = 400, Content = editor };
        window.Show();
        editor.Focus();
        Dispatcher.UIThread.RunJobs();
        return editor;
    }

    private static void Press(PlainTextEditor editor, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        editor.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = editor,
        });
        Dispatcher.UIThread.RunJobs();
    }

    private static void Type(PlainTextEditor editor, string text)
    {
        editor.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Text = text,
            Source = editor,
        });
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The first press has no selection to work from, so it makes one — that is what gives the
    /// second press something to mean.</summary>
    [AvaloniaFact]
    public void The_first_press_selects_the_word_under_the_caret()
    {
        var editor = Open("alpha beta alpha");
        editor.MoveCaret(2, extend: false); // inside "alpha"

        editor.AddNextOccurrence();

        Assert.Equal(0, editor.Buffer.Selection.Start);
        Assert.Equal(5, editor.Buffer.Selection.End);
        Assert.False(editor.HasMultipleSelections); // one selection, not yet several
    }

    /// <summary>A caret that has just finished typing a word sits past its last letter, and belongs to it.</summary>
    [AvaloniaFact]
    public void A_caret_just_past_a_word_still_takes_that_word()
    {
        var editor = Open("alpha beta");
        editor.MoveCaret(5, extend: false); // immediately after "alpha"

        editor.AddNextOccurrence();

        Assert.Equal(0, editor.Buffer.Selection.Start);
        Assert.Equal(5, editor.Buffer.Selection.End);
    }

    [AvaloniaFact]
    public void The_second_press_adds_the_next_occurrence()
    {
        var editor = Open("alpha beta alpha");
        editor.MoveCaret(2, extend: false);

        editor.AddNextOccurrence(); // selects the first "alpha"
        editor.AddNextOccurrence(); // adds the second

        Assert.True(editor.HasMultipleSelections);
        Assert.Equal([(0, 5), (11, 16)], editor.AllSelections().Select(s => (s.Start, s.End)));
        Assert.Equal(11, editor.Buffer.Selection.Start); // the newest is primary, so the view follows it
    }

    /// <summary>
    /// The search wraps, so the gesture keeps working when you started in the middle of the file — going
    /// quiet with occurrences still unselected above the caret would look like a bug.
    /// </summary>
    [AvaloniaFact]
    public void The_search_wraps_past_the_end_of_the_document()
    {
        var editor = Open("alpha beta alpha");
        editor.MoveCaret(13, extend: false); // inside the *second* alpha

        editor.AddNextOccurrence();
        editor.AddNextOccurrence();

        Assert.Equal([(0, 5), (11, 16)], editor.AllSelections().Select(s => (s.Start, s.End)));
    }

    /// <summary>Once everything is selected the gesture has finished its job and stops adding.</summary>
    [AvaloniaFact]
    public void Pressing_past_the_last_occurrence_adds_nothing()
    {
        var editor = Open("alpha beta alpha");
        editor.MoveCaret(2, extend: false);
        editor.AddNextOccurrence();
        editor.AddNextOccurrence();

        editor.AddNextOccurrence(); // no third "alpha"

        Assert.Equal(2, editor.AllSelections().Count);
    }

    /// <summary>The point of the whole feature: typing reaches every selection at once.</summary>
    [AvaloniaFact]
    public void Typing_replaces_every_selection()
    {
        var editor = Open("alpha beta alpha");
        editor.MoveCaret(2, extend: false);
        editor.AddNextOccurrence();
        editor.AddNextOccurrence();

        Type(editor, "x");

        Assert.Equal("x beta x", editor.Text);
        Assert.True(editor.HasMultipleSelections); // and the carets stay, so you can keep typing
        Assert.Equal([1, 8], editor.AllSelections().Select(s => s.Caret));

        Type(editor, "y");
        Assert.Equal("xy beta xy", editor.Text);
    }

    /// <summary>
    /// Five selections is one thing the user did once, so ⌘Z reverses it once. An undo that gave the word
    /// back one occurrence at a time would be five presses to get out of one.
    /// </summary>
    [AvaloniaFact]
    public void Typing_across_selections_is_a_single_undo()
    {
        var editor = Open("alpha beta alpha");
        editor.MoveCaret(2, extend: false);
        editor.AddNextOccurrence();
        editor.AddNextOccurrence();
        Type(editor, "x");

        editor.Undo();

        Assert.Equal("alpha beta alpha", editor.Text);
    }

    [AvaloniaFact]
    public void Backspace_deletes_at_every_caret()
    {
        var editor = Open("alpha beta alpha");
        editor.MoveCaret(2, extend: false);
        editor.AddNextOccurrence();
        editor.AddNextOccurrence();
        Type(editor, "xy"); // "xy beta xy", carets after each

        Press(editor, Key.Back);

        Assert.Equal("x beta x", editor.Text);
    }

    [AvaloniaFact]
    public void Delete_removes_forward_at_every_caret()
    {
        var editor = Open("alpha beta alpha!");
        editor.MoveCaret(2, extend: false);
        editor.AddNextOccurrence();
        editor.AddNextOccurrence();

        // Backspace over a selection removes the selection, not a character before it — at every one of
        // them. That leaves " beta !", with a caret where each word was: at 0 and at 6.
        Press(editor, Key.Back);
        Assert.Equal(" beta !", editor.Text);

        Press(editor, Key.Delete);

        // The leading space and the trailing "!" — one character forward from each caret.
        Assert.Equal("beta ", editor.Text);
    }

    /// <summary>
    /// Escape unwinds one layer at a time. Taking the extras and the primary together would throw away a
    /// multi-selection someone was still building.
    /// </summary>
    [AvaloniaFact]
    public void Escape_drops_the_extra_selections_before_the_primary_one()
    {
        var editor = Open("alpha beta alpha");
        editor.MoveCaret(2, extend: false);
        editor.AddNextOccurrence();
        editor.AddNextOccurrence();

        Press(editor, Key.Escape);
        Assert.False(editor.HasMultipleSelections);
        Assert.False(editor.Buffer.Selection.IsEmpty); // the primary selection is still there

        Press(editor, Key.Escape);
        Assert.True(editor.Buffer.Selection.IsEmpty);
    }

    /// <summary>
    /// Going somewhere else ends the gesture: the extra carets belonged to the occurrences ⌘D found, and
    /// once the caret has moved they are no longer about anything.
    /// </summary>
    [AvaloniaFact]
    public void Moving_the_caret_ends_the_multi_selection()
    {
        var editor = Open("alpha beta alpha");
        editor.MoveCaret(2, extend: false);
        editor.AddNextOccurrence();
        editor.AddNextOccurrence();

        Press(editor, Key.Right);

        Assert.False(editor.HasMultipleSelections);
    }

    /// <summary>An editor with one selection behaves exactly as it did — the extras are the new part.</summary>
    [AvaloniaFact]
    public void One_selection_still_types_the_ordinary_way()
    {
        var editor = Open("alpha");
        editor.MoveCaret(0, extend: false);

        Type(editor, "x");

        Assert.Equal("xalpha", editor.Text);
        Assert.False(editor.HasMultipleSelections);
    }

    /// <summary>The chord is bound in the catalog, so the menu, the key binding and a host palette agree.</summary>
    [AvaloniaFact]
    public void The_command_is_in_the_catalog_under_the_chord_VS_Code_uses()
    {
        var view = new Plaintain.Control.Views.PlainTextEditorView();
        var command = view.EditingCommands.Single(c => c.Id == "edit.addNextOccurrence");

        Assert.Equal(Key.D, command.Shortcut!.Value.Key);
    }
}
