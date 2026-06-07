using System;
using System.IO;
using Cellestra.Desktop.Services;
using Xunit;

namespace Cellestra.UiTests;

public class FileLockTests
{
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
        string dir = Path.Combine(Path.GetTempPath(), "cellestra-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string doc = Path.Combine(dir, "book.ods");
            Assert.Null(FileLock.Inspect(doc)); // unlocked

            var heldLock = FileLock.TryAcquire(doc);
            Assert.NotNull(heldLock);
            Assert.True(File.Exists(FileLock.LockPathFor(doc)));

            var info = FileLock.Inspect(doc);
            Assert.NotNull(info);
            Assert.Equal(Environment.UserName, info!.User);
            Assert.Equal(Environment.MachineName, info.Host);

            heldLock!.Dispose();
            Assert.False(File.Exists(FileLock.LockPathFor(doc)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Acquire_returns_null_when_already_locked()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cellestra-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string doc = Path.Combine(dir, "book.ods");
            // A foreign lock, e.g. one LibreOffice left behind.
            File.WriteAllText(FileLock.LockPathFor(doc), "alice,imac,01.01.2026 09:00,,LibreOffice;");

            Assert.Null(FileLock.TryAcquire(doc));
            Assert.Equal("alice", FileLock.Inspect(doc)!.User);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
