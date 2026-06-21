using System;
using System.Globalization;
using System.IO;

namespace Cellestra.Desktop.Services;

/// <summary>Who holds a document lock, parsed from a LibreOffice <c>.~lock</c> file.</summary>
public sealed record LockInfo(string User, string Host, string LockPath);

/// <summary>
/// Co-operative document locking compatible with LibreOffice's hidden <c>.~lock.&lt;name&gt;#</c>
/// files. We detect an existing lock (so we can warn before opening), and place our own while a
/// file is open so LibreOffice — or another Cellestra window — warns its user in turn. This is
/// advisory only: it relies on every editor honoring the same convention.
/// </summary>
public sealed class FileLock : IDisposable
{
    private bool _owned;

    private FileLock(string documentPath, string lockPath)
    {
        DocumentPath = documentPath;
        LockPath = lockPath;
        _owned = true;
    }

    public string DocumentPath { get; }

    public string LockPath { get; }

    /// <summary>The lock-file path LibreOffice would use for a document: <c>dir/.~lock.name#</c>.</summary>
    public static string LockPathFor(string documentPath)
    {
        string full = Path.GetFullPath(documentPath);
        string dir = Path.GetDirectoryName(full) ?? ".";
        return Path.Combine(dir, ".~lock." + Path.GetFileName(full) + "#");
    }

    /// <summary>The holder of an existing lock on the document, or null when it is unlocked.</summary>
    public static LockInfo? Inspect(string documentPath)
    {
        string lockPath = LockPathFor(documentPath);
        if (!File.Exists(lockPath))
        {
            return null;
        }

        try
        {
            // LibreOffice writes a single comma-separated line ending in ';':
            // user,host,timestamp,...;
            string content = File.ReadAllText(lockPath).Trim().TrimEnd(';');
            string[] fields = content.Split(',');
            string user = fields.Length > 0 ? fields[0].Trim() : string.Empty;
            string host = fields.Length > 1 ? fields[1].Trim() : string.Empty;
            return new LockInfo(user, host, lockPath);
        }
        catch (IOException)
        {
            return new LockInfo(string.Empty, string.Empty, lockPath);
        }
    }

    /// <summary>
    /// Place our own lock on the document. Returns null if a lock already exists (caller should
    /// have inspected and decided to proceed) or if the directory is not writable.
    /// </summary>
    public static FileLock? TryAcquire(string documentPath)
    {
        string lockPath = LockPathFor(documentPath);
        if (File.Exists(lockPath))
        {
            return null;
        }

        try
        {
            string stamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            File.WriteAllText(lockPath, $"{Environment.UserName},{Environment.MachineName},{stamp},,Cellestra;");
            return new FileLock(documentPath, lockPath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Remove our lock file. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (!_owned)
        {
            return;
        }

        _owned = false;
        try
        {
            if (File.Exists(LockPath))
            {
                File.Delete(LockPath);
            }
        }
        catch (IOException)
        {
            // Best-effort; a stale lock is preferable to crashing on close.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
