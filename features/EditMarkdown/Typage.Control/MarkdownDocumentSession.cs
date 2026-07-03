using System.Threading.Tasks;
using Typage.Engine.Document;

namespace Typage.Control;

/// <summary>
/// A reusable file-host seam for the Markdown editor: it binds a document's on-disk <see cref="CurrentPath"/>
/// and <see cref="Format"/> (encoding/BOM) to the text a <see cref="Views.MarkdownEditorView"/> shows, and
/// round-trips it <b>byte-faithfully</b> through the engine's pure <see cref="TextFileCodec"/> with an
/// <b>atomic</b> (temp-then-rename) save (PRD §7.1, golden rule #5).
/// <para>
/// It lives in the editor library — <em>not</em> the engine (which stays filesystem-free so the host owns
/// I/O), and <em>not</em> forked into a host — so any shell drives the same load/save behaviour: the
/// standalone <c>Typage.Desktop</c> or the Puffinote notes shell. This is the Typage counterpart to
/// <c>Cellestra.Editor</c>'s <c>IEditorHost</c>. Line endings (LF/CRLF) survive untouched because the text
/// is round-tripped verbatim; only the encoding is reapplied.
/// </para>
/// </summary>
public sealed class MarkdownDocumentSession
{
    /// <summary>The absolute path of the open file, or null before the first load / save-as.</summary>
    public string? CurrentPath { get; private set; }

    /// <summary>The encoding/BOM the file was read with, reapplied on save. Defaults to UTF-8 (no BOM).</summary>
    public TextFileFormat Format { get; private set; } = TextFileFormat.Utf8NoBom;

    public bool HasPath => CurrentPath is not null;

    /// <summary>Read <paramref name="path"/>, remembering its encoding for a round-trip save; returns the decoded text.</summary>
    public string Load(string path)
    {
        var (text, format) = TextFileCodec.Decode(File.ReadAllBytes(path));
        Format = format;
        CurrentPath = Path.GetFullPath(path);
        return text;
    }

    /// <summary>Save <paramref name="text"/> to <see cref="CurrentPath"/> (byte-faithful, atomic). Throws if no path is set.</summary>
    public void Save(string text)
    {
        if (CurrentPath is null)
        {
            throw new InvalidOperationException("No current file path; call SaveAs first.");
        }

        AtomicWrite(CurrentPath, TextFileCodec.Encode(text, Format));
    }

    /// <summary>Point the session at <paramref name="path"/> and save <paramref name="text"/> there.</summary>
    public void SaveAs(string path, string text)
    {
        CurrentPath = Path.GetFullPath(path);
        AtomicWrite(CurrentPath, TextFileCodec.Encode(text, Format));
    }

    /// <summary>
    /// Rebind to <paramref name="path"/> after the file was moved/renamed on disk, without any I/O — the
    /// bytes already live at the new path, so a host reconciling an external rename only updates where the
    /// next <see cref="Save"/> writes. The document text, format, and dirty state are untouched.
    /// </summary>
    public void Rebind(string path) => CurrentPath = Path.GetFullPath(path);

    // Async wrappers for hosts that don't want to block the UI thread on I/O. (Note-sized files are small,
    // so the sync methods above are fine on the UI thread; these exist for symmetry and larger files.)
    public Task<string> LoadAsync(string path) => Task.Run(() => Load(path));

    public Task SaveAsync(string text) => Task.Run(() => Save(text));

    // Atomic on the same volume: write a sibling temp file then rename over the target (PRD §7.1). Mirrors
    // Typage.Desktop's proven AtomicWrite so the behaviour is identical wherever the session is hosted.
    private static void AtomicWrite(string path, byte[] content)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? ".";
        var temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, content);
            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            throw;
        }
    }
}
