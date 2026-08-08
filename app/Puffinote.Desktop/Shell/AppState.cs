using System.Text.Json;
using Puffinote.Components.Shell;

namespace Puffinote.Desktop.Shell;

/// <summary>
/// Puffinote's per-user, cross-launch shell state — <b>global</b> to the app rather than scoped to one
/// notebook (that's <see cref="NotebookSession"/>). It holds where the app was when you left it: which
/// notebook to reopen on the next launch (PRD §5 — one notebook, one window), and which Preferences tab you
/// were last reading. Stored at the OS-standard per-user app-config path with the same tolerant-load /
/// best-effort-save contract as the other stores, so a missing or corrupt file simply means "nothing to
/// restore" and never blocks startup.
/// <para>
/// None of this is a <em>preference</em> — nobody chose it, it is just where they were — which is why it
/// lives here and not in <see cref="PuffinoteSettings"/>: it is neither layered over an editor's config nor
/// something a notebook should be able to commit and impose on whoever opens it.
/// </para>
/// <para>
/// Every field is written by a different part of the shell at a different moment, so a writer must layer its
/// one field onto the file as it stands (<c>Load() with { … }</c>) rather than saving a fresh record — a
/// <c>new AppState { OneField = … }.Save()</c> writes null over every other field.
/// </para>
/// </summary>
internal sealed record AppState
{
    /// <summary>The absolute path of the last-opened notebook root, or null when none has been opened yet.</summary>
    public string? LastNotebookPath { get; init; }

    /// <summary>
    /// The title of the Preferences tab that was showing when the dialog last closed, so reopening it comes
    /// back to the subject you were working on. Null until the dialog has been opened once, and stored by
    /// title rather than position so that adding, dropping, or reordering tabs in a later version can only
    /// fail to match — never match the wrong one.
    /// </summary>
    public string? LastSettingsTab { get; init; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Test seam: redirect the store off the real per-user config path so tests don't touch it.</summary>
    internal static string? PathOverrideForTest { get; set; }

    private static string StorePath => PathOverrideForTest ?? SettingsStore.AppConfigPath("Puffinote", "state.json");

    /// <summary>Load the app state (tolerant — an absent/corrupt file yields the empty default).</summary>
    public static AppState Load() => SettingsStore.Load(StorePath, static () => new AppState(), Options);

    /// <summary>Persist the app state (best-effort — an unwritable disk just isn't remembered next launch).</summary>
    public void Save() => SettingsStore.Save(StorePath, this, Options);
}
