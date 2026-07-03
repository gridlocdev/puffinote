using System.Linq;
using Avalonia.Controls;
using Cellestra.Desktop.Services;
using Cellestra.Editor;
using Puffinote.Components.Commands;

namespace Puffinote.Desktop.Panes;

/// <summary>
/// Hosts the <em>existing</em> Cellestra <see cref="SpreadsheetEditorView"/> unchanged. That control
/// self-hosts the whole spreadsheet UI (grid, formula/command chrome, sheet tabs, dialogs) and its own
/// command catalog — it <em>is</em> the <c>IAppEnvironment</c> — so a shell only supplies file operations
/// through <see cref="IEditorHost"/> and drives documents in/out via the stream embedding contract
/// (<c>LoadAsync(Stream, path)</c> / snapshot-and-write). Composition, not a fork.
/// </summary>
internal sealed class SheetPane : IEditorPane, IEditorHost
{
    private readonly SpreadsheetEditorView _view = new();
    private string? _path;

    public SheetPane()
    {
        _view.Host = this; // Cellestra's own file commands (its Save) route back through the pane
        _view.ModifiedChanged += (_, _) => DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public Control View => _view;

    public string Title => _path is { } p ? System.IO.Path.GetFileName(p) : "Untitled";

    public string? Path => _path;

    public bool IsDirty => _view.IsModified;

    /// <summary>Cellestra's full command catalog (the view self-hosts it), for the merged palette.</summary>
    public IReadOnlyList<AppCommand> Commands => _view.Commands.All.ToList();

    /// <summary>Outline (PRD §7.2.3): the workbook's sheets (chart sheets marked); selecting one activates it.</summary>
    public IReadOnlyList<OutlineItem> GetOutline() =>
        _view.Session.Workbook.Worksheets
            .Select(sheet => new OutlineItem(
                sheet.IsChartSheet ? $"{sheet.Name}  (chart)" : sheet.Name,
                0,
                () => { _view.Session.SetActiveSheet(sheet); _view.FocusGrid(); }))
            .ToList();

    public event EventHandler? DirtyChanged;

    public async Task LoadAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        await _view.LoadAsync(stream, path); // deserialize + initial recalc off the UI thread, then adopt
        _path = System.IO.Path.GetFullPath(path);
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync()
    {
        if (_path is null)
        {
            return;
        }

        // Snapshot the model on the UI thread (no edit can race it), write atomically off-thread, THEN
        // mark saved — so a failed write leaves the document dirty rather than falsely "saved". Mirrors
        // Cellestra.Desktop's own save path.
        var bytes = _view.Session.SerializeToBytes();
        await Task.Run(() => WorkbookSession.WriteBytesAtomically(_path!, bytes));
        _view.Session.MarkSaved(_path);
        _view.MarkSavedNow();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        // The editor holds only managed state; nothing unmanaged to release.
    }

    // ---- IEditorHost: Cellestra's in-editor file commands route to the shell/pane -----------------
    // The shell owns New/Open (they come from the file tree), so those no-op here; Save routes to the
    // pane's atomic save. Save As / CSV import-export are later phases.

    Task IEditorHost.SaveWorkbookAsync() => SaveAsync();

    Task IEditorHost.SaveWorkbookAsAsync() => SaveAsync();

    Task IEditorHost.NewWorkbookAsync() => Task.CompletedTask;

    Task IEditorHost.OpenWorkbookAsync() => Task.CompletedTask;

    Task IEditorHost.ImportCsvAsync() => Task.CompletedTask;

    Task IEditorHost.ExportCsvAsync() => Task.CompletedTask;
}
