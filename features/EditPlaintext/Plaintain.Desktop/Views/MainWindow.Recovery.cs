using Avalonia.Threading;
using Puffinote.Components.Core.Documents;
using Puffinote.Components.Overlays;

namespace Plaintain.Desktop.Views;

/// <summary>
/// Crash recovery: a periodic sidecar snapshot of an edited buffer, offered back on the next launch if
/// the app did not shut down cleanly.
/// <para>
/// This is the case Plaintain most needed and least had. Its auto-save is the narrowest in the family by
/// design — it refuses an untitled buffer, and it refuses a file whose encoding was only guessed, because
/// writing that guess back is the step that damages the file. Both refusals are right, and both leave the
/// text with nowhere to go. A snapshot has no such problem: it is our own file, in our own directory, in
/// UTF-8, and it is not the document. So it covers exactly the buffers auto-save cannot.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
    private readonly RecoveryService _recovery;
    private DispatcherTimer? _snapshotTimer;
    private bool _recoveryChecked;

    private void SetupRecoverySnapshots()
    {
        _snapshotTimer?.Stop();
        int seconds = _settings.Settings.RecoverySnapshotSeconds;
        if (seconds <= 0)
        {
            _snapshotTimer = null; // snapshots turned off
            return;
        }

        _snapshotTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _snapshotTimer.Tick += (_, _) => RecoverySnapshotTick();
        _snapshotTimer.Start();
    }

    /// <summary>Snapshot the buffer while it has unsaved changes; drop a stale snapshot once it hasn't.</summary>
    private void RecoverySnapshotTick()
    {
        if (EditorView.IsDirty)
        {
            _recovery.Save(CurrentPath, EditorView.Text);
        }
        else
        {
            _recovery.Clear();
        }
    }

    /// <summary>Test seam: run one snapshot cycle (a DispatcherTimer can't tick headlessly).</summary>
    internal void RecoverySnapshotTickForTest() => RecoverySnapshotTick();

    /// <summary>Test seam: whether the snapshot timer is running at all (an interval of 0 stops it).</summary>
    internal bool RecoverySnapshotsRunningForTest => _snapshotTimer?.IsEnabled == true;

    /// <summary>
    /// On startup, a surviving snapshot means the last session ended without a clean save or exit. Offer
    /// it back rather than restoring it unasked: the file on disk may well be the version the user wants,
    /// and silently replacing it with a buffer from a crash is its own kind of data loss.
    /// </summary>
    private async Task CheckForRecoveryAsync()
    {
        if (_recoveryChecked)
        {
            return;
        }

        _recoveryChecked = true;
        if (_recovery.TryLoad() is not { } snapshot)
        {
            return;
        }

        bool restore = await _overlay.ShowModalAsync(new ConfirmDialog(
            heading: "Unsaved changes were recovered",
            message: $"Plaintain closed without saving {(snapshot.Path is null ? "an untitled document" : $"“{Path.GetFileName(snapshot.Path)}”")} " +
                     $"on {snapshot.SavedAtUtc.ToLocalTime():g}. Restore what was in the buffer?",
            confirmLabel: "Restore",
            denyLabel: "Discard"));

        if (!restore)
        {
            _recovery.Clear();
            return;
        }

        // Re-adopt the original file first — its encoding and its place on disk — then lay the recovered
        // text over the top as an unsaved edit. Without the first half, a recovered Latin-1 file would be
        // saved back as UTF-8, which is the exact damage the read-only guard exists to prevent.
        if (snapshot.Path is { } path && File.Exists(path))
        {
            OpenPath(path);
        }

        // markSaved: false — recovered text is unsaved work by definition, and a buffer that looked clean
        // would let the next auto-save tick decide it had nothing to do.
        EditorView.LoadText(snapshot.Text, markSaved: false);
        UpdateChrome();
    }

    /// <summary>Test seam: run the startup recovery check now.</summary>
    internal Task CheckForRecoveryForTest() => CheckForRecoveryAsync();
}
