using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Plaintain.Desktop.Services;
using Plaintain.Desktop.Views;
using Puffinote.Components.Core.Documents;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// Crash recovery: a sidecar snapshot of an edited buffer, offered back if Plaintain closes without
/// saving.
/// <para>
/// This is the case this editor most needed and least had. Its auto-save is the narrowest in the family
/// by design — it refuses an untitled buffer, and it refuses a file whose encoding was only guessed,
/// because writing that guess back is the step that damages the file. Both refusals are right, and both
/// left the text with nowhere to go. A snapshot is our own file, in our own directory, in UTF-8, and it
/// is not the document, so it covers exactly what auto-save cannot.
/// </para>
/// </summary>
public class RecoveryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"plaintain-recovery-{Guid.NewGuid():N}");

    public RecoveryTests() => Directory.CreateDirectory(_dir);

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

    private RecoveryService NewRecovery() =>
        new("Plaintain", Path.Combine(_dir, $"recovery-{Guid.NewGuid():N}"));

    private MainWindow Open(RecoveryService recovery)
    {
        var settings = new SettingsService(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.json"));
        var window = new MainWindow(settings, recovery);
        window.Show();
        return window;
    }

    private static void SetText(MainWindow window, string text)
    {
        var buffer = window.EditorView.Editor.Buffer;
        buffer.ReplaceRange(0, buffer.Document.Length, text);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The buffer auto-save will not touch: no path, so nowhere to write it. Losing it is the whole reason
    /// this exists.
    /// </summary>
    [AvaloniaFact]
    public void An_untitled_buffer_is_snapshotted_even_though_auto_save_refuses_it()
    {
        var recovery = NewRecovery();
        var window = Open(recovery);
        SetText(window, "notes I have not saved anywhere");

        window.RecoverySnapshotTickForTest();

        var snapshot = recovery.TryLoad();
        Assert.NotNull(snapshot);
        Assert.Null(snapshot.Path);
        Assert.Equal("notes I have not saved anywhere", snapshot.Text);
    }

    /// <summary>A clean buffer has nothing to rescue, and a stale snapshot from before the save must go.</summary>
    [AvaloniaFact]
    public void A_clean_buffer_drops_the_snapshot_rather_than_keeping_a_stale_one()
    {
        var recovery = NewRecovery();
        var window = Open(recovery);
        SetText(window, "half a thought");
        window.RecoverySnapshotTickForTest();
        Assert.NotNull(recovery.TryLoad());

        var path = Path.Combine(_dir, "notes.txt");
        window.SaveForTest(path);
        window.RecoverySnapshotTickForTest();

        Assert.Null(recovery.TryLoad());
    }

    /// <summary>Saving is what makes the text durable, so the snapshot is dropped there and then.</summary>
    [AvaloniaFact]
    public void Saving_clears_the_snapshot_immediately()
    {
        var recovery = NewRecovery();
        var window = Open(recovery);
        SetText(window, "half a thought");
        window.RecoverySnapshotTickForTest();

        window.SaveForTest(Path.Combine(_dir, "notes.txt"));

        Assert.Null(recovery.TryLoad()); // without waiting for the next tick
    }

    /// <summary>
    /// The snapshot remembers which file it belongs to, so recovery can re-adopt that file's encoding
    /// rather than writing a recovered Latin-1 buffer back as UTF-8.
    /// </summary>
    [AvaloniaFact]
    public void A_snapshot_of_an_open_file_remembers_its_path()
    {
        var path = Path.Combine(_dir, "server.log");
        File.WriteAllText(path, "line one\n");
        var recovery = NewRecovery();
        var window = Open(recovery);
        window.OpenPath(path);
        SetText(window, "line one\nedited\n");

        window.RecoverySnapshotTickForTest();

        Assert.Equal(path, recovery.TryLoad()!.Path);
    }

    /// <summary>Snapshots can be turned off, and then nothing is written.</summary>
    [AvaloniaFact]
    public void An_interval_of_zero_turns_snapshots_off()
    {
        var settings = new SettingsService(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.json"));
        settings.Settings.RecoverySnapshotSeconds = 0;
        var recovery = NewRecovery();
        var window = new MainWindow(settings, recovery);
        window.Show();
        SetText(window, "half a thought");

        Assert.False(window.RecoverySnapshotsRunningForTest);
        Assert.Null(recovery.TryLoad());
    }

    /// <summary>
    /// A clean exit means there is nothing to recover next launch — which is what makes a <em>surviving</em>
    /// snapshot mean "this app did not shut down cleanly" rather than "this app was open recently".
    /// The window is closed with a clean buffer, because a dirty one raises the discard prompt first; the
    /// snapshot is planted directly, standing in for one the timer wrote earlier in the session.
    /// </summary>
    [AvaloniaFact]
    public void Closing_the_window_clears_the_snapshot()
    {
        var recovery = NewRecovery();
        var window = Open(recovery);
        recovery.Save(null, "half a thought");
        Assert.NotNull(recovery.TryLoad());

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Null(recovery.TryLoad());
    }
}
