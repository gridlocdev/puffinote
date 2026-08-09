using System.Text.Json;

namespace Puffinote.Components.Core.Documents;

/// <summary>An unsaved-work snapshot written by a shell's snapshot timer.</summary>
public sealed class RecoverySnapshot
{
    /// <summary>The document's file path, or null if it was never saved (an Untitled buffer).</summary>
    public string? Path { get; set; }

    /// <summary>The buffer text at snapshot time.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>When the snapshot was taken (UTC).</summary>
    public DateTime SavedAtUtc { get; set; }
}

/// <summary>
/// Persists a single crash-recovery snapshot of unsaved work to the app-config location. A shell
/// snapshots the buffer here while it is dirty and clears it on a clean save or exit, so a surviving
/// snapshot at startup means the app didn't shut down cleanly — and the user is offered the chance to
/// recover. All operations are best-effort: a failure never interrupts editing.
/// <para>
/// Shared because the two text editors want exactly this and nothing app-specific is left once the app
/// name is a parameter — the alternative was a second copy, and a second copy is two answers to the
/// question of where unsaved work goes. It stays text-shaped on purpose: a workbook or a board is not a
/// string, and pretending otherwise here would be the wrong generalisation.
/// </para>
/// </summary>
public sealed class RecoveryService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    private readonly string _path;

    /// <param name="appName">
    /// The app's folder under the platform's application-data directory ("Typage", "Plaintain"). Each app
    /// keeps its own snapshot: recovering someone's log into a Markdown editor would be worse than not
    /// recovering it at all.
    /// </param>
    /// <param name="directory">An explicit directory, overriding <paramref name="appName"/> (tests).</param>
    public RecoveryService(string appName, string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);
        _path = Path.Combine(dir, "recovery.json");
    }

    /// <summary>Write a snapshot of the current (unsaved) buffer.</summary>
    public void Save(string? documentPath, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var snapshot = new RecoverySnapshot { Path = documentPath, Text = text, SavedAtUtc = DateTime.UtcNow };
            File.WriteAllText(_path, JsonSerializer.Serialize(snapshot, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a failed snapshot must never disrupt editing.
        }
    }

    /// <summary>The pending snapshot, or null if there is none (or it is unreadable).</summary>
    public RecoverySnapshot? TryLoad()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<RecoverySnapshot>(File.ReadAllText(_path));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt snapshot — ignore it rather than block startup.
        }

        return null;
    }

    /// <summary>Discard the snapshot (after a clean save/exit or once the user declines recovery).</summary>
    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore; a stale snapshot is harmless and will be overwritten on the next save.
        }
    }
}
