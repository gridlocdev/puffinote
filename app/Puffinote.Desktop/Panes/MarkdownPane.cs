using Avalonia.Controls;
using Typage.Control;
using Typage.Control.Views;

namespace Puffinote.Desktop.Panes;

/// <summary>
/// Hosts the <em>existing</em> Typage <see cref="MarkdownEditorView"/> unchanged, driving load/save through
/// its file-host seam <see cref="MarkdownDocumentSession"/> (which owns byte-faithful, atomic writes). This
/// is composition, not a fork: Puffinote adds no Markdown logic — editing, undo, and rendering are the
/// editor's; save mechanics are the session's.
/// </summary>
internal sealed class MarkdownPane : IEditorPane
{
    private readonly MarkdownEditorView _view = new();
    private readonly MarkdownDocumentSession _session = new();

    public MarkdownPane()
    {
        // The editor raises TextChanged on every edit; re-surface it so the shell repaints the dirty dot.
        _view.TextChanged += (_, _) => DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public Control View => _view;

    public string Title =>
        _session.CurrentPath is { } p ? System.IO.Path.GetFileName(p) : "Untitled";

    public string? Path => _session.CurrentPath;

    public bool IsDirty => _view.IsDirty;

    public event EventHandler? DirtyChanged;

    public Task LoadAsync(string path)
    {
        // Note-sized files: read on the UI thread (the session's atomic I/O is fast) and set the editor's
        // saved baseline. LoadText marks the text clean, so a freshly opened doc isn't dirty.
        _view.LoadText(_session.Load(path));
        DirtyChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task SaveAsync()
    {
        _session.Save(_view.Text);
        _view.MarkSaved();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // The editor holds only managed state; nothing unmanaged to release in Phase 1.
    }
}
