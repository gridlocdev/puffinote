using System.Globalization;
using Puffinote.Components.Core.Documents;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Cellestra is the family's only lock holder, so the shared <see cref="FileLock"/> and the quit-time
/// sweep in <see cref="DocumentLocks"/> are covered from here (the pattern Puffinote.Components already
/// uses: shared code is exercised by whichever area actually hosts it).
/// </summary>
public sealed class FileLockTests : IDisposable
{
    /// <summary>A process id no operating system will have handed out — the "owner is gone" case.</summary>
    private const int DeadPid = int.MaxValue;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cellestra-lock-" + Guid.NewGuid().ToString("N"));

    public FileLockTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        DocumentLocks.ReleaseAll(); // never leave a lock behind for the next test in this process
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Lock_path_follows_the_libreoffice_convention()
    {
        string doc = Path.Combine(Path.GetTempPath(), "Budget.ods");
        Assert.Equal(
            Path.Combine(Path.GetTempPath(), ".~lock.Budget.ods#"),
            FileLock.LockPathFor(doc));
    }

    [Fact]
    public void Acquire_creates_a_lock_and_dispose_removes_it()
    {
        string doc = Doc();
        Assert.Null(FileLock.Inspect(doc)); // unlocked

        var heldLock = FileLock.TryAcquire(doc, "Cellestra");
        Assert.NotNull(heldLock);
        Assert.True(File.Exists(FileLock.LockPathFor(doc)));

        var info = FileLock.Inspect(doc);
        Assert.NotNull(info);
        Assert.Equal(Environment.UserName, info!.User);
        Assert.Equal(Environment.MachineName, info.Host);
        Assert.Equal("Cellestra", info.Application);
        Assert.False(info.IsStale); // the process that wrote it is this one, and it is very much alive

        heldLock!.Dispose();
        Assert.False(File.Exists(FileLock.LockPathFor(doc)));
    }

    [Fact]
    public void The_line_we_write_is_the_one_libreoffice_parses()
    {
        // A record of exactly five fields, closed with ';'. LibreOffice parses the buffer as a *list* of
        // five-field records, so a sixth field opens a second one that then runs out — which is what had
        // it greeting a Cellestra-locked file with "the lock file is corrupted and probably empty" and
        // offering nothing but read-only.
        string doc = Doc();
        using var held = FileLock.TryAcquire(doc, "Cellestra");

        string line = File.ReadAllText(FileLock.LockPathFor(doc));
        Assert.EndsWith(";", line, StringComparison.Ordinal);
        Assert.Equal(5, line.TrimEnd(';').Split(',').Length);

        // And in LibreOffice's own field order: user, user, host, edit time, user URL.
        var fields = line.TrimEnd(';').Split(',');
        Assert.Equal(Environment.UserName, fields[0]);
        Assert.Equal(Environment.UserName, fields[1]);
        Assert.Equal(Environment.MachineName, fields[2]);
        Assert.Matches(@"^\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}$", fields[3]);
        Assert.StartsWith("Puffinote:Cellestra:", fields[4], StringComparison.Ordinal);
    }

    [Fact]
    public void Acquire_returns_null_when_already_locked()
    {
        string doc = Doc();
        // A foreign lock, in the shape LibreOffice actually writes: the fifth field is its user URL.
        File.WriteAllText(FileLock.LockPathFor(doc), "Alice A,alice,imac,01.01.2026 09:00,file:///Users/alice/;");

        Assert.Null(FileLock.TryAcquire(doc, "Cellestra"));
        var info = FileLock.Inspect(doc)!;
        Assert.Equal("Alice A", info.User);
        Assert.Equal("imac", info.Host);
        Assert.Equal(string.Empty, info.Application); // a profile URL is not an application name
    }

    [Fact]
    public void A_name_with_a_comma_in_it_stays_one_field()
    {
        string doc = Doc();
        File.WriteAllText(FileLock.LockPathFor(doc), @"Alice\, A,alice,imac,01.01.2026 09:00,file:///Users/alice/;");

        Assert.Equal("Alice, A", FileLock.Inspect(doc)!.User);
    }

    [Fact]
    public void A_foreign_lock_is_never_stale_however_old_it_looks()
    {
        string doc = Doc();
        File.WriteAllText(FileLock.LockPathFor(doc), "Alice A,alice,imac,01.01.1999 09:00,file:///Users/alice/;");

        // No owner token means we cannot know whether the writer is alive, and guessing wrong deletes a
        // lock somebody is editing behind. Age is not evidence.
        Assert.False(FileLock.Inspect(doc)!.IsStale);
        Assert.Null(FileLock.TryAcquire(doc, "Cellestra"));
    }

