using System;
using System.Collections.Generic;
using Cellestra.Core.Dependencies;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;

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

    public void Execute(IEditCommand command)
    {
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

        ApplyResult(_history.Undo());
        IsModified = true;
    }

    public void Redo()
    {
        if (!_history.CanRedo)
        {
            return;
        }

        ApplyResult(_history.Redo());
        IsModified = true;
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

    private void Reset(Workbook workbook, OdsPackage? package, string? path)
    {
        _workbook = workbook;
        _sourcePackage = package;
        _recalc = new RecalculationEngine(workbook);
        _recalc.RebuildGraph();
        _recalc.RecalculateAll();
        _history = new EditHistory(workbook);
        ActiveSheet = workbook.ActiveSheet;
        FilePath = path;
        IsModified = false;
        StructureChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
