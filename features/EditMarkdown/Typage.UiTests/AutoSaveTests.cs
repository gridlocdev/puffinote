using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Puffinote.Components.Shell;
using Typage.Desktop.Services;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Document auto-save: the open file is written back once the typing stops. Distinct from the
/// crash-recovery snapshots in <see cref="RecoveryTests"/> — those write a sidecar for a buffer that may
/// never have been saved; this writes the document itself.
/// <para>
/// Every test drives <c>AutoSaveNowForTest</c> rather than waiting: a <c>DispatcherTimer</c> doesn't tick
/// in a headless suite that isn't pumping real time, and a test that waited thirty seconds for its
/// assertion would be a test nobody runs.
/// </para>
/// </summary>
public class AutoSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"typage-autosave-{Guid.NewGuid():N}");

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

    /// <summary>Replace the buffer's whole text <i>as an edit</i>, so the document comes out dirty —
    /// <c>LoadText</c> would mark it saved, which is the state these tests are trying to get out of.</summary>
    private static void SetText(Typage.Desktop.Views.MainWindow window, string text)
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
        var path = WriteFile("note.md", "original\n");
        var window = TestShell.NewWindow();
        window.Show();
        window.OpenPath(path);

        SetText(window, "edited by hand");
        Assert.True(window.EditorView.IsDirty);

        window.AutoSaveNowForTest().GetAwaiter().GetResult();

        Assert.Equal("edited by hand", File.ReadAllText(path));
        Assert.False(window.EditorView.IsDirty);
    }

    [AvaloniaFact]
    public void Typing_arms_the_countdown_and_a_manual_save_disarms_it()
    {
        var path = WriteFile("armed.md", "x\n");
        var window = TestShell.NewWindow();
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
        // There is nowhere to write it, and Save As is a question. The recovery snapshot covers this case.
        var window = TestShell.NewWindow();
        window.Show();
        SetText(window, "a draft with no home");

        window.AutoSaveNowForTest().GetAwaiter().GetResult();

        Assert.True(window.EditorView.IsDirty); // still unsaved, and no dialog opened
    }

    [AvaloniaFact]
    public void A_file_whose_encoding_was_only_guessed_is_never_written_back_automatically()
    {
        // 0xFF is not valid UTF-8, so the file opens through the single-byte fallback and the save is held
        // until a person confirms the guess. Auto-save must not be the thing that confirms it.
        var path = Path.Combine(_dir, "latin1.md");
        File.WriteAllBytes(path, [0x63, 0x61, 0x66, 0xE9, 0x0A]); // "café" in Latin-1

        // Left unshown on purpose: opening such a file puts up a modal notice that nothing in a headless
        // test can answer, and ShowError routes to the status bar instead when there is no visible window.
        // What is under test here is the encoding guard, not the notice in front of it.
        var window = TestShell.NewWindow();
        window.OpenPath(path);

        SetText(window, "rewritten");
        window.AutoSaveNowForTest().GetAwaiter().GetResult();

        Assert.Equal<byte[]>([0x63, 0x61, 0x66, 0xE9, 0x0A], File.ReadAllBytes(path));
        Assert.True(window.EditorView.IsDirty);
    }

    [AvaloniaFact]
    public void Turning_the_interval_off_in_Preferences_stops_it_on_the_open_document()
    {
        var settings = TestShell.NewSettings();
        settings.Settings.AutoSaveSeconds = 0;

        var path = WriteFile("off.md", "keep me\n");
        var window = TestShell.NewWindow(settings);
        window.Show();
        window.OpenPath(path);

        SetText(window, "should not reach disk");
        Dispatcher.UIThread.RunJobs();
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

    [Fact]
    public void A_settings_file_that_still_says_AutosaveSeconds_keeps_it_as_the_snapshot_interval()
    {
        // The key used to mean "seconds between recovery snapshots" before document auto-save took the
        // name. Someone who set it to 90 meant 90 seconds between snapshots, and still gets them.
        var path = Path.Combine(_dir, "legacy.json");
        File.WriteAllText(path, """{ "AutosaveSeconds": 90, "TabWidth": 2 }""");

        var loaded = new SettingsService(path).Settings;

        Assert.Equal(90, loaded.RecoverySnapshotSeconds);
        Assert.Equal(2, loaded.TabWidth);
        Assert.Equal(AutoSave.DefaultSeconds, loaded.AutoSaveSeconds); // the new setting takes its default

        // And the legacy key retires rather than living on beside a near-identically-spelled one.
        new SettingsService(path).Save();
        var written = File.ReadAllText(path);
        Assert.DoesNotContain("\"AutosaveSeconds\"", written, StringComparison.Ordinal);
        Assert.Contains("\"RecoverySnapshotSeconds\": 90", written, StringComparison.Ordinal);
    }
}
