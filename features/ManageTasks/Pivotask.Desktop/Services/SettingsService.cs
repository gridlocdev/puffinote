using System.Text.Json;
using System.Text.Json.Serialization;
using Pivotask.Control.Views;
using Pivotask.Core.Query;
using Puffinote.Components.Shell;
using Puffinote.Components.Theming;

namespace Pivotask.Desktop.Services;

/// <summary>
/// What Pivotask remembers between sessions. Deliberately short, and deliberately only the things a
/// person holds a standing opinion about: which reading of the board they want (columns or rows) and
/// which slice of it they want to land on. Everything else about a board lives in the board file, where
/// it travels with the document.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Light, dark, or whatever the OS is wearing. Unset means <see cref="ThemePreference.System"/>,
    /// because an app that has never been told which appearance to wear should wear the desktop's.
    /// </summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>The reading the board opens in. Columns is the default; the list is the same tasks.</summary>
    public BoardLayout Layout { get; set; } = BoardLayout.Board;

    /// <summary>The filter the board opens with — for someone who works from "unfinished", every time.</summary>
    public TaskFilter Filter { get; set; } = TaskFilter.All;
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON under the OS-standard per-user app-config location,
/// on the shared tolerant-load / best-effort-save contract (<see cref="SettingsStore"/>): a missing or
/// corrupt file is not an error, it is a first run, and the defaults are the answer.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }, // readable enum names in the file
    };

    private readonly string _path;

    /// <summary>
    /// Load the settings, from the app-config location by default. Passing a <paramref name="path"/>
    /// points both load and save somewhere else — the seam tests use to run against a scratch profile,
    /// so a headless window neither inherits the developer's preferences nor writes its own over them.
    /// </summary>
    public SettingsService(string? path = null)
    {
        _path = path ?? SettingsStore.AppConfigPath("Pivotask", "settings.json");
        Settings = SettingsStore.Load(_path, static () => new AppSettings(), Options);
    }

    public AppSettings Settings { get; }

    public void Save() => SettingsStore.Save(_path, Settings, Options);
}
