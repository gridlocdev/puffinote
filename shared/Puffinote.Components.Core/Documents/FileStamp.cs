namespace Puffinote.Components.Core.Documents;

/// <summary>
/// What a file looked like the last time an app read or wrote it — the baseline somebody else's write is
/// noticed against. Two cheap facts rather than a hash: a length and a last-write time answer "did this
/// change?" without re-reading the bytes of a document that may be tens of megabytes, and every write
/// that matters moves at least one of them.
/// <para>
/// A stamp is not proof. Two writes inside the filesystem's timestamp resolution that happen to produce
/// the same length look identical, and an app that can compare content cheaply should (Typage compares
/// the decoded text). It is the right tool where the document is a zip, a large log, or JSON somebody
/// else's tool rewrote — everywhere re-reading to find out costs more than the answer is worth.
/// </para>
/// </summary>
public readonly record struct FileStamp(long Length, DateTime LastWriteUtc)
{
    /// <summary>The stamp of <paramref name="path"/> right now, or null when it is missing or unreadable.</summary>
    public static FileStamp? For(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Watching a file is never allowed to be the thing that breaks editing it.
            return null;
        }
    }
}
