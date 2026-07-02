using System.IO;
using System.Text.Json;

namespace Puffinote.Components.Shell;

/// <summary>
/// The shared JSON-persistence mechanism behind each app's <c>SettingsService</c> (proposal 0001 §4.1E:
/// "a contract plus a JSON default"). Each app keeps its own settings <i>type</i> and file <i>path</i>
/// (they diverge, and one is UI-coupled) and hands them here; this owns the load/save plumbing —
/// tolerant load (missing/corrupt/unreadable falls back to a caller-supplied default) and best-effort
/// save (creates the directory; never throws on an I/O failure), so settings never block startup.
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions DefaultOptions = new() { WriteIndented = true };

    /// <summary>A file under the OS-standard per-user app-config directory: <c>&lt;AppData&gt;/{app}/{fileName}</c>.</summary>
    public static string AppConfigPath(string app, string fileName) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), app, fileName);

    /// <summary>
    /// Deserialize <typeparamref name="T"/> from <paramref name="path"/>, or return
    /// <paramref name="createDefault"/>() when the file is absent, corrupt, or unreadable.
    /// </summary>
    public static T Load<T>(string path, Func<T> createDefault, JsonSerializerOptions? options = null)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options ?? DefaultOptions) ?? createDefault();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings fall back to defaults rather than crashing.
        }

        return createDefault();
    }

    /// <summary>
    /// Serialize <paramref name="value"/> to <paramref name="path"/> (creating its directory).
    /// Best-effort: an I/O or permission failure is swallowed so a read-only disk can't block the app.
    /// </summary>
    public static void Save<T>(string path, T value, JsonSerializerOptions? options = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(value, options ?? DefaultOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort persistence; if we cannot write, continue without persisting.
        }
    }
}
