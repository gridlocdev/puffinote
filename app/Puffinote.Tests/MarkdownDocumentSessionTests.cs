using System.Text;
using Typage.Control;
using Xunit;

namespace Puffinote.Tests;

/// <summary>
/// The Typage file-host seam Puffinote drives (ARCHITECTURE §4). Pure file round-trip — no Avalonia.
/// </summary>
public sealed class MarkdownDocumentSessionTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("puffinote-session");

    public void Dispose() => _dir.Delete(recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_dir.FullName, name);

    [Fact]
    public void Load_then_Save_round_trips_text_and_line_endings_verbatim()
    {
        var path = Path("notes.md");
        const string content = "# Title\r\nWindows line\r\nUnix line\nend"; // mixed CRLF/LF
        File.WriteAllText(path, content, new UTF8Encoding(false));

        var session = new MarkdownDocumentSession();
        var loaded = session.Load(path);
        Assert.Equal(content, loaded);

        session.Save(loaded); // save unchanged
        Assert.Equal(content, File.ReadAllText(path));
        // No BOM was added.
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
    }

    [Fact]
    public void Save_preserves_a_utf8_bom()
    {
        var path = Path("bom.md");
        var original = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("hello")).ToArray();
        File.WriteAllBytes(path, original);

        var session = new MarkdownDocumentSession();
        var text = session.Load(path);
        Assert.Equal("hello", text);

        session.Save("goodbye");

        var saved = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, saved.Take(3));
        Assert.Equal("goodbye", new UTF8Encoding(false).GetString(saved, 3, saved.Length - 3));
    }

    [Fact]
    public void Save_without_a_path_throws()
    {
        Assert.Throws<InvalidOperationException>(() => new MarkdownDocumentSession().Save("x"));
    }

    [Fact]
    public void Save_is_atomic_leaving_no_temp_files_behind()
    {
        var path = Path("clean.md");
        File.WriteAllText(path, "a");
        var session = new MarkdownDocumentSession();
        session.Load(path);

        session.Save("b");

        Assert.Equal("b", File.ReadAllText(path));
        Assert.Equal(["clean.md"], Directory.GetFiles(_dir.FullName).Select(System.IO.Path.GetFileName));
    }
}
