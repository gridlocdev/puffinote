using Avalonia.Threading;

namespace Puffinote.Components.Shell;

/// <summary>
/// Watches the one file a single-document app has open and calls back, on the UI thread, when something
/// outside the app writes to it — a <c>git pull</c>, another editor, a synced folder. Every app in the
/// family needs this and none of it is app-specific: watch the containing directory filtered to one file
/// name (a <see cref="FileSystemWatcher"/> pointed at a file that gets replaced by a rename — which is
/// what every atomic save in this repo does — stops reporting), marshal to the UI thread, and never let
/// a failure here break editing.
/// <para>
/// What a change <em>means</em> is deliberately not here. Typage compares the decoded text, because for a
/// note that is cheap and exact; the others compare a <see cref="Core.Documents.FileStamp"/>, because
/// re-reading a workbook to find out whether it changed costs more than the answer is worth. This class
/// answers "something happened", and the app decides whether that is a reload, a conflict, or nothing.
/// </para>
/// </summary>
public sealed class OpenFileWatcher : IDisposable
{
    private readonly Action _onChanged;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    /// <param name="onChanged">Raised on the UI thread when the watched file may have changed.</param>
    public OpenFileWatcher(Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        _onChanged = onChanged;
    }

    /// <summary>The file currently being watched, or null when nothing is (an untitled buffer).</summary>
    public string? Path { get; private set; }

    /// <summary>
    /// Watch <paramref name="path"/> from now on, dropping whatever was being watched before. Null stops
    /// watching entirely — what a new, untitled document wants.
    /// </summary>
    public void Watch(string? path)
    {
        _watcher?.Dispose();
        _watcher = null;
        Path = path;
        if (_disposed || string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            string full = System.IO.Path.GetFullPath(path);
            string? directory = System.IO.Path.GetDirectoryName(full);
            if (directory is null)
            {
                return;
            }

            _watcher = new FileSystemWatcher(directory, System.IO.Path.GetFileName(full))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnEvent;
            _watcher.Created += OnEvent;
            _watcher.Renamed += OnEvent;
        }
        catch (Exception)
        {
            // A directory that can't be watched (a network mount, a full inotify table, a path that went
            // away between the open and here) costs the app this feature and nothing else.
            _watcher = null;
        }
    }

    // Events arrive on a thread-pool thread; everything the callback touches is UI state.
    private void OnEvent(object sender, FileSystemEventArgs e) => Dispatcher.UIThread.Post(Fire);

    private void Fire()
    {
        // Checked here, on the UI thread, because that is where Dispose runs: an event already in flight
        // must not reach a window that has since been torn down.
        if (!_disposed)
        {
            _onChanged();
        }
    }

    /// <summary>Test seam: deliver a change as the filesystem would, without waiting on a real one.</summary>
    public void RaiseForTest() => Fire();

    public void Dispose()
    {
        _disposed = true;
        _watcher?.Dispose();
        _watcher = null;
    }
}
