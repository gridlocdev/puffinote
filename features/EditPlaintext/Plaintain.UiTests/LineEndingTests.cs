using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Plaintain.Desktop.Services;
using Plaintain.Desktop.Views;
using Puffinote.Text.Document;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// Files with both CRLF and LF in them. Usually a merge artefact or a tool that appended in the wrong
/// convention — and sometimes exactly what someone meant, which is the whole reason this is offered and
/// never applied. Nothing here rewrites a file for looking untidy.
/// </summary>
public class LineEndingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"plaintain-endings-{Guid.NewGuid():N}");

    public LineEndingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }

        GC.SuppressFinalize(this);
    }

    private MainWindow OpenWith(string text)
    {
        var path = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(path, text);
        var window = TestShell.NewWindow(new SettingsService(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.json")));
        window.Show();
        window.OpenPath(path);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void A_mixed_file_says_so_in_the_status_bar()
    {
        var window = OpenWith("one\r\ntwo\nthree\r\n");

        Assert.True(window.EditorView.Stats.HasMixedNewlines);
        Assert.Equal("CRLF (mixed)", window.EditorView.Stats.NewlineLabel);
    }

    /// <summary>A file that uses one convention is not accused of using two.</summary>
    [AvaloniaFact]
    public void A_consistent_file_is_not_called_mixed()
    {
        Assert.Equal("LF", OpenWith("one\ntwo\nthree\n").EditorView.Stats.NewlineLabel);
        Assert.Equal("CRLF", OpenWith("one\r\ntwo\r\n").EditorView.Stats.NewlineLabel);
    }

    /// <summary>
    /// The command is the offer, and it is only offered when there is something to resolve — so the menu
    /// answers "does this file have that problem?" without anyone opening the dialog.
    /// </summary>
    [AvaloniaFact]
    public void The_normalise_command_is_available_only_for_a_mixed_file()
    {
        var mixed = OpenWith("one\r\ntwo\nthree\r\n");
        var command = mixed.EditorView.EditingCommands.Single(c => c.Id == "edit.normalizeLineEndings");
        Assert.True(command.CanExecute!());

        var consistent = OpenWith("one\ntwo\n");
        var same = consistent.EditorView.EditingCommands.Single(c => c.Id == "edit.normalizeLineEndings");
        Assert.False(same.CanExecute!());
    }

    /// <summary>Opening a mixed file changes nothing about it — the file is exactly as it was found.</summary>
    [AvaloniaFact]
    public void Opening_a_mixed_file_does_not_touch_it()
    {
        const string original = "one\r\ntwo\nthree\r\n";
        var window = OpenWith(original);

        Assert.Equal(original, window.EditorView.Text);
        Assert.False(window.EditorView.IsDirty); // no silent normalisation on the way in
    }

    [AvaloniaFact]
    public void Normalising_to_LF_rewrites_every_ending_as_one_undoable_edit()
    {
        var window = OpenWith("one\r\ntwo\nthree\r\n");

        window.EditorView.NormalizeLineEndings(NewlineStyle.Lf);

        Assert.Equal("one\ntwo\nthree\n", window.EditorView.Text);
        Assert.True(window.EditorView.IsDirty);
        Assert.False(window.EditorView.Stats.HasMixedNewlines);

        window.EditorView.Undo();
        Assert.Equal("one\r\ntwo\nthree\r\n", window.EditorView.Text); // one step back, not three
    }

    [AvaloniaFact]
    public void Normalising_to_CRLF_never_doubles_a_carriage_return()
    {
        var window = OpenWith("one\r\ntwo\nthree\r\n");

        window.EditorView.NormalizeLineEndings(NewlineStyle.CrLf);

        Assert.Equal("one\r\ntwo\r\nthree\r\n", window.EditorView.Text);
        Assert.DoesNotContain("\r\r", window.EditorView.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Normalising a file that already uses that convention does nothing at all — a command that dirtied a
    /// file it did not change would be the worst kind of "did that do anything?".
    /// </summary>
    [AvaloniaFact]
    public void Normalising_a_file_that_is_already_consistent_is_a_no_op()
    {
        var window = OpenWith("one\ntwo\n");

        window.EditorView.NormalizeLineEndings(NewlineStyle.Lf);

        Assert.False(window.EditorView.IsDirty);
        Assert.False(window.EditorView.CanUndo);
    }

    /// <summary>
    /// The counts are what tell you which convention is the accident — 900 LF and 3 CRLF is a different
    /// story from a 50/50 split, and the dialog puts both numbers in front of the person deciding.
    /// </summary>
    [AvaloniaFact]
    public void The_counts_name_the_majority_without_deciding_for_anyone()
    {
        var counts = LineEndingCounts.Of("a\nb\nc\nd\r\n");

        Assert.Equal(3, counts.Lf);
        Assert.Equal(1, counts.CrLf);
        Assert.True(counts.IsMixed);
        Assert.Equal(NewlineStyle.Lf, counts.Dominant);
    }
}
