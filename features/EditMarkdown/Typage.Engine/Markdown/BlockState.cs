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
}

/// <summary>
/// The block state carried from one line to the next (PRD §8.1). Only constructs that truly
/// span lines need carried state — fenced code (remembering its fence char/length so only a
/// matching fence closes it) and front matter. Everything else is recognized per line. The
/// checkpoint cache stores this value at line boundaries so any viewport can resume
/// tokenizing from just above it instead of from the top of the file (PRD §8.1, §9.1).
/// </summary>
public readonly record struct BlockState(BlockKind Kind, char FenceChar, int FenceLength, string? Language)
{
    /// <summary>The default state at the top of a document and between blocks.</summary>
    public static readonly BlockState Normal = new(BlockKind.Normal, '\0', 0, null);

    public static BlockState Fenced(char fenceChar, int fenceLength, string? language) =>
        new(BlockKind.FencedCode, fenceChar, fenceLength, language);

    public static readonly BlockState FrontMatter = new(BlockKind.FrontMatter, '\0', 0, null);
}
