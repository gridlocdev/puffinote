using System.IO.Compression;
using System.Text;

namespace Cellestra.Core.Serialization.Ods;

/// <summary>An unmodeled child of <c>&lt;office:spreadsheet&gt;</c>, kept verbatim (PRD §8.1).</summary>
public sealed record ForeignXml(bool BeforeTables, string Xml);

/// <summary>
/// The <c>.ods</c> ZIP container. Enforces the ODF rule that <c>mimetype</c> is the
/// <b>first</b> entry and <b>stored uncompressed</b> (Golden Rule #7). Holds every part as
/// bytes so parts Cellestra doesn't model are preserved verbatim on save (PRD §8.1).
/// </summary>
public sealed class OdsPackage
{
    private readonly Dictionary<string, byte[]> _parts = new(StringComparer.Ordinal);

    /// <summary>Unmodeled <c>&lt;office:spreadsheet&gt;</c> children captured on read, re-emitted on write (PRD §8.1).</summary>
    public List<ForeignXml> ForeignSpreadsheetChildren { get; } = [];

    public IReadOnlyCollection<string> PartNames => _parts.Keys;

    public bool Contains(string name) => _parts.ContainsKey(name);

    public byte[]? GetBytes(string name) => _parts.TryGetValue(name, out var bytes) ? bytes : null;

    public string? GetText(string name) => _parts.TryGetValue(name, out var bytes)
        ? new UTF8Encoding(false).GetString(bytes)
        : null;

    public void SetText(string name, string content) =>
        _parts[name] = new UTF8Encoding(false).GetBytes(content);

    public void SetBytes(string name, byte[] content) => _parts[name] = content;

    /// <summary>The parts that are NOT regenerated on save — preserved as-is (charts, images, etc.).</summary>
    public IEnumerable<string> PreservedParts =>
        _parts.Keys.Where(name => !OdsXmlNames.GeneratedParts.Contains(name));

    public static OdsPackage Read(Stream stream)
    {
        var package = new OdsPackage();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
            {
                continue; // directory entry
            }

            using var entryStream = entry.Open();
            using var memory = new MemoryStream();
            entryStream.CopyTo(memory);
            package._parts[entry.FullName] = memory.ToArray();
        }

        return package;
    }

    public void Write(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        // The mimetype MUST be first and stored (uncompressed), with the exact bytes.
        WriteStored(archive, OdsXmlNames.MimetypePart, new UTF8Encoding(false).GetBytes(OdsXmlNames.MimeType));

        foreach (var (name, bytes) in _parts)
        {
            if (name == OdsXmlNames.MimetypePart)
            {
                continue;
            }

            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            entryStream.Write(bytes, 0, bytes.Length);
        }
    }

    private static void WriteStored(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var entryStream = entry.Open();
        entryStream.Write(bytes, 0, bytes.Length);
    }
}
