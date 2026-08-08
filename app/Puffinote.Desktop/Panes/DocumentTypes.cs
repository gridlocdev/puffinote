namespace Puffinote.Desktop.Panes;

/// <summary>
/// The file types Puffinote <em>claims</em>: the three it can create from the New menu, and every
/// extension that routes to one of their editors. One table, read by both sides of the claim —
/// <see cref="EditorPaneFactory"/> asks it what opens a file, and the shell's "New File…" asks it what a
/// hand-typed name is <b>not</b> allowed to be. Two lists would drift, and the drift is silent in the
/// direction that matters: a "New File" called <c>budget.ods</c> would produce a 0-byte workbook that the
/// spreadsheet pane then refuses to open (the same empty-file trap the seeded New Spreadsheet avoids).
/// </summary>
internal static class DocumentTypes
{
    /// <summary>What "New Note…" writes. The alternates below open, but only this one is ever created.</summary>
    public const string NoteExtension = ".md";

    /// <summary>What "New Spreadsheet…" writes.</summary>
    public const string SpreadsheetExtension = ".ods";

    /// <summary>
    /// What "New Taskboard…" writes. A compound extension, matched as a <i>suffix</i>:
    /// <c>Path.GetExtension("board.pivotask.json")</c> is <c>".json"</c>, so extension matching is
    /// structurally unable to see one (proposal 0004 §4).
    /// </summary>
    public const string BoardSuffix = ".pivotask.json";

    /// <summary>Every extension Typage opens as a note.</summary>
    public static readonly string[] MarkdownExtensions = [NoteExtension, ".markdown", ".mdown"];

    /// <summary>Every extension Cellestra opens as a workbook.</summary>
    public static readonly string[] SpreadsheetExtensions = [SpreadsheetExtension];

    /// <summary>A type that already owns a name's extension: what it is, and the command that makes one.</summary>
    internal readonly record struct Reservation(string Extension, string Kind, string Command);

    /// <summary>The reserved extensions, spelled out for the New File dialog's hint.</summary>
    public static string ReservedSummary => $"{NoteExtension}, {SpreadsheetExtension} and {BoardSuffix}";

    /// <summary>
    /// The document type that already owns <paramref name="name"/>'s extension, or null when the name is
    /// free for "New File…" to take. Case-insensitive, because the filesystems this ships on are.
    /// </summary>
    public static Reservation? ReservedFor(string name)
    {
        // The board first, for the same reason the pane factory asks in this order: its suffix ends in
        // ".json", so anything that looks only at the extension can't tell one from a plain JSON file.
        if (name.EndsWith(BoardSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return new Reservation(BoardSuffix, "a task board", "New Taskboard…");
        }

        var ext = System.IO.Path.GetExtension(name);
        if (Matches(ext, MarkdownExtensions))
        {
            return new Reservation(ext.ToLowerInvariant(), "a note", "New Note…");
        }

        if (Matches(ext, SpreadsheetExtensions))
        {
            return new Reservation(ext.ToLowerInvariant(), "a spreadsheet", "New Spreadsheet…");
        }

        return null;
    }

    private static bool Matches(string extension, string[] candidates) =>
        candidates.Any(e => extension.Equals(e, StringComparison.OrdinalIgnoreCase));
}
