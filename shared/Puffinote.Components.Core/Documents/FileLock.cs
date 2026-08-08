using System.Diagnostics;
using System.Globalization;

namespace Puffinote.Components.Core.Documents;

/// <summary>
/// Who holds a document lock, parsed from a LibreOffice <c>.~lock</c> file.
/// <paramref name="IsStale"/> is true only for a lock this family wrote, on this machine, whose
/// process is demonstrably gone — a leftover, not a live claim.
/// </summary>
public sealed record LockInfo(string User, string Host, string Application, string LockPath, bool IsStale);

/// <summary>
/// Co-operative document locking compatible with LibreOffice's hidden <c>.~lock.&lt;name&gt;#</c>
/// files. We detect an existing lock (so we can warn before opening), and place our own while a
/// file is open so LibreOffice — or another window in this family — warns its user in turn. This is
/// advisory only: it relies on every editor honoring the same convention.
/// </summary>
/// <remarks>
/// A lock file is a promise to remove it, and a process that is killed cannot keep a promise. So the
/// line we write carries a sixth field naming the process that wrote it — <c>Puffinote:&lt;pid&gt;@&lt;start
/// ticks&gt;</c> — and <see cref="TryAcquire"/> reclaims a lock whose owner is no longer running. Without
/// that, one force-quit would lock a spreadsheet out of this whole family permanently, which is exactly
/// the "other applications can't open my file any more" this locking is supposed to prevent. The start
/// time is there because process ids are recycled: a stale 4321 and a live, unrelated 4321 are otherwise
/// indistinguishable, and reclaiming the second would delete a lock somebody is relying on.
///
/// The extra field is additive on purpose. LibreOffice reads this format positionally and ignores what
/// it doesn't recognise, so a lock of ours still reads correctly there; conversely a lock with no sixth
/// field is somebody else's and is never reclaimed, however old it looks. Releasing on the way out is
/// <see cref="DocumentLocks"/>'s job — hold locks through it, not directly, or nothing will clean up
/// after a signal.
/// </remarks>
public sealed class FileLock : IDisposable
{
    /// <summary>Marks the sixth field as ours, so a stray field from another writer is never parsed as one.</summary>
    private const string OwnerPrefix = "Puffinote:";

    /// <summary>
    /// Slack when matching a process start time. The value is re-read from the OS rather than from what
    /// we wrote, and the two are derived from boot time and clock ticks — equal in practice, but not
    /// worth betting a lock's identity on exact equality.
    /// </summary>
    private static readonly TimeSpan StartTimeSlack = TimeSpan.FromSeconds(2);

    private FileLock(string documentPath, string lockPath)
    {
        DocumentPath = documentPath;
        LockPath = lockPath;
        IsHeld = true;
    }

    /// <summary>The document this lock covers, as a full path.</summary>
    public string DocumentPath { get; }

    /// <summary>The lock file backing it.</summary>
    public string LockPath { get; }

    /// <summary>False once released, so a registry can drop it without a second delete.</summary>
    public bool IsHeld { get; private set; }

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
            // user,host,timestamp,ip,app; — and we append our owner token as a sixth field.
            string content = File.ReadAllText(lockPath).Trim().TrimEnd(';');
            string[] fields = content.Split(',');
            string user = Field(fields, 0);
            string host = Field(fields, 1);
            string application = Field(fields, 4);
            return new LockInfo(user, host, application, lockPath, OwnerIsGone(host, Field(fields, 5)));
        }
        catch (IOException)
        {
            // Unreadable is not the same as absent: something is there, so treat it as a live claim.
            return new LockInfo(string.Empty, string.Empty, string.Empty, lockPath, false);
        }
    }

    /// <summary>
    /// Place our own lock on the document, naming <paramref name="application"/> as the holder. Returns
    /// null when a live lock already exists (the caller should have inspected and decided to proceed),
    /// when another process won the race to create it, or when the directory is not writable. A lock
    /// left behind by one of our own processes that is no longer running is reclaimed rather than
    /// refused.
    /// </summary>
    public static FileLock? TryAcquire(string documentPath, string application)
    {
        string lockPath = LockPathFor(documentPath);
        if (File.Exists(lockPath))
        {
            if (Inspect(documentPath) is not { IsStale: true })
            {
                return null;
            }

            try
            {
                File.Delete(lockPath);
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

        try
        {
            // CreateNew, not WriteAllText: two processes can both find a stale lock and both decide to
            // reclaim it, and the one that loses that race must be told, not silently handed a lock
            // somebody else now holds.
            using (var stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(Line(application));
            }

            return new FileLock(Path.GetFullPath(documentPath), lockPath);
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
        if (!IsHeld)
        {
            return;
        }

        IsHeld = false;
        try
        {
            if (File.Exists(LockPath))
            {
                File.Delete(LockPath);
            }
        }
        catch (IOException)
        {
            // Best-effort; a stale lock is preferable to crashing on close — and a stale lock of ours is
            // reclaimable, which is the whole point of the owner token.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Field(string[] fields, int index) =>
        index < fields.Length ? fields[index].Trim() : string.Empty;

    private static string Line(string application)
    {
        string stamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        using var self = Process.GetCurrentProcess();
        string owner = string.Create(
            CultureInfo.InvariantCulture,
            $"{OwnerPrefix}{Environment.ProcessId}@{self.StartTime.Ticks}");
        return $"{Environment.UserName},{Environment.MachineName},{stamp},,{application},{owner};";
    }

    /// <summary>
    /// Whether the process that wrote this lock has gone without removing it. Every "can't tell" answer
    /// is "no": refusing to open a document is an inconvenience, deleting a lock somebody is editing
    /// behind is data loss.
    /// </summary>
    private static bool OwnerIsGone(string host, string ownerToken)
    {
        if (!ownerToken.StartsWith(OwnerPrefix, StringComparison.Ordinal))
        {
            return false; // LibreOffice's, or some other editor's — never ours to clean up
        }

        if (!string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return false; // written from another machine over a share; its process list is not ours to read
        }

        if (!TryParseOwner(ownerToken, out int pid, out long startTicks))
        {
            return false;
        }

        // Deliberately no "but that's my own pid" shortcut: the start-time comparison below answers that
        // case correctly on its own (a lock we wrote matches, since both sides read the same clock), and
        // the shortcut would misjudge the one case it is meant to cover — a lock left by a dead process
        // whose id this one has since been given.
        try
        {
            using var owner = Process.GetProcessById(pid);
            return Math.Abs(owner.StartTime.Ticks - startTicks) > StartTimeSlack.Ticks; // id recycled
        }
        catch (ArgumentException)
        {
            return true; // no process with that id — the owner is gone
        }
        catch (InvalidOperationException)
        {
            return true; // it exited between the lookup and the question
        }
        catch (SystemException)
        {
            return false; // we may not query it (another user's process) — assume it is alive
        }
    }

    private static bool TryParseOwner(string ownerToken, out int pid, out long startTicks)
    {
        pid = 0;
        startTicks = 0;
        string body = ownerToken[OwnerPrefix.Length..];
        int at = body.IndexOf('@', StringComparison.Ordinal);
        return at > 0
            && int.TryParse(body[..at], NumberStyles.None, CultureInfo.InvariantCulture, out pid)
            && long.TryParse(body[(at + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out startTicks);
    }
}
