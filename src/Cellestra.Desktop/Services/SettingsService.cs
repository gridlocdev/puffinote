using System;
using System.IO;
using System.Text.Json;

namespace Cellestra.Desktop.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> to
/// <c>~/Library/Application Support/Cellestra/settings.json</c> (ARCHITECTURE §8). Failures are
/// swallowed so a missing or unreadable file never blocks the app from starting.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsService(string? path = null) => _path = path ?? DefaultPath();

    public static string DefaultPath()
    {
        string home = Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "Application Support", "Cellestra", "settings.json");
    }

    public AppSettings Load()
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
            // Corrupt or unreadable settings fall back to defaults.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: if we cannot write settings, continue without persisting.
        }
    }
}
