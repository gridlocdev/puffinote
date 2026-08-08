using System.Text.Json;
using System.Text.Json.Nodes;

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

    /// <summary>
    /// Write every property of <paramref name="value"/> into the JSON object at <paramref name="path"/>,
    /// leaving the keys <paramref name="value"/> doesn't define exactly as they were, and creating the file
    /// when it is absent.
    /// <para>
    /// This is what <see cref="Save{T}"/> can't do: saving serializes the whole object, so a writer that
    /// only models <em>part</em> of a file silently drops the rest. That is the normal case when one app
    /// edits another's config — Puffinote's Preferences writes Typage's tab width, and must not take
    /// Typage's recent-files list with it — and it is also what keeps a hand-added key that no UI exposes
    /// alive across an edit from the UI. A corrupt or non-object file is treated as absent (same tolerance
    /// as <see cref="Load{T}"/>): its unreadable content is replaced rather than merged into.
    /// </para>
    /// </summary>
    public static void Merge<T>(string path, T value, JsonSerializerOptions? options = null)
    {
        var root = ReadObject(path);
        if (JsonSerializer.SerializeToNode(value, options ?? DefaultOptions) is not JsonObject properties)
        {
            return;
        }

        foreach (var (key, node) in properties)
        {
            // DeepClone because a JsonNode belongs to one parent: assigning the serialized node directly
            // would try to re-parent it out of `properties` and throw.
            root[key] = node?.DeepClone();
        }

        Save(path, root, options);
    }

    /// <summary>The JSON object stored at <paramref name="path"/>, or an empty one when it is absent or unreadable.</summary>
    private static JsonObject ReadObject(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Same contract as Load: an unreadable file is a first run, not an error.
        }

        return [];
    }
}
