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
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath) ?? ".";
        string temp = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = File.Create(temp))
            {
                OdsWriter.Write(_workbook, stream, _sourcePackage);
            }

            File.Move(temp, fullPath, overwrite: true); // atomic on the same volume
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        FilePath = fullPath;
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
    private int RecalculateAndCompareToCache(Workbook workbook)
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

        _recalc.RecalculateAll();

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

    private void Reset(Workbook workbook, OdsPackage? package, string? path)
    {
        _workbook = workbook;
        _sourcePackage = package;
        _recalc = new RecalculationEngine(workbook);
        _recalc.RebuildGraph();
        LoadRecalcMismatches = RecalculateAndCompareToCache(workbook);
        _history = new EditHistory(workbook);
        _undoSelections.Clear();
        _redoSelections.Clear();
        ActiveSheet = workbook.ActiveSheet;
        FilePath = path;
        IsModified = false;
        StructureChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
