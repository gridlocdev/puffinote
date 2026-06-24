using System.Text.Json;

namespace Typage.Desktop.Services;

/// <summary>An unsaved-work snapshot written by the autosave timer (PRD §9.4).</summary>
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
/// Persists a single crash-recovery snapshot of unsaved work to the app-config location (PRD §9.4).
/// The shell autosaves the buffer here while it is dirty and clears it on a clean save or exit, so a
/// surviving snapshot at startup means the app didn't shut down cleanly — and the user is offered the
/// chance to recover. All operations are best-effort: a failure never interrupts editing.
/// </summary>
public sealed class RecoveryService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    private readonly string _path;

    public RecoveryService(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Typage");
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
            // Best-effort: a failed autosave must never disrupt editing.
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
