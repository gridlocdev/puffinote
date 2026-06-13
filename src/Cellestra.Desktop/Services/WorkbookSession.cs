using System;
using System.Collections.Generic;
using System.IO;
using Cellestra.Core.Dependencies;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;
using Cellestra.Core.Values;

namespace Cellestra.Desktop.Services;

/// <summary>
/// The bridge between the UI and the Core brain: holds the open <see cref="Workbook"/>, its
/// recalculation engine and undo history, the active sheet and selection, and the dirty/file
/// state. The UI talks to the model only through here, so edits, recalc, and undo stay in step.
/// </summary>
public sealed class WorkbookSession
{
    private Workbook _workbook;
    private RecalculationEngine _recalc;
    private EditHistory _history;
    private OdsPackage? _sourcePackage;

    // Selection mementos paralleling the undo/redo stacks, so undo restores the active cell and
    // range to where the edit happened (#33). Provided by the UI, which owns the selection.
    private readonly Stack<SelectionMemento> _undoSelections = new();
    private readonly Stack<SelectionMemento> _redoSelections = new();

    public WorkbookSession(Workbook workbook)
    {
        _workbook = workbook;
        _recalc = new RecalculationEngine(workbook);
        _history = new EditHistory(workbook);
        _recalc.RebuildGraph();
        ActiveSheet = workbook.ActiveSheet;
    }

    public static WorkbookSession NewBlank() => new(Workbook.CreateNew());

    public Workbook Workbook => _workbook;

    public Worksheet ActiveSheet { get; private set; }

    // ---- Format Painter arming -------------------------------------------
    // The Format Painter is a two-step gesture: a first invocation captures the source's formatting
    // (the "stamp"); the painter is then "armed" until that formatting is applied once. The stamp
    // lives here (not in a private static) so both the command catalog and the grid observe one shared
    // state through a clean seam — the grid arms its paintbrush cursor and drag-to-paint off it.
    private FormatBlock? _formatStamp;

    /// <summary>The captured formatting waiting to be painted, or null when the painter isn't armed.</summary>
    public FormatBlock? FormatStamp => _formatStamp;

    /// <summary>True while the Format Painter holds a captured stamp ready to apply.</summary>
    public bool IsFormatPainterArmed => _formatStamp is not null;

    /// <summary>Raised when the painter is armed or disarmed, so the grid can update its cursor and repaint.</summary>
    public event EventHandler? FormatPainterArmedChanged;

