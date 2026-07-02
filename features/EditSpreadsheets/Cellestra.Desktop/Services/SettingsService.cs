using System;
using System.IO;
using System.Text.Json;
using Puffinote.Components.Shell;

namespace Cellestra.Desktop.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> to
/// <c>~/Library/Application Support/Cellestra/settings.json</c> (ARCHITECTURE §8). The tolerant
/// load / best-effort save plumbing is the shared <see cref="SettingsStore"/> (proposal 0001 §4.1E);
/// this owns only Cellestra's file path and settings type.
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

    public AppSettings Load() => SettingsStore.Load(_path, static () => new AppSettings(), JsonOptions);

    public void Save(AppSettings settings) => SettingsStore.Save(_path, settings, JsonOptions);
}
