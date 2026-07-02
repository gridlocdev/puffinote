using System.Text.Json;
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

    public SettingsService()
    {
        _path = SettingsStore.AppConfigPath("Typage", "settings.json");
        Settings = SettingsStore.Load(_path, static () => new AppSettings(), Options);
    }

    public AppSettings Settings { get; private set; }

    public void Save() => SettingsStore.Save(_path, Settings, Options);
}
