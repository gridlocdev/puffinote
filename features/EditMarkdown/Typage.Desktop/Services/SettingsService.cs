using System.Text.Json;

namespace Typage.Desktop.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON in the OS-standard app-config location
/// (PRD §7.9). Failures to read/write settings are non-fatal — the app falls back to defaults
/// rather than refusing to start.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }, // readable enum names
    };
    private readonly string _path;

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Typage");
        _path = Path.Combine(dir, "settings.json");
        Settings = Load();
    }

    public AppSettings Settings { get; private set; }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings — start fresh rather than crash.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(Settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort persistence; ignore failures.
        }
    }
}
