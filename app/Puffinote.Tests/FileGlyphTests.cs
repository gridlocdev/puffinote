using Puffinote.Desktop.Controls;
using Xunit;

namespace Puffinote.Tests;

/// <summary>
/// The file tree's type map: which glyph a file gets, and how its name splits into the stem the tree shows and
/// the extension its pill shows.
/// </summary>
public sealed class FileGlyphTests
{
    [Theory]
    [InlineData("Roadmap.md", "Roadmap", "md")]
    [InlineData("Budget.ods", "Budget", "ods")]
    [InlineData("2026-07-02.md", "2026-07-02", "md")] // a daily note: dots in the stem are not the extension
    [InlineData("README", "README", "")]
    [InlineData("archive.tar.gz", "archive", "tar.gz")] // a compound extension stays whole...
    [InlineData("types.d.ts", "types", "d.ts")]
    [InlineData("app.min.js", "app", "min.js")]
    [InlineData("notes.", "notes.", "")] // a trailing dot is not an extension
    public void Split_separates_the_stem_from_the_extension(string fileName, string stem, string extension)
    {
        Assert.Equal((stem, extension), FileGlyph.Split(fileName));
    }

    [Theory]
    [InlineData("Attention.md", "markdown")] // Puffinote's own two editable types lead the map
    [InlineData("Budget.ods", "table")]
    [InlineData("metrics.csv", "table")]
    [InlineData("cover.PNG", "image")] // extensions match case-insensitively
    [InlineData("survey.pdf", "file-text")]
    [InlineData("config.json", "file-json")]
    [InlineData("deploy.ts", "code")]
    [InlineData("backup.sh", "terminal")]
    [InlineData("config.yaml", "settings-2")]
    [InlineData("theme.mp3", "music")]
    [InlineData("intro.mp4", "film")]
    [InlineData("dataset.tar.gz", "archive")]
    [InlineData("model.bin", "binary")]
    public void A_file_gets_the_glyph_of_its_type(string fileName, string iconKey)
    {
        Assert.Equal(iconKey, FileGlyph.ForFile(fileName).IconKey);
    }

    [Fact]
    public void An_unknown_type_falls_back_to_a_plain_file()
    {
        Assert.Equal(FileGlyph.Unknown.IconKey, FileGlyph.ForFile("mystery.qqq").IconKey);
        Assert.Equal(FileGlyph.Unknown.IconKey, FileGlyph.ForFile("LICENSE").IconKey);
    }

    [Fact]
    public void An_unmapped_compound_extension_falls_back_to_its_final_segment()
    {
        // "backup.qqq.gz" is not in the map, but it is still visibly an archive — better than a blank page.
        Assert.Equal("archive", FileGlyph.For("qqq.gz").IconKey);
    }

    [Fact]
    public void Folders_do_not_share_the_accent()
    {
        // "this is a folder" and "this is the current thing" must not be said in the same hue, or the tree's two
        // most common signals collide. Guarding the intent, not the specific blue.
        Assert.NotEqual(Puffinote.Components.Theming.Tokens.Dark.Accent, FileGlyph.Folder);
    }
}
