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
/// <para><b>The format is exactly LibreOffice's, five fields and no more</b>: OpenOffice user name,
/// system user name, host, edit time, and the writer's "user URL", comma-separated and closed with a
/// <c>;</c>, with <c>\</c>, <c>,</c> and <c>;</c> backslash-escaped inside a field. We used to append a
/// sixth field of our own, on the assumption that LibreOffice reads the line positionally and ignores
/// what it doesn't recognise. It does not: it parses the buffer as a <i>list</i> of five-field records,
/// so a sixth field starts a second record that then runs out of fields — and LibreOffice greets the
/// file with "The lock file is corrupted and probably empty", offering only to open the document
/// read-only. Opening a Cellestra-locked file in LibreOffice hit that every time; the reverse worked,
/// because reading a lock is forgiving in a way parsing one is not.</para>
///
/// <para>A lock file is also a promise to remove it, and a process that is killed cannot keep a promise.
/// So the token naming the writer — <c>Puffinote:&lt;app&gt;:&lt;pid&gt;@&lt;start ticks&gt;</c> — rides in
/// the <b>user-URL</b> field, which LibreOffice treats as an opaque string it only compares against its
/// own. <see cref="TryAcquire"/> reclaims a lock whose owner is demonstrably no longer running; without
/// that, one force-quit would lock a spreadsheet out of this whole family permanently, which is exactly
/// the "other applications can't open my file any more" this locking exists to prevent. The start time is
/// there because process ids are recycled: a stale 4321 and a live, unrelated 4321 are otherwise
/// indistinguishable, and reclaiming the second would delete a lock somebody is relying on. A lock whose
/// user-URL is not one of ours is somebody else's and is never reclaimed, however old it looks.</para>
///
/// <para>Releasing on the way out is <see cref="DocumentLocks"/>'s job — hold locks through it, not
/// directly, or nothing will clean up after a signal.</para>
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
            // One record of five fields: ooo-user, system-user, host, edit time, user URL — the last of
            // which carries our own token when the lock is one of ours (see the remarks above).
            string[] fields = ParseRecord(File.ReadAllText(lockPath));
            string user = Field(fields, OooUserField) is { Length: > 0 } named ? named : Field(fields, SystemUserField);
            string host = Field(fields, HostField);
            string owner = Field(fields, UserUrlField);
            return new LockInfo(user, host, ApplicationOf(owner), lockPath, OwnerIsGone(host, owner));
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

    // LibreOffice's field order (svl's LockFileCommon): everything that reads or writes one of these
    // lines names its fields from here, so the order is stated once.
    private const int OooUserField = 0;
    private const int SystemUserField = 1;
    private const int HostField = 2;
    private const int EditTimeField = 3;
    private const int UserUrlField = 4;
    private const int FieldCount = 5;

    private static string Field(string[] fields, int index) =>
        index < fields.Length ? fields[index].Trim() : string.Empty;

    private static string Line(string application)
    {
        string stamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        using var self = Process.GetCurrentProcess();
        string owner = string.Create(
            CultureInfo.InvariantCulture,
            $"{OwnerPrefix}{application}:{Environment.ProcessId}@{self.StartTime.Ticks}");

        var fields = new string[FieldCount];
        fields[OooUserField] = Environment.UserName;
        fields[SystemUserField] = Environment.UserName;
        fields[HostField] = Environment.MachineName;
        fields[EditTimeField] = stamp;
        fields[UserUrlField] = owner;
        return string.Join(',', fields.Select(Escape)) + ";";
    }

    /// <summary>Escape a field the way LibreOffice does: <c>\</c>, <c>,</c> and <c>;</c> carry a backslash,
    /// so a user or host name containing one can't split the record.</summary>
    private static string Escape(string field) =>
        field.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace(",", "\\,", StringComparison.Ordinal)
             .Replace(";", "\\;", StringComparison.Ordinal);

    /// <summary>
    /// The fields of the first record in <paramref name="content"/>, unescaped. Splitting on bare commas
    /// would be wrong for a name written by LibreOffice with an escaped one in it, and the terminating
    /// <c>;</c> ends the record even when a file holds several.
    /// </summary>
    private static string[] ParseRecord(string content)
    {
        var fields = new List<string>(FieldCount);
        var field = new System.Text.StringBuilder();
        for (var i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (c == '\\' && i + 1 < content.Length)
            {
                field.Append(content[++i]);
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == ';' || c == '\n' || c == '\r')
            {
                break;
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return [.. fields];
    }

    /// <summary>The application named in one of our own owner tokens; empty for anybody else's lock (a
    /// LibreOffice lock's user URL is a profile URL, and claiming that as an app name would be a lie).</summary>
    private static string ApplicationOf(string ownerToken)
    {
        if (!ownerToken.StartsWith(OwnerPrefix, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        string body = ownerToken[OwnerPrefix.Length..];
        int colon = body.LastIndexOf(':');
        return colon > 0 ? body[..colon] : string.Empty;
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

    /// <summary>Read the <c>&lt;app&gt;:&lt;pid&gt;@&lt;start ticks&gt;</c> body of one of our owner tokens.</summary>
    private static bool TryParseOwner(string ownerToken, out int pid, out long startTicks)
    {
        pid = 0;
        startTicks = 0;
        string body = ownerToken[OwnerPrefix.Length..];
        int at = body.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0)
        {
            return false;
        }

        // The application name may itself hold a colon, so the *last* one before the '@' opens the pid.
        string head = body[..at];
        int colon = head.LastIndexOf(':');
        string pidText = colon >= 0 ? head[(colon + 1)..] : head;
        return int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out pid)
            && long.TryParse(body[(at + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out startTicks);
    }
}
