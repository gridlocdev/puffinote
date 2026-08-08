using System.Runtime.InteropServices;

namespace Puffinote.Components.Core.Documents;

/// <summary>
/// Every document lock this process holds, and the promise that none of them outlive it.
/// </summary>
/// <remarks>
/// A <see cref="FileLock"/> is a file on the user's disk that says "someone is editing this". Taking one
/// is easy; the hard part is that it has to be gone by the time the app is, down every way an app can
/// end — and a window's <c>Closing</c> handler, which is where all of this repo's teardown lives, only
/// covers one of them. Quitting from the dock, a <c>SIGTERM</c> at logout, <c>kill</c>, Ctrl+C on a
/// terminal-launched build, or an unhandled exception all end the process without closing a window, and
/// each one used to leave a lock file behind that nothing would ever remove: <see cref="FileLock.TryAcquire"/>
/// refuses an existing lock, so LibreOffice and Cellestra alike would report the document as busy forever.
///
/// So locks are held here rather than in a field somewhere, and released from every exit the OS lets us
/// observe. <c>SIGKILL</c> and a power cut are not among them, by definition — that is what
/// <see cref="FileLock"/>'s owner token is for, and the two halves are meant to be read together: this
/// class makes the common exits clean, and the token makes the uncleanable ones recoverable.
///
/// <see cref="ReleaseAll"/> is idempotent and safe to call from several of these at once, which matters
/// because they overlap — a signal handler runs on a thread-pool thread while the UI thread may still be
/// shutting down.
/// </remarks>
public static class DocumentLocks
{
    private static readonly Lock Gate = new();

    /// <summary>Locks held right now. Released entries are pruned as they are noticed.</summary>
    private static readonly List<FileLock> Held = [];

    /// <summary>Kept alive deliberately: a collected registration is an unregistered signal handler.</summary>
    private static readonly List<PosixSignalRegistration> Signals = [];

    private static string _application = "Puffinote";
    private static bool _installed;

    /// <summary>
    /// Name this process in the locks it writes, and arm the exit paths that release them. Call once at
    /// startup, before any document is opened; calling again only updates the name.
    /// </summary>
    public static void Install(string application)
    {
        lock (Gate)
        {
            _application = application;
            if (_installed)
            {
                return;
            }

            _installed = true;

            // Normal termination: the run loop ended and Main returned, or someone called Environment.Exit.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => ReleaseAll();

            // A crash still gets a chance to tidy up. The process is going down either way, so there is
            // nothing to lose by trying, and a lock left by a crash is the worst kind — the user has no
            // idea it is there.
            AppDomain.CurrentDomain.UnhandledException += (_, _) => ReleaseAll();

            // Signals the runtime lets us see. SIGTERM and SIGINT already run ProcessExit on .NET, but
            // being explicit costs nothing and makes the ordering ours; SIGHUP (terminal closed, macOS
            // logout) has no such default and is the one that would otherwise get away.
            Register(PosixSignal.SIGTERM);
            Register(PosixSignal.SIGINT);
            Register(PosixSignal.SIGHUP);
            Register(PosixSignal.SIGQUIT);
        }
    }

    /// <summary>
    /// Lock a document on this process's behalf, or null if it is already locked by something still
    /// running. The result is tracked, so it is released on the way out even if the caller forgets.
    /// </summary>
    public static FileLock? TryAcquire(string documentPath)
    {
        string application;
        lock (Gate)
        {
            application = _application;
        }

        var acquired = FileLock.TryAcquire(documentPath, application);
        if (acquired is null)
        {
            return null;
        }

        lock (Gate)
        {
            Held.RemoveAll(held => !held.IsHeld);
            Held.Add(acquired);
        }

        return acquired;
    }

    /// <summary>
    /// Release one lock — when its document is closed, saved elsewhere, or swapped for another. Null is
    /// accepted so callers don't have to guard.
    /// </summary>
    public static void Release(FileLock? fileLock)
    {
        if (fileLock is null)
        {
            return;
        }

        fileLock.Dispose();
        lock (Gate)
        {
            Held.Remove(fileLock);
        }
    }

    /// <summary>Release every lock this process holds. Idempotent; safe to call from any thread.</summary>
    public static void ReleaseAll()
    {
        FileLock[] locks;
        lock (Gate)
        {
            locks = [.. Held];
            Held.Clear();
        }

        foreach (var held in locks)
        {
            // Dispose is already best-effort per lock, but one unwritable directory must not stop the
            // rest of the documents from being unlocked.
            held.Dispose();
        }
    }

    /// <summary>How many locks are held — the observable the tests assert the exit paths against.</summary>
    public static int HeldCount
    {
        get
        {
            lock (Gate)
            {
                return Held.Count(held => held.IsHeld);
            }
        }
    }

    private static void Register(PosixSignal signal)
    {
        try
        {
            // No ctx.Cancel: we are tidying up on the way past, not overriding what the signal means.
            Signals.Add(PosixSignalRegistration.Create(signal, _ => ReleaseAll()));
        }
        catch (PlatformNotSupportedException)
        {
            // Windows has no SIGHUP/SIGQUIT; ProcessExit covers it there.
        }
        catch (IOException)
        {
            // The handler couldn't be installed. The other exit paths still stand.
        }
    }
}
