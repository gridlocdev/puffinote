namespace Puffinote.Desktop.Panes;

/// <summary>
/// Maps a file to the pane that can edit it (ARCHITECTURE §4.1): <c>.md</c> → Typage, <c>.ods</c> →
/// Cellestra, <c>.pivotask.json</c> → Pivotask, and <b>anything else that reads as text</b> → Plaintain.
/// A future file type is one new <see cref="IEditorPane"/> plus a branch here — no shell rewrite.
///
/// <para><b>The order matters, twice.</b> The specific editors are asked first, because a Markdown file
/// and a spreadsheet are both "text" to a decoder and neither should open in a plaintext pane. And a
/// board is matched by <i>suffix</i> before anything looks at extensions at all:
/// <c>Path.GetExtension("board.pivotask.json")</c> returns <c>".json"</c>, so extension matching is
/// structurally unable to see one — and <c>.json</c> is on Plaintain's fast list, so a board would
/// otherwise open as text (proposal 0004 §4, and the risk its §7 records).</para>
///
/// <para>Plaintain is last, and it is a <i>fallback</i> rather than another whitelist: a
/// known-extension list routes fast, and anything else that reads as text opens too. That is what lets
/// Puffinote genuinely open any text file — a <c>.log</c>, a <c>.conf</c>, an extensionless
/// <c>LICENSE</c> — without maintaining an infinite list of extensions (proposal 0003, Phase 4).</para>
/// </summary>
internal static class EditorPaneFactory
{
    /// <summary>Extensions Plaintain routes on sight, so the common cases never touch the disk twice.</summary>
    private static readonly string[] PlainTextExtensions =
    [
        ".txt", ".text", ".log", ".conf", ".cfg", ".ini", ".env", ".csv", ".tsv",
        ".yaml", ".yml", ".toml", ".xml", ".json", ".sh", ".sql", ".gitignore",
    ];

    /// <summary>
    /// Extensions that are a <b>declaration</b>, not a guess: whatever the bytes turn out to be, a file
    /// named <c>.png</c> is an image and belongs in the app that shows images. Without this the probe
    /// would happily open a truncated or placeholder PNG as text purely because its first 8 KB happened
    /// to contain no NUL — which is right about the bytes and wrong about the file.
    /// </summary>
    private static readonly string[] NeverText =
    [
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".avif", ".heic", ".tiff",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".rtf", ".odt", ".odp",
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a",
        ".mp4", ".mov", ".avi", ".mkv", ".webm",
        ".zip", ".gz", ".tar", ".bz2", ".xz", ".7z", ".rar",
        ".exe", ".dll", ".dylib", ".so", ".bin", ".woff", ".woff2", ".ttf", ".otf",
    ];

    /// <summary>How much of a file the decode probe reads. Enough to catch binary early, small enough
    /// that probing a 50 MB video costs nothing.</summary>
    private const int ProbeBytes = 8 * 1024;

    /// <summary>True when some pane in this build can open <paramref name="path"/>.</summary>
    public static bool CanOpen(string path) => IsMarkdown(path) || IsSpreadsheet(path) || IsBoard(path) || IsPlainText(path);

    /// <summary>
    /// Create a pane for <paramref name="path"/>, or null when no editor handles its type.
    /// <paramref name="config"/> is the already-resolved editor config (each editor's own settings with
    /// Puffinote's overrides layered on, PRD §7.5); omit it to inherit the editors' defaults. Seeding
    /// happens here rather than in each pane's constructor so every pane type inherits its editor's
    /// settings the same way, through the one interface the tab host also re-applies through.
    /// </summary>
    public static IEditorPane? Create(string path, EditorConfig? config = null)
    {
        var pane = CreateFor(path);
        if (pane is IConfigurablePane configurable && config is not null)
        {
            configurable.ApplyConfig(config);
        }

        return pane;
    }

    private static IEditorPane? CreateFor(string path)
    {
        if (IsMarkdown(path))
        {
            return new MarkdownPane();
        }

        if (IsSpreadsheet(path))
        {
            return new SheetPane();
        }

        // Before any extension test: ".pivotask.json" ends in ".json", which Plaintain routes on sight.
        if (IsBoard(path))
        {
            return new TaskBoardPane();
        }

        if (IsPlainText(path))
        {
            return new PlainTextPane();
        }

        return null;
    }

    // Which extension belongs to which editor is DocumentTypes' to say — the New menu reserves exactly
    // what these three route, and a list per side would drift.
    private static bool IsMarkdown(string path) =>
        HasExtension(path, DocumentTypes.MarkdownExtensions);

    private static bool IsSpreadsheet(string path) =>
        HasExtension(path, DocumentTypes.SpreadsheetExtensions);

    private static bool IsBoard(string path) =>
        path is not null && path.EndsWith(DocumentTypes.BoardSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether Plaintain should take this file: a known text extension, or — for anything else — bytes
    /// that actually look like text.
    /// </summary>
    private static bool IsPlainText(string path) =>
        !HasExtension(path, NeverText)
        && (HasExtension(path, PlainTextExtensions) || DecodesAsText(path));

    /// <summary>
    /// A decode probe over the first few KB. The test that matters is <b>not</b> "is this valid UTF-8" —
    /// a Latin-1 file is text, and Plaintain opens those read-only on purpose — but "does this contain
    /// bytes no text file has": a NUL, or a run of control characters. That is what separates a
    /// <c>.conf</c> from a <c>.png</c>, and it costs one small read rather than a whitelist nobody can
    /// finish writing.
    /// </summary>
    private static bool DecodesAsText(string path)
    {
        byte[] head;
        try
        {
            using var stream = File.OpenRead(path);
            head = new byte[(int)Math.Min(ProbeBytes, stream.Length)];
            int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            head = head[..read];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // unreadable is not "openable"
        }

        if (head.Length == 0)
        {
            return true; // an empty file is a perfectly good empty text file
        }

        foreach (byte b in head)
        {
            // NUL is the giveaway; the other control codes below 0x20 are allowed only where text uses
            // them (tab, LF, CR, form feed) — everything else is a binary format's framing.
            if (b == 0 || (b < 0x20 && b is not (0x09 or 0x0A or 0x0C or 0x0D)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasExtension(string path, params string[] extensions)
    {
        var ext = System.IO.Path.GetExtension(path);
        return extensions.Any(e => ext.Equals(e, StringComparison.OrdinalIgnoreCase));
    }
}
