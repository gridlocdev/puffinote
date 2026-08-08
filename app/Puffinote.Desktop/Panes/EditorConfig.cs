using System.Text.Json;
using Puffinote.Components.Shell;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Panes;

/// <summary>
/// Every embedded editor's own settings, as Puffinote sees them (PRD §7.5): one mirror per editor, each
/// read from — and written back to — the config file that editor reads when it runs on its own.
/// <para>
/// <b>Why mirrors and not the editors' own settings types.</b> Those types live in each editor's desktop
/// <i>app</i>, and Puffinote references the editors' embeddable controls, never their shells (one-way
/// layering, PRD §9). A mirror is the seam that keeps that true: it names the subset of each file Puffinote
/// can actually act on, defaulted to the same values the editor defaults to, so a note or a sheet opened
/// here behaves as it does standalone. The cost is that a mirror can drift from the settings type it
/// mirrors; the guard is that each one names the app and file it belongs to, and its defaults are pinned by
/// tests.
/// </para>
/// </summary>
internal sealed record EditorConfig
{
    /// <summary>Typage's settings, with any Puffinote override already layered on (PRD §7.5 precedence).</summary>
    public NoteConfig Notes { get; init; } = new();

    /// <summary>Cellestra's settings.</summary>
    public SheetConfig Sheets { get; init; } = new();

    /// <summary>Plaintain's settings.</summary>
    public PlainTextConfig PlainText { get; init; } = new();

    /// <summary>Pivotask's settings.</summary>
    public BoardConfig Tasks { get; init; } = new();

    /// <summary>
    /// How every mirror reads and writes its editor's file: indented, with enums as names — which is what
    /// the editors' own <c>SettingsService</c>s write, and what a person editing the file by hand expects to
    /// see. The <see cref="TolerantEnumConverter"/> keeps a value this build doesn't recognize from costing
    /// the user every other setting in the file.
    /// </summary>
    public static JsonSerializerOptions Json { get; } = new()
    {
        WriteIndented = true,
        Converters = { new TolerantEnumConverter() },
    };

    /// <summary>
    /// Read every editor's own config file (tolerant — an absent or corrupt file yields that editor's
    /// defaults), then layer Puffinote's <paramref name="overrides"/> over the ones it overrides.
    /// </summary>
    public static EditorConfig Resolve(PuffinoteSettings overrides) => new()
    {
        Notes = NoteConfig.Load().WithOverrides(overrides),
        Sheets = SheetConfig.Load(),
        PlainText = PlainTextConfig.Load(),
        Tasks = BoardConfig.Load(),
    };
}