    /// <summary>Capture <paramref name="stamp"/> and arm the painter (replaces any prior stamp).</summary>
    public void ArmFormatPainter(FormatBlock stamp)
    {
        _formatStamp = stamp;
        FormatPainterArmedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Disarm the painter without applying (Esc, or after a single-shot paint).</summary>
    public void DisarmFormatPainter()
    {
        if (_formatStamp is null)
        {
            return;
        }

        _formatStamp = null;
        FormatPainterArmedChanged?.Invoke(this, EventArgs.Empty);
    }

    public string? FilePath { get; private set; }

    public bool IsModified { get; private set; }

    /// <summary>
    /// How many formula cells recomputed to a value different from the one cached in the file on
    /// the last open (PRD §8.6). Cellestra shows the freshly recomputed values; a non-zero count
    /// flags a divergence from what LibreOffice last wrote — usually a function-coverage gap.
    /// </summary>
    public int LoadRecalcMismatches { get; private set; }

    /// <summary>Raised after any change that affects displayed values (recalc done).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the active sheet or sheet list changes.</summary>
    public event EventHandler? StructureChanged;

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public string? UndoLabel => _history.UndoLabel;

    public string? RedoLabel => _history.RedoLabel;

    public Worksheet AddSheet()
    {
        var sheet = _workbook.AddSheet();
        SetActiveSheet(sheet);
        return sheet;
    }

    /// <summary>Add a chart sheet for <paramref name="chart"/>, make it active, and mark the workbook modified.</summary>
    public Worksheet AddChartSheet(Cellestra.Charts.ChartDefinition chart)
    {
        var sheet = _workbook.AddChartSheet(chart);
        IsModified = true;
        SetActiveSheet(sheet); // raises StructureChanged so the UI swaps in the chart view
        return sheet;
    }

    /// <summary>
    /// Record that a chart definition was edited in place: mark the workbook modified and raise
    /// <see cref="Changed"/> so the chrome (dirty flag, status) refreshes. Chart edits are not part of
    /// the cell undo history (like other structural sheet changes, PRD §8.5).
    /// </summary>
    public void MarkChartModified()
    {
        IsModified = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetActiveSheet(Worksheet sheet)
    {
        if (_workbook.IndexOf(sheet) < 0)
        {
            return;
        }

        ActiveSheet = sheet;
        _workbook.Settings.ActiveSheetIndex = _workbook.IndexOf(sheet);
        StructureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Delete a worksheet (no-op on the last one) and activate a neighbour. Marks the workbook modified.</summary>
    public void DeleteSheet(Worksheet sheet)
    {
        if (_workbook.Worksheets.Count <= 1)
        {
            return;
        }

        int index = _workbook.IndexOf(sheet);
        if (!_workbook.RemoveSheet(sheet))
        {
            return;
        }

        // Undo commands resolve their sheet by name, so a deleted sheet would make replaying older
        // edits throw. Sheet structure changes aren't undoable (PRD §8.5) — clear the stacks.
        ClearHistory();
        IsModified = true;
        var next = _workbook.Worksheets[System.Math.Min(index, _workbook.Worksheets.Count - 1)];
        SetActiveSheet(next); // raises StructureChanged
    }

    /// <summary>Rename a worksheet (throws on an invalid or duplicate name so the UI can explain it).</summary>
    public void RenameSheet(Worksheet sheet, string newName)
    {
        _workbook.RenameSheet(sheet, newName);
        // Older undo commands reference the previous sheet name and would throw on replay; drop them.
        ClearHistory();
        IsModified = true;
        StructureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reorder a worksheet (drag-to-reorder the tabs), keeping the same sheet active.</summary>
    public void MoveSheet(int fromIndex, int toIndex)
    {
        var active = ActiveSheet;
        _workbook.MoveSheet(fromIndex, toIndex);
        _workbook.Settings.ActiveSheetIndex = _workbook.IndexOf(active);
        IsModified = true;
        StructureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drop the undo/redo stacks (used by non-undoable structural sheet changes).</summary>
    private void ClearHistory()
    {
        _history.Clear();
        _undoSelections.Clear();
        _redoSelections.Clear();
    }

    /// <summary>Captures the current selection; set by the UI so undo can restore it.</summary>
    public Func<SelectionMemento>? SelectionProvider { get; set; }

    /// <summary>Restores a selection memento; set by the UI so undo can restore it.</summary>
    public Action<SelectionMemento>? SelectionRestorer { get; set; }

    public void Execute(IEditCommand command)
    {
        if (SelectionProvider is { } capture)
        {
            _undoSelections.Push(capture());
            _redoSelections.Clear();
        }

        var result = _history.Execute(command);
        IsModified = true;
        ApplyResult(result);
    }

    public void Undo()
    {
        if (!_history.CanUndo)
        {
            return;
        }

        if (SelectionProvider is { } capture)
        {
            _redoSelections.Push(capture());
        }

        ApplyResult(_history.Undo());
        IsModified = true;

        if (SelectionRestorer is { } restore && _undoSelections.Count > 0)
        {
            restore(_undoSelections.Pop()); // back to where the edit happened
        }
    }

    public void Redo()
    {
        if (!_history.CanRedo)
        {
            return;
        }

        if (SelectionProvider is { } capture)
        {
            _undoSelections.Push(capture());
        }

        ApplyResult(_history.Redo());
        IsModified = true;

        if (SelectionRestorer is { } restore && _redoSelections.Count > 0)
        {
            restore(_redoSelections.Pop());
        }
    }

    /// <summary>Force a full recalculation (F9).</summary>
    public void RecalculateAll()
    {
        _recalc.RecalculateAll();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyResult(EditResult result)
    {
        if (result.Structural)
        {
            _recalc.RebuildGraph();
            _recalc.RecalculateAll();
            StructureChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (result.ChangedCells.Count > 0)
        {
            _recalc.RecordEdits(result.ChangedCells);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- File ------------------------------------------------------------

    public void LoadFrom(Stream stream, string path)
    {
        var (workbook, package) = OdsReader.ReadWithPackage(stream);
        Reset(workbook, package, path);
    }

    public void SaveTo(Stream stream, string? path = null)
    {
        OdsWriter.Write(_workbook, stream, _sourcePackage);
        if (path is not null)
        {
            FilePath = path;
        }

        IsModified = false;
    }

    /// <summary>
    /// Save atomically: write the whole <c>.ods</c> to a sibling temp file, then rename it over
    /// the target. A crash, disk-full, or permission error mid-write leaves the original file
    /// untouched (PRD §3 round-trip safety) — never a half-written, corrupt document.
    /// </summary>
    public void SaveToFile(string path)
    {
        WriteBytesAtomically(path, SerializeToBytes());
        MarkSaved(path);
    }

    /// <summary>
    /// Serialize the workbook to an in-memory <c>.ods</c> image. The model is read synchronously, so
    /// callers on the UI thread get a consistent snapshot with no concurrent edit; the returned bytes
    /// are immutable and safe to hand to a background thread for the disk write. (The earlier code
    /// serialized the live model directly on a background <c>Task.Run</c> while the grid still had
    /// keyboard focus, so a keystroke mid-save mutated the model under the serializer.)
    /// </summary>
    public byte[] SerializeToBytes() => OdsWriter.WriteToBytes(_workbook, _sourcePackage);

    /// <summary>
    /// Atomically write a pre-serialized <c>.ods</c> image to disk: a sibling temp file, then a rename
    /// over the target. Any failure leaves the original untouched and cleans up the temp. Safe to call
    /// off the UI thread since it touches no shared model state.
    /// </summary>
    public static void WriteBytesAtomically(string path, byte[] bytes)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath) ?? ".";
        string temp = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, fullPath, overwrite: true); // atomic on the same volume
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Record that the in-memory workbook now matches the file saved at <paramref name="path"/>.</summary>
    public void MarkSaved(string path)
    {
        FilePath = Path.GetFullPath(path);
        IsModified = false;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of the temp file; nothing actionable if it fails.
        }
    }

    /// <summary>
    /// Recompute every formula and count how many disagree with the value cached in the file.
    /// Numbers compare with a relative tolerance (floating-point noise is not a disagreement);
    /// a cached blank is treated as "no cache" and never flagged.
    /// </summary>
    private static int RecalculateAndCompareToCache(Workbook workbook, RecalculationEngine recalc)
    {
        var cached = new List<(string Sheet, int Row, int Column, CellValue Value)>();
        foreach (var sheet in workbook.Worksheets)
        {
            foreach (var (row, column, cell) in sheet.EnumerateCells())
            {
                if (cell.IsFormula && !cell.Value.IsEmpty)
                {
                    cached.Add((sheet.Name, row, column, cell.Value));
                }
            }
        }

        recalc.RecalculateAll();

        int mismatches = 0;
        foreach (var (sheetName, row, column, before) in cached)
        {
            var after = workbook.FindSheet(sheetName)?.Find(row, column)?.Value ?? CellValue.Empty;
            if (!ValuesAgree(before, after))
            {
                mismatches++;
            }
        }

        return mismatches;
    }

    private static bool ValuesAgree(CellValue cached, CellValue recomputed)
    {
        bool cachedNumeric = cached.IsNumber || cached.IsBoolean;
        bool recomputedNumeric = recomputed.IsNumber || recomputed.IsBoolean;
        if (cachedNumeric && recomputedNumeric)
        {
            double x = cached.AsNumber;
            double y = recomputed.AsNumber;
            double tolerance = 1e-9 * Math.Max(1.0, Math.Max(Math.Abs(x), Math.Abs(y)));
            return Math.Abs(x - y) <= tolerance;
        }

        return cached.Type == recomputed.Type && cached.Equals(recomputed);
    }

    private void Reset(Workbook workbook, OdsPackage? package, string? path) =>
        AdoptWorkbook(LoadAndRecalc(workbook, package), path);

    /// <summary>
    /// A deserialized, fully-recalculated workbook ready to adopt on the UI thread. Produced by the
    /// heavy <see cref="LoadAndRecalc(Stream)"/> on a background thread, consumed by
    /// <see cref="AdoptWorkbook"/> on the UI thread.
    /// </summary>
    public sealed record LoadedWorkbook(Workbook Workbook, OdsPackage? Package, RecalculationEngine Recalc, int Mismatches);

    /// <summary>
    /// Deserialize a workbook and run its initial recalculation (PRD §8.6) — the heavy, UI-free work
    /// the app layer runs on a background thread so a large open never blocks the UI thread. Touches
    /// no session state; the result is swapped in by <see cref="AdoptWorkbook"/> on the UI thread.
    /// </summary>
    public static LoadedWorkbook LoadAndRecalc(Stream stream)
    {
        var (workbook, package) = OdsReader.ReadWithPackage(stream);
        return LoadAndRecalc(workbook, package);
    }

    private static LoadedWorkbook LoadAndRecalc(Workbook workbook, OdsPackage? package)
    {
        var recalc = new RecalculationEngine(workbook);
        recalc.RebuildGraph();
        int mismatches = RecalculateAndCompareToCache(workbook, recalc);
        return new LoadedWorkbook(workbook, package, recalc, mismatches);
    }

    /// <summary>
    /// Swap in a workbook produced by <see cref="LoadAndRecalc(Stream)"/> (UI thread only): adopt its
    /// recalc engine, reset undo history and selection mementos, and raise the change events so the
    /// grid repaints. No recalculation happens here — it already ran off-thread.
    /// </summary>
    public void AdoptWorkbook(LoadedWorkbook loaded, string? path)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        _workbook = loaded.Workbook;
        _sourcePackage = loaded.Package;
        _recalc = loaded.Recalc;
        LoadRecalcMismatches = loaded.Mismatches;
        _history = new EditHistory(_workbook);
        _undoSelections.Clear();
        _redoSelections.Clear();
        ActiveSheet = _workbook.ActiveSheet;
        FilePath = path;
        IsModified = false;
        StructureChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
