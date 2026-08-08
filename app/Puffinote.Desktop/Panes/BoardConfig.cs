using Pivotask.Control.Views;
using Pivotask.Core.Query;
using Puffinote.Components.Controls;
using Puffinote.Components.Shell;

namespace Puffinote.Desktop.Panes;

/// <summary>
/// The task-board settings Puffinote inherits from Pivotask's <em>own</em> config file — a mirror, for the
/// reasons <see cref="EditorConfig"/> gives. Pivotask keeps only the two standing opinions a person has
/// about a board — which reading of it they want, and which slice they want to land on — and this covers
/// both; everything else about a board lives in the board file, where it travels with the document.
/// </summary>
internal sealed record BoardConfig
{
    /// <summary>The reading a board opens in: columns, or grouped rows.</summary>
    public BoardLayout Layout { get; set; } = BoardLayout.Board;

    /// <summary>The filter a board opens with.</summary>
    public TaskFilter Filter { get; set; } = TaskFilter.All;

    /// <summary>Pivotask's own settings file — the same path standalone Pivotask reads and writes.</summary>
    public static string DefaultPath => SettingsStore.AppConfigPath("Pivotask", "settings.json");

    /// <summary>Load Pivotask's own settings (tolerant — absent/corrupt yields Pivotask's defaults).</summary>
    public static BoardConfig Load(string? path = null) =>
        SettingsStore.Load(path ?? DefaultPath, static () => new BoardConfig(), EditorConfig.Json);

    /// <summary>Write these back into Pivotask's own file, merging so unmodelled keys survive.</summary>
    public void Save(string? path = null) =>
        SettingsStore.Merge(path ?? DefaultPath, this, EditorConfig.Json);

    /// <summary>The Preferences form's "Tasks" tab, over this instance.</summary>
    public IReadOnlyList<SettingsSection> Fields() =>
    [
        new SettingsSection("Board",
        [
            ChoiceSetting.ForEnum<BoardLayout>("Open in", () => Layout, v => Layout = v,
                ["Board", "List"],
                "Columns or grouped rows — the same tasks, read two ways."),
            ChoiceSetting.ForEnum<TaskFilter>("Show", () => Filter, v => Filter = v,
                ["All tasks", "Starred", "Due soon", "Unfinished"],
                "The filter a board opens with. The View menu still switches it per session."),
        ]),
    ];
}
