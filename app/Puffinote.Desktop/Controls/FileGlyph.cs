using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Puffinote.Desktop.Controls;

/// <summary>
/// How a file's <em>type</em> is drawn in the file tree: a vendored Lucide glyph plus the colour it is stroked
/// in. Pure — no Avalonia controls, no filesystem — so the whole map is unit-testable.
///
/// <para>The tree used to give every file a blank 15px spacer where a folder got a glyph, which made a notebook
/// read as an undifferentiated list of names: a note, an attached PDF and a spreadsheet looked identical until
/// you read the extension off the end of the name. Type is the first thing you scan a file list for, so it gets
/// the strongest channel available — shape — with colour as reinforcement and the extension pill (see
/// <see cref="Split"/>) as the literal, colour-independent statement of what the file is. That triple is
/// deliberate: shape and text both survive a reader who cannot distinguish the hues.</para>
///
/// <para>Puffinote's own two native types lead the map: <c>.md</c> (Typage) gets the first-party
/// <c>markdown</c> glyph and <c>.ods</c> (Cellestra) the <c>table</c> glyph, so the files the app actually
/// edits are the two that never have to be squinted at.</para>
/// </summary>
internal static class FileGlyph
{
    /// <summary>A file type's glyph key and stroke colour.</summary>
    internal readonly record struct Kind(string IconKey, IBrush Color);

    private static IBrush Rgb(byte r, byte g, byte b) => new ImmutableSolidColorBrush(Color.FromRgb(r, g, b));

    /// <summary>
    /// Folders are one colour, whatever they hold — a steady blue that is deliberately <em>not</em> the accent,
    /// so "this is a folder" and "this is the current thing" never collide in the same hue.
    /// </summary>
    public static IBrush Folder { get; } = Rgb(0x7F, 0x9C, 0xD4);

    /// <summary>The tree's glyph for a directory (open or shut).</summary>
    public static string FolderIcon(bool expanded) => expanded ? "folder-open" : "folder";

    // The families a notebook actually accumulates. Each is a (glyph, colour) pair; several families share a
    // glyph where the shapes would otherwise be split hairs (a PDF and a text file are both documents) and lean
    // on colour + the extension pill to separate them.
    private static readonly Kind Note = new("markdown", Rgb(0x51, 0x9D, 0xD6));
    private static readonly Kind Sheet = new("table", Rgb(0x62, 0xB5, 0x62));
    private static readonly Kind Image = new("image", Rgb(0x34, 0xB8, 0xC9));
    private static readonly Kind Pdf = new("file-text", Rgb(0xE0, 0x57, 0x4B));
    private static readonly Kind Text = new("file-text", Rgb(0x9A, 0xA4, 0xB2));
    private static readonly Kind Json = new("file-json", Rgb(0xD8, 0xB7, 0x4A));
    private static readonly Kind Code = new("code", Rgb(0x4B, 0x8B, 0xDE));
    private static readonly Kind Script = new("terminal", Rgb(0x7F, 0xAE, 0x6A));
    private static readonly Kind Config = new("settings-2", Rgb(0xB9, 0x8C, 0xD6));
    private static readonly Kind Audio = new("music", Rgb(0xD9, 0x8C, 0xC4));
    private static readonly Kind Video = new("film", Rgb(0xE0, 0x86, 0x4B));
    private static readonly Kind Archive = new("archive", Rgb(0xCB, 0xB2, 0x4A));
    private static readonly Kind Binary = new("binary", Rgb(0x9A, 0xA4, 0xB2));

    /// <summary>Anything unrecognised: a plain sheet of paper, in the same neutral as plain text.</summary>
    public static Kind Unknown { get; } = new("file", Rgb(0x9A, 0xA4, 0xB2));

    private static readonly Dictionary<string, Kind> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        // Puffinote's own two editable types.
        ["md"] = Note,
        ["markdown"] = Note,
        ["mdown"] = Note,
        ["ods"] = Sheet,
        ["csv"] = Sheet,
        ["tsv"] = Sheet,
        ["xlsx"] = Sheet,
        ["xls"] = Sheet,

        ["png"] = Image,
        ["jpg"] = Image,
        ["jpeg"] = Image,
        ["gif"] = Image,
        ["webp"] = Image,
        ["svg"] = Image,
        ["bmp"] = Image,
        ["ico"] = Image,
        ["avif"] = Image,
        ["heic"] = Image,

        ["pdf"] = Pdf,
        ["txt"] = Text,
        ["log"] = Text,
        ["rtf"] = Text,
        ["doc"] = Text,
        ["docx"] = Text,

        ["json"] = Json,
        ["jsonc"] = Json,

