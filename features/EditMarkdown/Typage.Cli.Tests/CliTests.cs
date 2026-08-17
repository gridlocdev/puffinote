using Xunit;

namespace Typage.Cli.Tests;

/// <summary>
/// End-to-end tests that drive the CLI in-process via <see cref="CliApplication.Run"/>. They
/// redirect the process-global <see cref="Console"/>, so they live in one class and run
/// sequentially (mirroring the Cellestra.Cli.Tests convention).
/// </summary>
[Collection("cli")]
public class CliTests : IDisposable
{
    private readonly TextWriter _origOut = Console.Out;
    private readonly TextWriter _origErr = Console.Error;
    private readonly string _temp = Path.Combine(Path.GetTempPath(), $"typage-test-{Guid.NewGuid():N}");

    public CliTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        Console.SetOut(_origOut);
        Console.SetError(_origErr);
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_temp, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var outW = new StringWriter();
        var errW = new StringWriter();
        Console.SetOut(outW);
        Console.SetError(errW);
        var code = CliApplication.Run(args);
        return (code, outW.ToString(), errW.ToString());
    }

    [Fact]
    public void Html_RendersDocument()
    {
        var file = Write("doc.md", "# Title\n\nHello **world**.");
        var (code, output, _) = Run("html", file);
        Assert.Equal(0, code);
        Assert.Contains("<!DOCTYPE html>", output);
        Assert.Contains("<h1>Title</h1>", output);
        Assert.Contains("<strong>world</strong>", output);
        Assert.Contains("<title>doc</title>", output);
    }

    [Fact]
    public void Html_Fragment_OmitsWrapper()
    {
        var file = Write("doc.md", "# Title");
        var (code, output, _) = Run("html", file, "--fragment");
        Assert.Equal(0, code);
        Assert.DoesNotContain("<!DOCTYPE html>", output);
        Assert.Contains("<h1>Title</h1>", output);
    }

    [Fact]
    public void Html_WritesToOutputFile()
    {
        var file = Write("doc.md", "# Hi");
        var outPath = Path.Combine(_temp, "out.html");
        var (code, _, _) = Run("html", file, "-o", outPath);
        Assert.Equal(0, code);
        Assert.Contains("<h1>Hi</h1>", File.ReadAllText(outPath));
    }

    [Fact]
    public void Stats_CountsWordsAndChars()
    {
        var file = Write("doc.md", "one two three");
        var (code, output, _) = Run("stats", file);
        Assert.Equal(0, code);
        Assert.Contains("words\t3", output);
        Assert.Contains("characters\t13", output);
    }

    [Fact]
    public void Outline_ListsHeadings()
    {
        var file = Write("doc.md", "# Top\n\n## Sub\n\ntext\n\n## Sub2");
        var (code, output, _) = Run("outline", file);
        Assert.Equal(0, code);
        Assert.Equal("Top\n  Sub\n  Sub2\n", output);
    }

    [Fact]
    public void Tokens_DumpsStream()
    {
        var file = Write("doc.md", "**bold**");
        var (code, output, _) = Run("tokens", file);
        Assert.Equal(0, code);
        Assert.Contains("Strong", output);
        Assert.Contains("Marker", output);
    }

    [Fact]
    public void Cat_RoundTripsSource()
    {
        var src = "# Title\n\nbody text\n";
        var file = Write("doc.md", src);
        var (code, output, _) = Run("cat", file);
        Assert.Equal(0, code);
        Assert.Equal(src, output);
    }

    [Fact]
    public void MissingFile_ReportsCleanError()
    {
        var (code, _, err) = Run("stats", Path.Combine(_temp, "nope.md"));
        Assert.Equal(1, code);
        Assert.Contains("typage:", err);
        Assert.Contains("File not found", err);
    }

    // ---- `typage <file>` — the launcher at the root of the verb set --------
    // Launching the GUI itself can't be asserted in a headless container; what is asserted is everything
    // that has to be decided before anything launches.

    [Fact]
    public void Launching_a_file_that_does_not_exist_is_refused_before_anything_launches()
    {
        var (code, _, err) = Run(Path.Combine(_temp, "nope.md"));
        Assert.Equal(1, code);
        Assert.Contains("does not exist", err, StringComparison.Ordinal);
    }

    [Fact]
    public void An_argument_that_is_not_a_document_names_both_readings()
    {
        // Far more likely a mistyped verb than a file, so the error says so rather than letting the app
        // open and put up its own dialog.
        var (code, _, err) = Run("statz");
        Assert.Equal(1, code);
        Assert.Contains("isn't a document Typage opens", err, StringComparison.Ordinal);
        Assert.Contains("typage --help", err, StringComparison.Ordinal);
    }

    [Fact]
    public void Bare_typage_still_prints_help_rather_than_launching()
    {
        // This is a toolkit first and a launcher second: the verb list is what the bare name came for.
        var (code, output, _) = Run();
        Assert.Equal(0, code);
        Assert.Contains("Usage", output, StringComparison.Ordinal);
        Assert.Contains("outline", output, StringComparison.Ordinal);
    }
}
