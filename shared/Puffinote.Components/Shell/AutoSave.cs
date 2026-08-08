using Avalonia.Threading;
using Puffinote.Components.Controls;

namespace Puffinote.Components.Shell;

/// <summary>
/// The family's inactivity auto-save: a countdown that the document's own edits keep pushing back, and
/// that writes the document once the editing stops. One implementation for all five apps, because the
/// hard parts are the same everywhere — don't stack a second save on an unfinished one, don't keep trying
/// after a failure, and don't fire while the document is in a state that can't be written.
/// <para>
/// <b>Inactivity, not a heartbeat.</b> <see cref="Notify"/> restarts the countdown, so a save lands in the
/// pause after a burst of typing rather than in the middle of one. That is what the feature was asked for
/// and it is also the kinder behaviour: writing mid-keystroke means the file on disk is a snapshot of half
/// a thought, and on a large sheet it means a serialize competing with the typing. The cost is that
/// someone who types without pausing for ten minutes is not auto-saved in those ten minutes — Typage's
/// crash-recovery snapshots (PRD §9.4) are the belt to this braces, and every app still warns on close.
/// </para>
/// <para>
/// <b>What it never does.</b> It never opens a dialog. An auto-save that could ask a question would ask it
/// while you were reading something else — so the host's callback is expected to decline (return without
/// writing) whenever saving would need an answer: an untitled document that has nowhere to be written, a
/// file whose encoding was guessed and not yet confirmed, a modal already up. Declining costs nothing; the
/// next edit arms the countdown again.
/// </para>
/// </summary>
public sealed class AutoSave : IDisposable
{
    /// <summary>The interval every app ships with: long enough to be a pause, short enough to be a rescue.</summary>
    public const int DefaultSeconds = 30;

    /// <summary>
    /// The shortest interval actually honoured. The Preferences spinner can't go below it, but a
    /// hand-edited config file can say <c>1</c> — and a document rewritten every second is a disk that
    /// never idles, so a positive value under this is raised to it rather than obeyed.
    /// </summary>
    public const int MinimumSeconds = 5;

    /// <summary>The longest interval offered — ten minutes, past which "auto" isn't the word for it.</summary>
    public const int MaximumSeconds = 600;

    private readonly Func<Task> _save;
    private readonly DispatcherTimer _timer = new();
    private bool _saving;

    /// <summary>
    /// An auto-save that runs <paramref name="save"/> once the document has been idle for
    /// <see cref="IntervalSeconds"/>. The callback runs on the UI thread and is responsible for deciding
    /// whether this document can be written at all (see the type remarks).
    /// </summary>
    public AutoSave(Func<Task> save)
    {
        _save = save;
        _timer.Tick += (_, _) => _ = RunAsync();
    }

    /// <summary>
    /// Seconds of no editing before the document is written; <c>0</c> or less turns auto-save off, and a
    /// positive value below <see cref="MinimumSeconds"/> is raised to it. Assigning this while a countdown
    /// is running restarts it on the new interval, so a change made in Preferences takes effect on the
    /// document you are already editing rather than on the next one.
    /// </summary>
    public int IntervalSeconds
    {
        get;
        set
        {
            var wanted = value <= 0 ? 0 : Math.Clamp(value, MinimumSeconds, MaximumSeconds);
            if (field == wanted)
            {
                return;
            }

            field = wanted;
            if (wanted == 0)
            {
                _timer.Stop();
            }
            else if (_timer.IsEnabled)
            {
                Notify(); // re-arm on the new interval
            }
        }
    }

    /// <summary>
    /// Stand auto-save down without forgetting the interval the user chose. Set after a write fails —
    /// a save that just failed will almost certainly fail again in thirty seconds, and an editor that
    /// reports the same I/O error every thirty seconds for an afternoon is worse than one that reports it
    /// once and waits. The host clears it when the next manual save succeeds, which is the user telling us
    /// the obstacle is gone.
    /// </summary>
    public bool Suspended
    {
        get;
        set
        {
            field = value;
            if (value)
            {
                _timer.Stop();
            }
        }
    }

    /// <summary>Whether a countdown is currently running (a save is due unless the document is edited again).</summary>
    public bool IsPending => _timer.IsEnabled;

    /// <summary>
    /// The document changed: start the countdown, or push back the one already running. Cheap enough to
    /// call on every keystroke, which is how every host calls it.
    /// </summary>
    public void Notify()
    {
        if (IntervalSeconds <= 0 || Suspended)
        {
            return;
        }

        _timer.Stop(); // restart, rather than let the running countdown finish early
        _timer.Interval = TimeSpan.FromSeconds(IntervalSeconds);
        _timer.Start();
    }

    /// <summary>
    /// Drop any pending save. Called when the document is saved, closed, or replaced — at which point the
    /// countdown is about a document that is no longer the one in front of the user.
    /// </summary>
    public void Cancel() => _timer.Stop();

    /// <summary>
    /// Run the pending auto-save now. This is what the timer's tick calls, and it is the seam the headless
    /// UI tests drive — a <see cref="DispatcherTimer"/> never ticks in a test that isn't pumping real time,
    /// so every app exposes this rather than making its suite wait out thirty seconds.
    /// <para>
    /// Re-entrant calls are dropped rather than queued: the save in flight is already writing the text the
    /// second one would write, and two overlapping atomic writes to one path is a race with no upside.
    /// </para>
    /// </summary>
    public async Task RunAsync()
    {
        _timer.Stop();
        if (_saving || Suspended || IntervalSeconds <= 0)
        {
            return;
        }

        _saving = true;
        try
        {
            await _save();
        }
        finally
        {
            _saving = false;
        }
    }

    public void Dispose() => _timer.Stop();

    /// <summary>
    /// The Preferences row every app shows for this, so the five read as one surface and the explanation
    /// of what "0" means is written once. Hosts differ only in which section they file it under.
    /// </summary>
    public static SettingsField IntervalField(Func<int> get, Action<int> set) =>
        new NumberSetting(
            "Auto-save",
            () => get(),
            v => set((int)v),
            0,
            MaximumSeconds,
            step: 5,
            hint: $"Seconds of no editing before the document is saved to disk. 0 turns auto-save off; "
                + $"anything lower than {MinimumSeconds} is treated as {MinimumSeconds}.");
}