        ["js"] = Code,
        ["mjs"] = Code,
        ["cjs"] = Code,
        ["jsx"] = Code,
        ["ts"] = Code,
        ["tsx"] = Code,
        ["py"] = Code,
        ["go"] = Code,
        ["rs"] = Code,
        ["rb"] = Code,
        ["java"] = Code,
        ["cs"] = Code,
        ["c"] = Code,
        ["cpp"] = Code,
        ["h"] = Code,
        ["hpp"] = Code,
        ["php"] = Code,
        ["swift"] = Code,
        ["kt"] = Code,
        ["html"] = Code,
        ["xml"] = Code,
        ["css"] = Code,
        ["scss"] = Code,
        ["sass"] = Code,
        ["sql"] = Code,

        ["sh"] = Script,
        ["bash"] = Script,
        ["zsh"] = Script,
        ["fish"] = Script,
        ["ps1"] = Script,

        ["yaml"] = Config,
        ["yml"] = Config,
        ["toml"] = Config,
        ["ini"] = Config,
        ["env"] = Config,
        ["conf"] = Config,
        ["cfg"] = Config,

        ["mp3"] = Audio,
        ["wav"] = Audio,
        ["m4a"] = Audio,
        ["flac"] = Audio,
        ["aac"] = Audio,
        ["ogg"] = Audio,

        ["mp4"] = Video,
        ["mov"] = Video,
        ["webm"] = Video,
        ["avi"] = Video,
        ["mkv"] = Video,

        ["zip"] = Archive,
        ["tar"] = Archive,
        ["gz"] = Archive,
        ["tgz"] = Archive,
        ["rar"] = Archive,
        ["7z"] = Archive,
        ["bz2"] = Archive,
        ["xz"] = Archive,
        ["zst"] = Archive,
        ["tar.gz"] = Archive,
        ["tar.bz2"] = Archive,
        ["tar.xz"] = Archive,
        ["tar.zst"] = Archive,

        ["bin"] = Binary,
        ["exe"] = Binary,
        ["dll"] = Binary,
        ["wasm"] = Binary,
        ["so"] = Binary,
        ["dylib"] = Binary,
    };

    /// <summary>
    /// Extensions that are really two segments and must stay whole. <c>archive.tar.gz</c> is a gzipped tarball,
    /// not a "gz" file called <c>archive.tar</c>, and <c>types.d.ts</c> is a declaration file — splitting on the
    /// last dot would name them wrongly in the pill. Longest first, so <c>tar.gz</c> wins over <c>gz</c>.
    /// </summary>
    private static readonly string[] Compound =
    [
        "tar.gz", "tar.bz2", "tar.xz", "tar.zst",
        "d.ts", "test.ts", "test.tsx", "test.js", "spec.ts", "spec.js", "min.js", "min.css",
    ];

    /// <summary>
    /// Split a file name into the stem the tree shows and the extension its pill shows — <c>("Roadmap", "md")</c>
    /// for <c>Roadmap.md</c>, <c>("archive", "tar.gz")</c> for <c>archive.tar.gz</c>. A name with no extension
    /// (or a leading dot and nothing else) keeps its whole name and gets an empty extension, so it renders with
    /// no pill rather than an empty one.
    /// </summary>
    public static (string Stem, string Extension) Split(string fileName)
    {
        foreach (var compound in Compound)
        {
            if (fileName.Length > compound.Length + 1 &&
                fileName.EndsWith('.' + compound, StringComparison.OrdinalIgnoreCase))
            {
                return (fileName[..^(compound.Length + 1)], fileName[^compound.Length..]);
            }
        }

        int dot = fileName.LastIndexOf('.');
        return dot > 0 && dot < fileName.Length - 1
            ? (fileName[..dot], fileName[(dot + 1)..])
            : (fileName, string.Empty);
    }

    /// <summary>
    /// The glyph and colour for <paramref name="extension"/> (no leading dot). An unmapped compound extension
    /// falls back to its final segment — an unknown <c>foo.bar.gz</c> is still visibly an archive — and only
    /// then to <see cref="Unknown"/>.
    /// </summary>
    public static Kind For(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return Unknown;
        }

        if (ByExtension.TryGetValue(extension, out var kind))
        {
            return kind;
        }

        int dot = extension.LastIndexOf('.');
        return dot >= 0 && ByExtension.TryGetValue(extension[(dot + 1)..], out var tail) ? tail : Unknown;
    }

    /// <summary>The glyph and colour for a file <em>name</em> (the common case at a call site).</summary>
    public static Kind ForFile(string fileName) => For(Split(fileName).Extension);

    /// <summary>
    /// Every glyph key this map can ask for. A key with no vendored SVG behind it draws <em>nothing</em> rather
    /// than throwing, so the only thing standing between a typo here and an invisible icon in the tree is the
    /// test that walks this list — which is exactly how the tree shipped with a blank where every collapsed
    /// folder's chevron should have been.
    /// </summary>
    internal static IEnumerable<string> AllIconKeys =>
        ByExtension.Values
            .Select(kind => kind.IconKey)
            .Append(Unknown.IconKey)
            .Append(FolderIcon(expanded: false))
            .Append(FolderIcon(expanded: true))
            .Distinct(StringComparer.Ordinal);
}
