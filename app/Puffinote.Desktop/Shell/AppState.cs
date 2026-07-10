using System.Text.Json;
using Puffinote.Components.Shell;

namespace Puffinote.Desktop.Shell;

/// <summary>
/// Puffinote's per-user, cross-launch shell state — <b>global</b> to the app rather than scoped to one
/// notebook (that's <see cref="NotebookSession"/>). Today it holds a single thing: which notebook to reopen
/// on the next launch (PRD §5 — one notebook, one window), so Puffinote comes back up where you left it.
/// Stored at the OS-standard per-user app-config path with the same tolerant-load / best-effort-save contract
/// as the other stores, so a missing or corrupt file simply means "no notebook to restore" and never blocks
/// startup.
/// </summary>
internal sealed record AppState
{
    /// <summary>The absolute path of the last-opened notebook root, or null when none has been opened yet.</summary>
    public string? LastNotebookPath { get; init; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Test seam: redirect the store off the real per-user config path so tests don't touch it.</summary>
    internal static string? PathOverrideForTest { get; set; }

    private static string StorePath => PathOverrideForTest ?? SettingsStore.AppConfigPath("Puffinote", "state.json");

    /// <summary>Load the app state (tolerant — an absent/corrupt file yields the empty default).</summary>
    public static AppState Load() => SettingsStore.Load(StorePath, static () => new AppState(), Options);

    /// <summary>Persist the app state (best-effort — an unwritable disk just isn't remembered next launch).</summary>
    public void Save() => SettingsStore.Save(StorePath, this, Options);
}
