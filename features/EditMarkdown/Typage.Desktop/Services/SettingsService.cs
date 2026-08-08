using System.Text.Json;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Shell;

namespace Typage.Desktop.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON in the OS-standard app-config location
/// (PRD §7.9). The tolerant load / best-effort save plumbing is the shared <see cref="SettingsStore"/>
/// (proposal 0001 §4.1E); this owns only Typage's file path, serializer options, and settings type.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }, // readable enum names
    };
    private readonly string _path;

    /// <summary>
    /// Load the settings, from the app-config location by default. Passing a <paramref name="path"/>
    /// points both load and save somewhere else — the seam tests use to run against a scratch profile
    /// (as Cellestra's <c>SettingsService</c> already did), so a headless window neither inherits the
    /// developer's theme and recent files nor writes its own back over them.
    /// </summary>
    public SettingsService(string? path = null)
    {
        _path = path ?? SettingsStore.AppConfigPath("Typage", "settings.json");
        Settings = SettingsStore.Load(_path, static () => new AppSettings(), Options);

        // The toolbar was a shown/hidden boolean before it grew its third state; carry a file written by that
        // build over rather than putting a bar back over the text of someone who had turned it off (PRD §7.5).
        if (SettingsStore.MigratedBool(_path, nameof(AppSettings.Toolbar), "ShowToolbar") is { } shown)
        {
            Settings.Toolbar = shown ? CommandBarVisibility.Visible : CommandBarVisibility.Hidden;
        }
    }

    public AppSettings Settings { get; private set; }

    public void Save() => SettingsStore.Save(_path, Settings, Options);
}
