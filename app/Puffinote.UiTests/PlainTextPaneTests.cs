using System.Text;
using Avalonia.Headless.XUnit;
using Puffinote.Desktop.Panes;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The third pane type, and the routing that makes Puffinote genuinely "opens any text file": the
/// specific editors are asked first, and Plaintain is a fallback with a decode probe rather than another
/// whitelist nobody can finish writing.
/// </summary>
public sealed class PlainTextPaneTests : IDisposable
{
    private readonly DirectoryInfo _scratch = Directory.CreateTempSubdirectory("puffinote-plaintext");

    public void Dispose() => _scratch.Delete(recursive: true);

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_scratch.FullName, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string Write(string name, string text) => Write(name, Encoding.UTF8.GetBytes(text));

    [AvaloniaFact]
    public void The_specific_editors_are_asked_first()
    {
        // Markdown and a spreadsheet are both "text" to a decoder, and neither belongs in a text pane.
        Assert.IsType<MarkdownPane>(EditorPaneFactory.Create(Write("note.md", "# hi")));
        Assert.IsType<SheetPane>(EditorPaneFactory.Create(Write("book.ods", "not really an ods")));
    }

    [AvaloniaFact]
    public void A_known_text_extension_routes_without_reading_the_file()
    {
        // These need no probe — and the path need not even exist, which is what "routes fast" means.
        Assert.IsType<PlainTextPane>(EditorPaneFactory.Create("/nowhere/server.log"));
        Assert.IsType<PlainTextPane>(EditorPaneFactory.Create("/nowhere/app.conf"));
        Assert.IsType<PlainTextPane>(EditorPaneFactory.Create("/nowhere/settings.YAML"));
    }

    [AvaloniaFact]
    public void An_unknown_extension_opens_when_its_bytes_look_like_text()
    {
        // The point of the fallback: nobody listed ".frobnicate", and it opens anyway.
        Assert.IsType<PlainTextPane>(EditorPaneFactory.Create(Write("weird.frobnicate", "plain words")));
        Assert.IsType<PlainTextPane>(EditorPaneFactory.Create(Write("LICENSE", "MIT License\n")));
    }

    [AvaloniaFact]
    public void Bytes_no_text_file_has_are_declined()
    {
        // A NUL is the giveaway. This is a real PNG header.
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
        string path = Write("image.dat", png);

        Assert.Null(EditorPaneFactory.Create(path));
        Assert.False(EditorPaneFactory.CanOpen(path));
    }

    [AvaloniaFact]
    public void An_extension_that_declares_a_binary_format_is_believed_over_its_bytes()
    {
        // A file named .png is an image whatever its first 8 KB happen to contain — a truncated or
        // placeholder one must not open in a text editor just because it has no NUL yet.
        string path = Write("photo.png", "not really a png");

        Assert.Null(EditorPaneFactory.Create(path));
        Assert.False(EditorPaneFactory.CanOpen(path));
    }

    [AvaloniaFact]
    public void An_empty_file_is_a_perfectly_good_empty_text_file()
    {
        Assert.IsType<PlainTextPane>(EditorPaneFactory.Create(Write("empty.dat", string.Empty)));
    }

    [AvaloniaFact]
    public async Task Loading_and_saving_round_trips_the_bytes()
    {
        string path = Write("notes.txt", "alpha\r\nbeta\r\n");
        using var pane = EditorPaneFactory.Create(path)!;
        await pane.LoadAsync(path);

        Assert.Equal("notes.txt", pane.Title);
        Assert.False(pane.IsDirty);

        await pane.SaveAsync();
        Assert.Equal("alpha\r\nbeta\r\n", File.ReadAllText(path)); // CRLF survives a save that changed nothing
    }

    [AvaloniaFact]
    public async Task A_file_whose_encoding_was_guessed_is_read_but_never_written_back()
    {
        // The pane refuses rather than asking: a notebook tab has no good place for an encoding prompt,
        // and a file you can read but not write beats one silently rewritten through a guess.
        byte[] latin1 = Encoding.Latin1.GetBytes("café\n");
        string path = Write("legacy.txt", latin1);

        using var pane = EditorPaneFactory.Create(path)!;
        await pane.LoadAsync(path);
        pane.View.Focus();

        var view = Assert.IsType<Plaintain.Control.Views.PlainTextEditorView>(pane.View);
        Assert.Equal("café\n", view.Text);
        Assert.True(view.Editor.IsReadOnlyEncoding);

        view.Editor.Buffer.Insert("x");
        await pane.SaveAsync();
        Assert.Equal(latin1, File.ReadAllBytes(path)); // untouched
    }

    [AvaloniaFact]
    public async Task Plaintext_contributes_nothing_to_the_outline_and_that_is_deliberate()
    {
        // Proposal 0003 §9.4: indentation trees and comment banners produce confident-looking noise on
        // prose and logs. A blank panel is the honest answer for a file with no structure.
        string path = Write("server.log", "  indented\n    more\n# banner\n");
        using var pane = EditorPaneFactory.Create(path)!;
        await pane.LoadAsync(path);

        Assert.Empty(pane.GetOutline());
    }

    [AvaloniaFact]
    public void The_pane_publishes_Plaintain_s_own_catalog()
    {
        using var pane = EditorPaneFactory.Create("/nowhere/x.txt")!;

        Assert.Contains(pane.Commands, c => c.Id == "view.wordWrap");
        Assert.Contains(pane.Commands, c => c.Id == "edit.find");
    }
}
