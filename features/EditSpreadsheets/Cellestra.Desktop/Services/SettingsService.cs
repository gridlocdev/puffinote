using System.Text.Json;
using Puffinote.Components.Controls.CommandBar;
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
    // Enums by name, tolerantly: the settings file is meant to be read and hand-edited, and a value this
    // build doesn't recognise should cost that one setting rather than every other setting in the file.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new TolerantEnumConverter() },
    };

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
        var settings = SettingsStore.Load(_path, static () => new AppSettings(), JsonOptions);

        // The command bar was a two-state boolean (collapsed or not) before it grew its third state; carry a
        // file written by that build over rather than springing the bar back open on the user (PRD §6.3).
        return SettingsStore.MigratedBool(_path, nameof(AppSettings.CommandBar), "CommandBarCollapsed") is { } collapsed
            ? settings with { CommandBar = collapsed ? CommandBarVisibility.Collapsed : CommandBarVisibility.Visible }
            : settings;
    }

    public void Save(AppSettings settings) => SettingsStore.Save(_path, settings, JsonOptions);
}
