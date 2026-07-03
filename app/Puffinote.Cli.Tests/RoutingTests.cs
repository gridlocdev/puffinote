using Xunit;

namespace Puffinote.Cli.Tests;

/// <summary>
/// The router reaches both sub-CLIs' real command trees under <c>md</c> / <c>sheet</c>, and reports
/// errors from anywhere in the tree as a clean <c>puffinote: …</c> line (ARCHITECTURE.md §11).
/// </summary>
[Collection("cli")]
public class RoutingTests : CliTestBase
{
    [Fact]
    public void Md_Html_RunsTypageRenderer()
    {
        var file = Write("note.md", "# Title\n\nHello **world**.");
        var (code, output, _) = Run("md", "html", file);
        Assert.Equal(0, code);
        Assert.Contains("<h1>Title</h1>", output);
        Assert.Contains("<strong>world</strong>", output);
    }

    [Fact]
    public void Md_Outline_RunsTypageOutline()
    {
        var file = Write("note.md", "# One\n\n## Two\n");
        var (code, output, _) = Run("md", "outline", file);
        Assert.Equal(0, code);
        Assert.Contains("One", output);
        Assert.Contains("  Two", output); // level-2 heading indented by the typage outline
    }

    [Fact]
    public void Sheet_NewSetSheets_RunsCellestraCommands()
    {
        var file = Path.Combine(Notebook, "book.ods");

        Assert.Equal(0, Run("sheet", "new", file).Code);
        Assert.True(File.Exists(file));

        Assert.Equal(0, Run("sheet", "set", file, "Sheet1.A1", "hello", "-i").Code);

        var (code, output, _) = Run("sheet", "sheets", file);
        Assert.Equal(0, code);
        Assert.Contains("Sheet1", output);
    }

    [Fact]
    public void Md_MissingFile_ReportsCleanPuffinoteError()
    {
        var (code, _, err) = Run("md", "cat", Path.Combine(Notebook, "nope.md"));
        Assert.Equal(1, code);
        Assert.StartsWith("puffinote:", err.Trim());
        Assert.DoesNotContain("Exception", err); // a clean message, not a stack trace
    }

    [Fact]
    public void Sheet_UnknownFormat_ReportsCleanPuffinoteError()
    {
        // Cellestra raises this as a user error; the router folds its expected-error set in so it stays
        // a clean line rather than surfacing as a bug.
        var file = Write("note.txt", "not a workbook");
        var (code, _, err) = Run("sheet", "cat", file, "--from", "bogus");
        Assert.Equal(1, code);
        Assert.StartsWith("puffinote:", err.Trim());
    }
}
