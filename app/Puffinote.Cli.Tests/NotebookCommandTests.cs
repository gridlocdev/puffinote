using Xunit;

namespace Puffinote.Cli.Tests;

/// <summary>
/// The notebook-scoped commands (<c>tree</c>/<c>search</c>/<c>info</c>) read the notebook directly and match
/// the sidebar's rules: folders before files, dotfiles + <c>.puffinote/</c> hidden, notes searched as
/// text and sheets by cell contents (ARCHITECTURE.md §11, PRD §7.2).
/// </summary>
[Collection("cli")]
public class NotebookCommandTests : CliTestBase
{
    [Fact]
    public void Tree_ListsFoldersFirst_AndHidesDotEntries()
    {
        Write("notes/apple.md", "a");
        Write("notes/banana.md", "b");
        Write("top.md", "t");
        Write(".hidden.md", "secret");
        Directory.CreateDirectory(Path.Combine(Notebook, ".puffinote"));
        File.WriteAllText(Path.Combine(Notebook, ".puffinote", "session.json"), "{}");

        var (code, output, _) = Run("tree", Notebook);
        Assert.Equal(0, code);

        var lines = output.Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { "notes/", "  apple.md", "  banana.md", "top.md" }, lines);
        Assert.DoesNotContain("hidden", output);
        Assert.DoesNotContain("session.json", output);
    }

    [Fact]
    public void Tree_DefaultsToCurrentDirectory()
    {
        Write("only.md", "x");
        var prior = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Notebook);
            var (code, output, _) = Run("tree");
            Assert.Equal(0, code);
            Assert.Equal("only.md", output.Trim());
        }
        finally
        {
            Directory.SetCurrentDirectory(prior);
        }
    }

    [Fact]
    public void Search_RanksByHitCount_AndReportsRelativePaths()
    {
        Write("many.md", "widget widget widget");
        Write("sub/few.md", "one widget here");
        Write("none.md", "nothing to see");

        var (code, output, _) = Run("search", "widget", Notebook);
        Assert.Equal(0, code);

        var lines = output.Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("many.md\t3\t", lines[0]);       // most hits first
        Assert.StartsWith($"sub{Path.DirectorySeparatorChar}few.md\t1\t", lines[1]);
        Assert.DoesNotContain("none.md", output);
    }

    [Fact]
    public void Search_CaseInsensitive()
    {
        Write("note.md", "The Puffin flies.");
        var (code, output, _) = Run("search", "puffin", Notebook);
        Assert.Equal(0, code);
        Assert.StartsWith("note.md\t1\t", output.Trim());
    }

    [Fact]
    public void Search_ReadsSheetCellContents()
    {
        // Build a real .ods through the sheet route so search exercises the OdsReader cell path.
        var sheet = Path.Combine(Notebook, "data.ods");
        Assert.Equal(0, Run("sheet", "new", sheet).Code);
        Assert.Equal(0, Run("sheet", "set", sheet, "Sheet1.A1", "pineapple", "-i").Code);

        var (code, output, _) = Run("search", "pineapple", Notebook);
        Assert.Equal(0, code);
        Assert.StartsWith("data.ods\t1\t", output.Trim());
    }

    [Fact]
    public void Search_EmptyQuery_IsACleanError()
    {
        var (code, _, err) = Run("search", "   ", Notebook);
        Assert.Equal(1, code);
        Assert.StartsWith("puffinote:", err.Trim());
    }

    [Fact]
    public void Info_CountsFilesByKind()
    {
        Write("a.md", "hello");
        Write("b.txt", "world");
        Write("c.ods", "not-really-ods-but-counts-by-extension");
        Write("d.png", "binary-ish");

        var (code, output, _) = Run("info", Notebook);
        Assert.Equal(0, code);

        Assert.Contains("files\t4", output);
        Assert.Contains("notes\t2", output);  // .md + .txt
        Assert.Contains("sheets\t1", output); // .ods
        Assert.Contains("other\t1", output);  // .png
        Assert.Contains($"notebook\t{Notebook}", output);
    }

    [Fact]
    public void Notebook_NotFound_IsACleanError()
    {
        var (code, _, err) = Run("info", Path.Combine(Notebook, "does-not-exist"));
        Assert.Equal(1, code);
        Assert.StartsWith("puffinote:", err.Trim());
    }
}
