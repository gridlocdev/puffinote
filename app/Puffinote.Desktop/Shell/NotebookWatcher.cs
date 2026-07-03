using Avalonia.Threading;

namespace Puffinote.Desktop.Shell;

/// <summary>
/// Watches the open notebook for external filesystem changes (PRD §7.1 — live tree refresh) and raises a single
/// debounced callback on the UI thread, so the shell can rebuild the file tree and refresh the search index
/// when files appear/vanish/change outside Puffinote. Changes under <c>.puffinote/</c> and hidden dot-entries
/// are ignored (they never enter the tree, and ignoring our own index writes avoids a refresh feedback loop).
/// </summary>
internal sealed class NotebookWatcher : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(400);

    private readonly string _root;
    private readonly Action _onChanged;
    private readonly FileSystemWatcher _watcher;
    private readonly object _gate = new();
    private Timer? _debounce;
    private bool _disposed;

    public NotebookWatcher(string root, Action onChanged)
    {
        _root = System.IO.Path.GetFullPath(root);
        _onChanged = onChanged;

        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += (_, _) => Schedule(); // buffer overflow etc. → just do a full refresh
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Whether a change at <paramref name="fullPath"/> should trigger a refresh: true for entries inside the
    /// notebook that aren't hidden dot-entries or under the <c>.puffinote/</c> state directory. Pure/testable.
    /// </summary>
    public static bool IsRelevant(string root, string fullPath)
    {
        var relative = System.IO.Path.GetRelativePath(root, fullPath);
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return false; // the root itself, or something outside the notebook
        }

        foreach (var segment in relative.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
        {
            if (segment.Length > 0 && segment[0] == '.')
            {
                return false; // a dotfile / dot-directory (incl. .puffinote) anywhere in the path
            }
        }

        return true;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (IsRelevant(_root, e.FullPath))
        {
            Schedule();
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        // A rename is relevant if either end is in-scope (e.g. moving a file into or out of the notebook view).
        if (IsRelevant(_root, e.FullPath) || IsRelevant(_root, e.OldFullPath))
        {
            Schedule();
        }
    }

    // Coalesce a burst of events (a save often fires several) into one refresh after a quiet interval.
    private void Schedule()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _debounce?.Dispose();
            _debounce = new Timer(_ => Fire(), null, DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        if (!_disposed)
        {
            Dispatcher.UIThread.Post(_onChanged);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _debounce?.Dispose();
            _debounce = null;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
