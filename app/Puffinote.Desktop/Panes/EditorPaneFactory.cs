namespace Puffinote.Desktop.Panes;

/// <summary>
/// Maps a file to the pane that can edit it (ARCHITECTURE §4.1). Phase 1 knows only Markdown; Phase 2 adds
/// <c>.ods</c> → a Cellestra pane, and unsupported files get a preview / open-externally action (PRD §7.3).
/// A future file type is one new <see cref="IEditorPane"/> plus a branch here — no shell rewrite.
/// </summary>
internal static class EditorPaneFactory
{
    /// <summary>True when some pane in this build can open <paramref name="path"/>.</summary>
    public static bool CanOpen(string path) => IsMarkdown(path);

    /// <summary>Create a pane for <paramref name="path"/>, or null when no editor handles its type yet.</summary>
    public static IEditorPane? Create(string path) => IsMarkdown(path) ? new MarkdownPane() : null;

    private static bool IsMarkdown(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mdown", StringComparison.OrdinalIgnoreCase);
    }
}
