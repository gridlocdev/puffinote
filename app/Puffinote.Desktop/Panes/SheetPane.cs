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
internal sealed class SheetPane : IEditorPane, IEditorHost, IConfigurablePane
{
    private readonly SpreadsheetEditorView _view = new();

    public SheetPane()
    {
        _view.Host = this; // Cellestra's own file commands (its Save) route back through the pane
        _view.ModifiedChanged += (_, _) => DirtyChanged?.Invoke(this, EventArgs.Empty);
        ApplyConfig(new EditorConfig()); // Cellestra's defaults until the host supplies the resolved config
    }

    /// <summary>
    /// Apply Cellestra's own settings (PRD §7.5), so a sheet opens here the way it opens in the standalone
    /// app, and a Preferences edit reaches the sheet already in front of you.
    /// </summary>
    public void ApplyConfig(EditorConfig config)
    {
        _view.Zoom = config.Sheets.Zoom;
        _view.CommandBarVisibility = config.Sheets.CommandBar;
    }

    /// <summary>The editor's own reading zoom — the seam the shell remembers a tab's zoom through.</summary>
    public double Zoom
    {
        get => _view.Zoom;
        set => _view.Zoom = value;
    }

    public Control View => _view;

    /// <summary>Put keyboard focus back on the sheet grid (delegates to Cellestra's editor).</summary>
    public void Focus() => _view.FocusGrid();

    public string Title => Path is { } p ? System.IO.Path.GetFileName(p) : "Untitled";

    public string? Path { get; private set; }

    public bool IsDirty => _view.IsModified;

    /// <summary>Cellestra's full command catalog (the view self-hosts it), for the merged palette.</summary>
    public IReadOnlyList<AppCommand> Commands => [.. _view.Commands.All];

    /// <summary>Outline (PRD §7.2.3): the workbook's sheets (chart sheets marked); selecting one activates it.</summary>
    public IReadOnlyList<OutlineItem> GetOutline() =>
        [.. _view.Session.Workbook.Worksheets
            .Select(sheet => new OutlineItem(
                sheet.IsChartSheet ? $"{sheet.Name}  (chart)" : sheet.Name,
                0,
                () =>
                {
                    _view.Session.SetActiveSheet(sheet);
                    _view.FocusGrid();
                }))];

    public event EventHandler? DirtyChanged;

    public async Task LoadAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        await _view.LoadAsync(stream, path); // deserialize + initial recalc off the UI thread, then adopt
        Path = System.IO.Path.GetFullPath(path);
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The workbook changed on disk: take that version as an undoable step, so ⌘Z gives back the
    /// one the tab was holding.</summary>
    public async Task ReloadAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        await _view.LoadAsync(stream, path, reloading: true);
        Path = System.IO.Path.GetFullPath(path);
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync()
    {
        if (Path is null)
        {
            return;
        }

        // Snapshot the model on the UI thread (no edit can race it), write atomically off-thread, THEN
        // mark saved — so a failed write leaves the document dirty rather than falsely "saved". Mirrors
        // Cellestra.Desktop's own save path.
        var bytes = _view.Session.SerializeToBytes();
        await Task.Run(() => WorkbookSession.WriteBytesAtomically(Path!, bytes));
        _view.Session.MarkSaved(Path);
        _view.MarkSavedNow();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Rebind(string newPath) => Path = System.IO.Path.GetFullPath(newPath);

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
