namespace Typage.Engine.Markdown;

/// <summary>The multi-line block a line is inside when tokenization reaches it.</summary>
public enum BlockKind
{
    /// <summary>Ordinary content: paragraphs, headings, lists, quotes, rules — classified per line.</summary>
    Normal,

    /// <summary>Inside a fenced code block opened by ``` or ~~~ (PRD §8.1).</summary>
    FencedCode,

    /// <summary>Inside leading YAML front matter delimited by <c>---</c> (PRD §7.4).</summary>
    FrontMatter,

    /// <summary>Inside an indented (4-space) code block (CommonMark §4.4).</summary>
    IndentedCode,
}

/// <summary>
/// The block state carried from one line to the next (PRD §8.1). Only constructs that truly
/// span lines need carried state — fenced code (remembering its fence char/length so only a
/// matching fence closes it) and front matter. Everything else is recognized per line. The
/// checkpoint cache stores this value at line boundaries so any viewport can resume
/// tokenizing from just above it instead of from the top of the file (PRD §8.1, §9.1).
/// </summary>
/// <param name="AfterBlank">
/// True when the previous line was blank (or this is the top of the document) — the only position an
/// indented code block may open from, so this rides along the carried state (CommonMark §4.4: an
/// indented line cannot interrupt a paragraph).
/// </param>
public readonly record struct BlockState(BlockKind Kind, char FenceChar, int FenceLength, string? Language, bool AfterBlank = false)
{
    /// <summary>The default state between blocks (the previous line was non-blank).</summary>
    public static readonly BlockState Normal = new(BlockKind.Normal, '\0', 0, null);

    /// <summary>The start-of-document state: Normal, but a fresh block position (an indented line here is code).</summary>
    public static readonly BlockState Start = new(BlockKind.Normal, '\0', 0, null, AfterBlank: true);

    /// <summary>Normal state entering a line, recording whether the line above it was blank.</summary>
    public static BlockState NormalAfter(bool blank) => new(BlockKind.Normal, '\0', 0, null, blank);

    public static BlockState Fenced(char fenceChar, int fenceLength, string? language) =>
        new(BlockKind.FencedCode, fenceChar, fenceLength, language);

    public static readonly BlockState FrontMatter = new(BlockKind.FrontMatter, '\0', 0, null);

    /// <summary>Inside an indented code block.</summary>
    public static readonly BlockState IndentedCode = new(BlockKind.IndentedCode, '\0', 0, null);
}