    [Fact]
    public void A_lock_left_by_a_dead_process_is_reclaimed()
    {
        string doc = Doc();
        WriteOurLock(doc, DeadPid, startTicks: 12345);

        Assert.True(FileLock.Inspect(doc)!.IsStale);

        // The point of the whole exercise: a killed Cellestra must not lock the document out for good.
        var reclaimed = FileLock.TryAcquire(doc, "Cellestra");
        Assert.NotNull(reclaimed);
        Assert.Equal(Environment.UserName, FileLock.Inspect(doc)!.User);
        reclaimed!.Dispose();
    }

    [Fact]
    public void A_lock_naming_a_recycled_process_id_is_reclaimed()
    {
        string doc = Doc();
        // This process's id, but a start time that cannot be ours — i.e. a dead process that happened to
        // hold the id we were later given. Without the start time this would read as "still running".
        WriteOurLock(doc, Environment.ProcessId, startTicks: 1);

        Assert.True(FileLock.Inspect(doc)!.IsStale);
    }

    [Fact]
    public void A_lock_from_another_machine_is_left_alone()
    {
        string doc = Doc();
        // Same shape as ours, dead pid — but written on a different host over a share, where our process
        // table says nothing about theirs.
        File.WriteAllText(
            FileLock.LockPathFor(doc),
            $"Alice A,alice,some-other-mac,01.01.2026 09:00,Puffinote:Cellestra:{DeadPid}@12345;");

        Assert.False(FileLock.Inspect(doc)!.IsStale);
        Assert.Null(FileLock.TryAcquire(doc, "Cellestra"));
    }

    [Fact]
    public void Releasing_all_locks_removes_every_lock_file()
    {
        string one = Doc("one.ods");
        string two = Doc("two.ods");

        DocumentLocks.Install("Cellestra");
        Assert.NotNull(DocumentLocks.TryAcquire(one));
        Assert.NotNull(DocumentLocks.TryAcquire(two));
        Assert.Equal(2, DocumentLocks.HeldCount);

        // This is what every exit path ends up calling — the window close, ⌘Q, SIGTERM, ProcessExit.
        DocumentLocks.ReleaseAll();

        Assert.Equal(0, DocumentLocks.HeldCount);
        Assert.False(File.Exists(FileLock.LockPathFor(one)));
        Assert.False(File.Exists(FileLock.LockPathFor(two)));
    }

    [Fact]
    public void Releasing_all_locks_twice_is_harmless()
    {
        string doc = Doc();
        DocumentLocks.Install("Cellestra");
        Assert.NotNull(DocumentLocks.TryAcquire(doc));

        // The exit paths overlap — a signal handler can run while the lifetime is already shutting down.
        DocumentLocks.ReleaseAll();
        DocumentLocks.ReleaseAll();

        Assert.Equal(0, DocumentLocks.HeldCount);
        Assert.False(File.Exists(FileLock.LockPathFor(doc)));
    }

    [Fact]
    public void Releasing_one_lock_stops_tracking_it()
    {
        string doc = Doc();
        DocumentLocks.Install("Cellestra");
        var held = DocumentLocks.TryAcquire(doc);

        DocumentLocks.Release(held); // the document was closed, or swapped for another
        Assert.Equal(0, DocumentLocks.HeldCount);
        Assert.False(File.Exists(FileLock.LockPathFor(doc)));

        DocumentLocks.Release(null); // callers shouldn't have to guard
        DocumentLocks.ReleaseAll();
    }

    [Fact]
    public void A_reclaimed_stale_lock_is_tracked_for_the_quit_sweep()
    {
        string doc = Doc();
        WriteOurLock(doc, DeadPid, startTicks: 12345);

        DocumentLocks.Install("Cellestra");
        Assert.NotNull(DocumentLocks.TryAcquire(doc));
        Assert.Equal(1, DocumentLocks.HeldCount);

        // Taking a lock over must not also inherit the habit that left it behind.
        DocumentLocks.ReleaseAll();
        Assert.False(File.Exists(FileLock.LockPathFor(doc)));
    }

    private string Doc(string name = "book.ods") => Path.Combine(_dir, name);

    /// <summary>Write a lock in our own format, attributed to a process of the test's choosing.</summary>
    private static void WriteOurLock(string documentPath, int pid, long startTicks) =>
        File.WriteAllText(
            FileLock.LockPathFor(documentPath),
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Environment.UserName},{Environment.UserName},{Environment.MachineName},01.01.2026 09:00,Puffinote:Cellestra:{pid}@{startTicks};"));
}
