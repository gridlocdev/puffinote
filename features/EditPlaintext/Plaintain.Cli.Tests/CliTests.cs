using System.Text;
using Xunit;

namespace Plaintain.Cli.Tests;

/// <summary>
/// `plaintain info` end to end. One class, run sequentially, because these redirect the process-global
/// <see cref="Console"/> — the same arrangement the sibling CLIs' tests use.
/// </summary>
[Collection("cli")]
public sealed class CliTests : IDisposable
{
    private readonly DirectoryInfo _scratch = Directory.CreateTempSubdirectory("plaintain-tests");
    private readonly TextWriter _stdout = Console.Out;
    private readonly TextWriter _stderr = Console.Error;

    public void Dispose()
    {
        Console.SetOut(_stdout);
        Console.SetError(_stderr);
        _scratch.Delete(recursive: true);
    }

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_scratch.FullName, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static (int Exit, string Out) Run(params string[] args)
    {
        var output = new StringWriter();
        Console.SetOut(output);
        int exit = CliApplication.Run(args);
        return (exit, output.ToString());
    }

    [Fact]
    public void Info_reports_what_a_plain_utf8_file_is_made_of()
    {
        string path = Write("notes.txt", Encoding.UTF8.GetBytes("alpha beta\ngamma\n"));
        var (exit, text) = Run("info", path);

        Assert.Equal(0, exit);
        Assert.Contains("encoding:      UTF-8", text, StringComparison.Ordinal);
        Assert.Contains("line endings:  LF", text, StringComparison.Ordinal);
        Assert.Contains("words:         3", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Info_names_the_line_endings_it_found()
    {
        string path = Write("crlf.txt", Encoding.UTF8.GetBytes("a\r\nb\r\n"));
        var (_, text) = Run("info", path);

        Assert.Contains("line endings:  CRLF", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Info_says_when_the_encoding_was_guessed_and_exits_non_zero()
    {
        // The one thing a script wants to branch on: this file is not text we can vouch for.
        string path = Write("latin1.txt", Encoding.Latin1.GetBytes("café"));
        var (exit, text) = Run("info", path);

        Assert.Equal(2, exit);
        Assert.Contains("Latin-1", text, StringComparison.Ordinal);
        Assert.Contains("guessed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Info_keeps_the_byte_count_honest_for_a_bom()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("hi")];
        string path = Write("bom.txt", bytes);
        var (_, text) = Run("info", path);

        Assert.Contains("encoding:      UTF-8 BOM", text, StringComparison.Ordinal);
        Assert.Contains("bytes:         5", text, StringComparison.Ordinal); // the BOM is bytes too
    }

    [Fact]
    public void A_missing_file_is_a_clean_error_not_a_stack_trace()
    {
        var errors = new StringWriter();
        Console.SetError(errors);
        int exit = CliApplication.Run(["info", Path.Combine(_scratch.FullName, "nope.txt")]);

        Assert.Equal(1, exit);
        Assert.StartsWith("plaintain:", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_a_file_that_does_not_exist_is_refused_before_anything_launches()
    {
        var errors = new StringWriter();
        Console.SetError(errors);
        int exit = CliApplication.Run([Path.Combine(_scratch.FullName, "nope.txt")]);

        Assert.Equal(1, exit);
        Assert.Contains("does not exist", errors.ToString(), StringComparison.Ordinal);
    }
}
