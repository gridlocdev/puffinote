using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Plaintain.Desktop.Services;
using Plaintain.Desktop.Views;
using Puffinote.Text.Document;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// Converting a file's encoding — the action that actually rescues a Latin-1 file, as opposed to
/// confirming the guess, which only agrees to write it back unchanged.
/// <para>
/// It is explicit and always will be. The file contract (PRD §4) is that bytes are preserved exactly, so
/// an editor that quietly upgraded files to UTF-8 on save would be rewriting documents nobody asked it to
/// touch. Every test here goes through the command; none of them shows conversion happening on its own.
/// </para>
/// </summary>
public class EncodingConversionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"plaintain-encoding-{Guid.NewGuid():N}");

    public EncodingConversionTests() => Directory.CreateDirectory(_dir);

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

    private MainWindow NewWindow() =>
        TestShell.NewWindow(new SettingsService(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.json")));

    /// <summary>A file of Latin-1 bytes — not valid UTF-8, so Plaintain opens it through the fallback.</summary>
    private string WriteLatin1(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(text));
        return path;
    }

    private MainWindow Open(string path)
    {
        var window = NewWindow();
        window.Show();
        window.OpenPath(path);

        // Opening a file whose encoding was guessed raises the "this isn't valid UTF-8" notice, and a
        // headless run has nobody to click it away — a modal left standing takes the whole run down.
        window.DismissDialogForTest();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>The case this exists for: a Latin-1 file becomes a UTF-8 file, on purpose, in one step.</summary>
    [AvaloniaFact]
    public void Converting_a_latin1_file_writes_it_as_utf8()
    {
        var path = WriteLatin1("café.txt", "café ‑ notes\n"); // 0xE9 — one byte in Latin-1, invalid UTF-8
        var window = Open(path);
        Assert.Equal("Latin-1", window.EncodingNameForTest);
        Assert.True(window.EncodingUnconfirmedForTest);
        string onScreen = window.EditorView.Text;

        window.ConvertEncodingForTest(TextFileFormat.Utf8NoBom);

        // The bytes changed; the characters did not.
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(onScreen, Encoding.UTF8.GetString(bytes));
        Assert.Equal("UTF-8", window.EncodingNameForTest);
    }

    /// <summary>
    /// Choosing an encoding by hand is a declaration, so it settles the unconfirmed-guess state and lifts
    /// the read-only hold: the file is no longer being written through something nobody vouched for.
    /// </summary>
    [AvaloniaFact]
    public void Converting_settles_the_guessed_encoding()
    {
        var window = Open(WriteLatin1("notes.txt", "café\n"));
        Assert.True(window.EditorView.Editor.IsReadOnlyEncoding);

        window.ConvertEncodingForTest(TextFileFormat.Utf8NoBom);

        Assert.False(window.EncodingUnconfirmedForTest);
        Assert.False(window.EditorView.Editor.IsReadOnlyEncoding);
    }

    /// <summary>A BOM asked for is a BOM written — that is the whole difference between the two UTF-8 rows.</summary>
    [AvaloniaFact]
    public void Converting_to_utf8_with_a_bom_writes_the_bom()
    {
        var path = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(path, "plain\n");
        var window = Open(path);

        window.ConvertEncodingForTest(TextFileFormat.Utf8Bom);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.Equal("UTF-8 BOM", window.EncodingNameForTest);

        // And it round-trips: reopening reads the BOM back as a declaration, not as text.
        var reopened = Open(path);
        Assert.Equal("plain\n", reopened.EditorView.Text);
        Assert.Equal("UTF-8 BOM", reopened.EncodingNameForTest);
    }

    /// <summary>Converting an untitled buffer sets what the first save will write, and writes nothing yet.</summary>
    [AvaloniaFact]
    public void Converting_an_untitled_buffer_only_sets_what_the_next_save_writes()
    {
        var window = NewWindow();
        window.Show();

        window.ConvertEncodingForTest(TextFileFormat.Utf16Le);

        Assert.Equal("UTF-16 LE", window.EncodingNameForTest);
        Assert.Null(window.CurrentPathForTest);
        Assert.Empty(Directory.GetFiles(_dir, "*.txt"));
    }

    /// <summary>
    /// Nothing converts on its own. Opening, editing and saving a Latin-1 file — once its encoding is
    /// confirmed — writes Latin-1 back, because that is what the file is.
    /// </summary>
    [AvaloniaFact]
    public void An_ordinary_save_never_changes_the_encoding()
    {
        var path = WriteLatin1("notes.txt", "café\n");
        var window = Open(path);

        window.SaveForTest(path); // the plain write path, as auto-save and ⌘S reach it

        Assert.Equal("Latin-1", window.EncodingNameForTest);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(0xE9, bytes[3]); // still one Latin-1 byte, not the two UTF-8 would use
    }

    /// <summary>The list is the five the codec can write, in the order someone would look for them.</summary>
    [AvaloniaFact]
    public void The_conversion_offers_every_encoding_the_codec_can_write()
    {
        Assert.Equal(
            ["UTF-8", "UTF-8 BOM", "UTF-16 LE", "UTF-16 BE", "Latin-1"],
            MainWindow.ConvertibleEncodingNamesForTest);
    }
}
