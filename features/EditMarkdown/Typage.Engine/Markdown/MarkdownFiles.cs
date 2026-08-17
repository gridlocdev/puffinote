namespace Typage.Engine.Markdown;

/// <summary>
/// Which file names Typage opens. One list, read by everything that has to agree on the answer: the
/// desktop Open dialog's filter, and the `typage &lt;file&gt;` launcher deciding whether the first
/// argument is a document or a mistyped command. The `Info.plist` document-type claim is the third
/// reader and can't reference C#, so it is the one place this list is spelled twice — keep them in step.
/// </summary>
public static class MarkdownFiles
{
    /// <summary>
    /// Every extension Typage opens, lowercase and dotted. `.txt` is here because a plain-text file is
    /// valid Markdown and someone editing notes in one should not be turned away; Plaintain is the app
    /// that *claims* `.txt`, and this is only what Typage will accept when asked.
    /// </summary>
    public static readonly string[] Extensions = [".md", ".markdown", ".mdown", ".mkd", ".txt"];

    /// <summary>The same list as glob patterns, for a file picker's filter.</summary>
    public static string[] Patterns => [.. Extensions.Select(e => $"*{e}")];

    /// <summary>
    /// Whether <paramref name="path"/> names a document Typage opens. Case-insensitive, because the
    /// filesystems this ships on are.
    /// </summary>
    public static bool IsOne(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The extensions as a person reads them — for the message when an argument isn't one.</summary>
    public static string Summary => string.Join(", ", Extensions);
}
