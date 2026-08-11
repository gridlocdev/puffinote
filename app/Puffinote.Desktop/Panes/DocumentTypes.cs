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
    /// What "New Taskboard…" writes. A plain extension of its own since the format stopped spelling
    /// itself <c>.pivotask.json</c>: the file is still JSON, but its <i>name</i> now says whose it is, so
    /// an operating system can hand a board to Pivotask without also offering Pivotask for every JSON
    /// file on the machine (proposal 0004 §4's compound-extension risk, removed rather than verified).
    /// </summary>
    public const string BoardSuffix = ".pivotask";

    /// <summary>
    /// The name boards used to have. Still <em>opened</em> — a notebook written before the rename is full
    /// of them, and refusing to open somebody's existing boards is not a rename, it is a deletion — but
    /// never created, and never claimed from the operating system.
    /// </summary>
    public const string LegacyBoardSuffix = ".pivotask.json";

    /// <summary>Every suffix that names a task board, newest first.</summary>
    public static readonly string[] BoardSuffixes = [BoardSuffix, LegacyBoardSuffix];

    /// <summary>Whether <paramref name="name"/> names a task board, under either spelling.</summary>
    public static bool IsBoard(string name) =>
        BoardSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

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
        // The board first, for the same reason the pane factory asks in this order: its legacy suffix ends
        // in ".json", so anything that looks only at the extension can't tell one from a plain JSON file.
        if (IsBoard(name))
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
