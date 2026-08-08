using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Plaintain.Desktop.Services;
using Plaintain.Desktop.Views;
using Puffinote.Components.Shell;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// Document auto-save: the open file is written back once the typing stops.
/// <para>
/// Every test drives <c>AutoSaveNowForTest</c> rather than waiting — a <c>DispatcherTimer</c> doesn't tick
/// in a headless suite that isn't pumping real time, and a test that waited out thirty seconds for its
/// assertion is a test nobody runs.
/// </para>
/// </summary>
public class AutoSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"plaintain-autosave-{Guid.NewGuid():N}");

    public AutoSaveTests() => Directory.CreateDirectory(_dir);

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

    private MainWindow Open(int? autoSaveSeconds = null)
    {
        var settings = new SettingsService(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.json"));
        if (autoSaveSeconds is { } seconds)
        {
            settings.Settings.AutoSaveSeconds = seconds;
        }

        return new MainWindow(settings);
    }

    /// <summary>Replace the buffer's whole text <i>as an edit</i>, so the document comes out dirty —
    /// <c>LoadText</c> marks it saved, which is the state these tests are trying to get out of.</summary>
    private static void SetText(MainWindow window, string text)
    {
        var buffer = window.EditorView.Editor.Buffer;
        buffer.ReplaceRange(0, buffer.Document.Length, text);
        Dispatcher.UIThread.RunJobs();
    }

    private string WriteFile(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [AvaloniaFact]
    public void An_edited_document_is_written_back_without_anyone_pressing_Save()
    {
        var path = WriteFile("notes.txt", "original\n");
        var window = Open();
        window.Show();
        window.OpenPath(path);

        SetText(window, "edited by hand");
        Assert.True(window.EditorView.IsDirty);

        window.AutoSaveNowForTest().GetAwaiter().GetResult();

        Assert.Equal("edited by hand", File.ReadAllText(path));
        Assert.False(window.EditorView.IsDirty);
    }

    [AvaloniaFact]
    public void Typing_arms_the_countdown_and_saving_disarms_it()
    {
        var path = WriteFile("armed.txt", "x\n");
        var window = Open();
        window.Show();
        window.OpenPath(path);
        Assert.False(window.AutoSavePendingForTest); // opening a file is not an edit

        window.EditorView.Editor.Buffer.ReplaceRange(0, 0, "more ");
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.AutoSavePendingForTest);

        window.AutoSaveNowForTest().GetAwaiter().GetResult();
        Assert.False(window.AutoSavePendingForTest); // saved: nothing left to write
    }

    [AvaloniaFact]
    public void An_untitled_buffer_is_left_alone_rather_than_asked_where_to_go()
    {
        // There is nowhere to write it, and Save As is a question an auto-save must never ask.
        var window = Open();
        window.Show();
        SetText(window, "a draft with no home");

        window.AutoSaveNowForTest().GetAwaiter().GetResult();

        Assert.True(window.EditorView.IsDirty); // still unsaved, and no dialog opened
    }

    [AvaloniaFact]
    public void A_file_whose_encoding_was_only_guessed_is_never_written_back_automatically()
    {
        // 0xE9 is not valid UTF-8, so the file opens through the Latin-1 fallback and every save is held
        // until a person confirms the guess. Auto-save must not be the thing that confirms it — this is
        // the case Plaintain exists to get right.
        var path = Path.Combine(_dir, "latin1.log");
        File.WriteAllBytes(path, [0x63, 0x61, 0x66, 0xE9, 0x0A]); // "café" in Latin-1

        // Left unshown on purpose: opening such a file puts up a modal notice that nothing headless can
        // answer. What is under test is the encoding guard, not the notice in front of it.
        var window = Open();
        window.OpenPath(path);
        Assert.True(window.EncodingUnconfirmedForTest);

        SetText(window, "rewritten");
        window.AutoSaveNowForTest().GetAwaiter().GetResult();

        Assert.Equal<byte[]>([0x63, 0x61, 0x66, 0xE9, 0x0A], File.ReadAllBytes(path));
        Assert.True(window.EditorView.IsDirty);
    }

    [AvaloniaFact]
    public void Turning_the_interval_off_stops_it_on_the_open_document()
    {
        var path = WriteFile("off.txt", "keep me\n");
        var window = Open(autoSaveSeconds: 0);
        window.Show();
        window.OpenPath(path);

        SetText(window, "should not reach disk");
        Assert.False(window.AutoSavePendingForTest);

        window.AutoSaveNowForTest().GetAwaiter().GetResult();
        Assert.Equal("keep me\n", File.ReadAllText(path));
    }

    [Fact]
    public void The_interval_defaults_to_the_family_default_and_survives_a_round_trip()
    {
        var path = Path.Combine(_dir, "settings.json");
        Assert.Equal(AutoSave.DefaultSeconds, new SettingsService(path).Settings.AutoSaveSeconds);

        var service = new SettingsService(path);
        service.Settings.AutoSaveSeconds = 120;
        service.Save();

        Assert.Equal(120, new SettingsService(path).Settings.AutoSaveSeconds);
    }
}
