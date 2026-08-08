using System.Text.Json;
using System.Text.Json.Serialization;
using Puffinote.Components.Shell;
using Puffinote.Components.Theming;

namespace Plaintain.Desktop.Services;

/// <summary>What Plaintain remembers between sessions. Deliberately short.</summary>
public sealed class AppSettings
{
    /// <summary>
    /// Light, dark, or whatever the OS is wearing. Unset means <see cref="ThemePreference.System"/>,
    /// because an app that has never been told which appearance to wear should wear the desktop's.
    /// </summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>Whether long lines fold. Off is the Notepad default; the toggle is remembered because
    /// people who want wrap want it for everything, and people who don't never want it once.</summary>
    public bool WordWrap { get; set; }

    public bool ShowLineNumbers { get; set; } = true;

    public double FontSize { get; set; } = 13;

    /// <summary>The reading zoom applied on top of <see cref="FontSize"/>, on the family's shared ladder
    /// (1.0 = 100%).</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>
    /// Seconds of no editing before the open file is written back to disk; 0 turns auto-save off.
    /// Plaintain opens logs and config files as readily as notes, so this only ever runs for a file whose
    /// encoding is confirmed — see the guard in the window.
    /// </summary>
    public int AutoSaveSeconds { get; set; } = AutoSave.DefaultSeconds;

    /// <summary>Most-recently-used files, newest first.</summary>
    public List<string> RecentFiles { get; init; } = [];

    /// <summary>Record <paramref name="path"/> as the most recent, de-duplicated and capped.</summary>
    public void PushRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > 10)
        {
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
        }
    }
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON under the platform's application-data directory.
/// A missing or corrupt file is not an error — it is a first run, and the defaults are the answer.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // Readable enum names, as the rest of the family writes them: "Dark" in the file says what it
        // means to whoever opens it, where a 2 would send them looking for the enum that defines it.
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public SettingsService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Plaintain", "settings.json");
        Settings = Read();
    }

    public AppSettings Settings { get; private set; }

    private AppSettings Read()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Settings, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a preference is not worth interrupting anyone over.
        }
    }
}
