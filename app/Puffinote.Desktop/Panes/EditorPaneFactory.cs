namespace Puffinote.Desktop.Panes;

/// <summary>
/// Maps a file to the pane that can edit it (ARCHITECTURE §4.1): <c>.md</c> → Typage, <c>.ods</c> →
/// Cellestra. Unsupported files get a preview / open-externally action later (Phase 3, PRD §7.3). A future
/// file type is one new <see cref="IEditorPane"/> plus a branch here — no shell rewrite.
/// </summary>
internal static class EditorPaneFactory
{
    /// <summary>True when some pane in this build can open <paramref name="path"/>.</summary>
    public static bool CanOpen(string path) => IsMarkdown(path) || IsSpreadsheet(path);

    /// <summary>Create a pane for <paramref name="path"/>, or null when no editor handles its type yet.</summary>
    public static IEditorPane? Create(string path)
    {
        if (IsMarkdown(path))
        {
            return new MarkdownPane();
        }

        if (IsSpreadsheet(path))
        {
            return new SheetPane();
        }

        return null;
    }

    private static bool IsMarkdown(string path) =>
        HasExtension(path, ".md", ".markdown", ".mdown");

    private static bool IsSpreadsheet(string path) =>
        HasExtension(path, ".ods");

    private static bool HasExtension(string path, params string[] extensions)
    {
        var ext = System.IO.Path.GetExtension(path);
        return extensions.Any(e => ext.Equals(e, StringComparison.OrdinalIgnoreCase));
    }
}
