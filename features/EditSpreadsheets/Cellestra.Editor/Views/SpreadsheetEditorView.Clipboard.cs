using System.Globalization;
using System.Text;
using Avalonia.Input.Platform;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using NumberFormatter = Cellestra.Core.Styling.NumberFormatter;

namespace Cellestra.Editor;

public partial class SpreadsheetEditorView
{
    public void Copy() => CopyToClipboard(cut: false);

    public void Cut() => CopyToClipboard(cut: true);

    public async void Paste()
    {
        string? text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

        // Prefer the rich in-app block while the OS clipboard still holds exactly what we put there
        // (so an external copy in between falls back to plain text).
        if (_clipboard is { } block && text == _clipboardText)
        {
            var command = new PasteBlockCommand(Session.ActiveSheet.Name, Selection.Range, block);
            if (!Grid.TryEnforceValidation(command)) // reject the whole paste on a Stop-rule violation (#B)
            {
                return;
            }

            Session.Execute(command);
            if (block.IsCut)
            {
                ClearClipboard(); // a cut is a move, consumed by its first paste
            }

            if (command.ShapeMismatch)
            {
                Narrate("Paste area is a different shape than the copied cells");
            }

            Grid.Refresh();
            return;
        }

        if (!string.IsNullOrEmpty(text))
        {
            PasteTsv(text);
        }
    }

    /// <summary>The largest selection ⌥⇧-arrow will nudge — guards against a whole row/column swallowing memory.</summary>
    private const int MaxNudgeCells = 16384;

    /// <summary>
    /// ⌥⇧+Arrow nudge: reposition the current selection's contents one cell in the given direction.
    /// When the destination is clear of the source it <b>swaps</b> with the equal-shaped neighbour
    /// (non-destructive, and references follow symmetrically so nothing is hijacked — the gesture is
    /// its own inverse); when a multi-cell block moves along its own axis and overlaps itself, it
    /// shifts instead, overwriting what it covers. Either way it's one undoable step. Off-sheet,
    /// multi-area, over-large, and across-merge nudges are refused with a status-line reason (#1/#5).
    /// </summary>
    private void MoveSelection(int rowDelta, int columnDelta)
    {
        if (Selection.IsDiscontiguous)
        {
            Narrate("Can’t move a multi-area selection");
            return;
        }

        var range = Selection.Range;
        int destTop = range.Top + rowDelta;
        int destLeft = range.Left + columnDelta;
        int destBottom = range.Bottom + rowDelta;
        int destRight = range.Right + columnDelta;

        if (destTop < 0 || destLeft < 0
            || destBottom >= SheetDimensions.MaxRows || destRight >= SheetDimensions.MaxColumns)
        {
            Narrate("At the edge of the sheet — can’t move further");
            return;
        }

        if (range.CellCount > MaxNudgeCells)
        {
            Narrate("Selection is too large to move");
            return;
        }

        var sheet = Session.ActiveSheet;
        var destRange = new CellRange(new CellAddress(destLeft, destTop), new CellAddress(destRight, destBottom));

        if (IntersectsMerge(sheet, range) || IntersectsMerge(sheet, destRange))
        {
            Narrate("Can’t move across merged cells");
            return;
        }

        if (!range.Intersects(destRange))
        {
            // Disjoint neighbour: exchange the two blocks (non-destructive; an empty destination makes
            // it a plain move). References into either block follow it (ReferenceAdjuster.ForSwap),
            // scoped to just the formulas the dependency graph says are affected (no workbook scan).
            var affected = AffectedFormulas(sheet.Name, range, rowDelta, columnDelta);
            Session.Execute(new SwapCellsCommand(sheet.Name, range, rowDelta, columnDelta, affected));
        }
        else
        {
            // A multi-cell block moving along its own axis overlaps itself — shift it, overwriting the
            // cells it covers (the displaced data can't be exchanged into a self-overlapping footprint).
            var block = ClipboardBlock.Capture(sheet, range, isCut: true);
            var command = new PasteBlockCommand(sheet.Name, destTop, destLeft, block);
            if (!Grid.TryEnforceValidation(command)) // honor any Stop-rule validation at the destination (#B)
            {
                return;
            }

            Session.Execute(command);
        }

        Selection.SetRange(destTop, destLeft, destBottom, destRight);
        RevealActiveCell();
        OnSelectionChanged();
        Refresh();
        Narrate($"Moved {range} to {destRange}");
    }

    /// <summary>
    /// The formula cells that reference either swap block (the source range and its destination), from
    /// the live dependency graph — so the swap rewrites only those instead of scanning the workbook.
    /// </summary>
    private IReadOnlyCollection<Cellestra.Core.Dependencies.CellRef> AffectedFormulas(
        string sheetName, CellRange blockA, int rowDelta, int columnDelta)
    {
        var cells = new List<Cellestra.Core.Dependencies.CellRef>(blockA.CellCount * 2);
        foreach (var (r, c) in blockA.Positions())
        {
            cells.Add(new Cellestra.Core.Dependencies.CellRef(sheetName, r, c));
            cells.Add(new Cellestra.Core.Dependencies.CellRef(sheetName, r + rowDelta, c + columnDelta));
        }

        return Session.FormulasReferencing(cells);
    }

    /// <summary>Whether any merged region on <paramref name="sheet"/> overlaps <paramref name="range"/>.</summary>
    private static bool IntersectsMerge(Cellestra.Core.Model.Worksheet sheet, CellRange range)
    {
        foreach (var merge in sheet.MergedRegions)
        {
            if (merge.Range.Intersects(range))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Paste Special (PRD §4 / §6.2): offer the four modes — Values / Formats / Formulas / All — in
    /// the palette overlay, then paste the chosen facet. The narrowed modes need the rich in-app block
    /// (values+formulas+styles); a plain external clipboard has only text, so we fall back to an
    /// ordinary TSV value paste and say so.
    /// </summary>
    public async void PasteSpecial()
    {
        string? text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

        // Only the rich in-app block carries formulas and styles; without it there's nothing special
        // to choose between, so paste the external text as values.
        if (_clipboard is not { } || text != _clipboardText)
        {
            if (!string.IsNullOrEmpty(text))
            {
                PasteTsv(text);
                Narrate("Pasted values (external clipboard has no formats or formulas)");
            }

            return;
        }

        var items = new[]
        {
            PasteSpecialItem("Paste Values", "Computed values only — no formulas, keep formatting", PasteMode.Values),
            PasteSpecialItem("Paste Formats", "Formatting only — leave the values in place", PasteMode.Formats),
            PasteSpecialItem("Paste Formulas", "Formulas (and their results) — keep formatting", PasteMode.Formulas),
            PasteSpecialItem("Paste All", "Values, formulas, and formatting", PasteMode.All),
        };
        ShowPalette("Paste Special — values, formats, formulas…", items);
    }

    private PaletteItem PasteSpecialItem(string label, string description, PasteMode mode) =>
        new(label, "clipboard-paste", description, string.Empty, () => PasteWithMode(mode), "Edit");

    /// <summary>Paste the in-app block at the current selection in the given <paramref name="mode"/>.</summary>
    private void PasteWithMode(PasteMode mode)
    {
        if (_clipboard is not { } block)
        {
            return; // the clipboard cleared (e.g. an edit) between opening the chooser and picking
        }

        var command = new PasteBlockCommand(Session.ActiveSheet.Name, Selection.Range, block, mode);
        if (!Grid.TryEnforceValidation(command)) // reject the whole paste on a Stop-rule violation (#B)
        {
            return;
        }

        Session.Execute(command);
        if (block.IsCut)
        {
            ClearClipboard(); // a cut is a move, consumed by its first paste
        }

        if (command.ShapeMismatch)
        {
            Narrate("Paste area is a different shape than the copied cells");
        }

        Grid.Refresh();
    }

    private async void CopyToClipboard(bool cut)
    {
        var sheet = Session.ActiveSheet;
        var range = Selection.Range;
        var culture = Session.Workbook.Settings.EffectiveLocale;
        var sb = new StringBuilder();

        for (int row = range.Top; row <= range.Bottom; row++)
        {
            for (int column = range.Left; column <= range.Right; column++)
            {
                if (column > range.Left)
                {
                    sb.Append('\t');
                }

                var cell = sheet.Find(row, column);
                if (cell is not null && !cell.Value.IsEmpty)
                {
                    sb.Append(NumberFormatter.Format(cell.Value, Session.Workbook.Styles.Get(cell.StyleId).NumberFormat, culture));
                }
            }

            sb.Append('\n');
        }

        string tsv = sb.ToString();
        _clipboard = ClipboardBlock.Capture(sheet, range, cut);
        _clipboardText = tsv;
        Grid.ShowMarquee(range, cut, sheet.Name);

        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(tsv);
        }

        // A cut no longer clears immediately — it is a deferred move that its first paste performs,
        // so an unused cut (Esc / new copy) leaves the data in place.
    }

    private void ClearClipboard()
    {
        _clipboard = null;
        _clipboardText = null;
        Grid.ShowMarquee(null, false, null);
    }

    /// <summary>Return with a marquee active pastes once at the active cell and consumes the clipboard (#3).</summary>
    private void EnterPaste()
    {
        if (_clipboard is { } block)
        {
            var command = new PasteBlockCommand(Session.ActiveSheet.Name, Selection.Range, block);
            if (!Grid.TryEnforceValidation(command)) // reject the whole paste on a Stop-rule violation (#B)
            {
                return;
            }

            Session.Execute(command);
            ClearClipboard();
            Grid.Refresh();
        }
    }

    private void PasteTsv(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
        int rows = lines.Length;
        int columns = lines.Max(l => l.Split('\t').Length);
        var values = new CellValue[rows, columns];
        var culture = Session.Workbook.Settings.EffectiveLocale;

        for (int r = 0; r < rows; r++)
        {
            var fields = lines[r].Split('\t');
            for (int c = 0; c < columns; c++)
            {
                string field = c < fields.Length ? fields[c] : string.Empty;
                values[r, c] = field.Length == 0
                    ? CellValue.Empty
                    : (double.TryParse(field, NumberStyles.Any, culture, out double n) ? CellValue.Number(n) : CellValue.Text(field));
            }
        }

        // Reject the whole paste if any landed value violates a Stop rule (#B). TSV values are never
        // formulas, so every projected write is a literal value.
        int top = Selection.ActiveRow;
        int left = Selection.ActiveColumn;
        var incoming = new List<(int, int, CellValue, bool)>(rows * columns);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                incoming.Add((top + r, left + c, values[r, c], false));
            }
        }

        if (!Grid.TryEnforceValidation(incoming))
        {
            return;
        }

        Session.Execute(EditCommands.SetBlock(Session.ActiveSheet.Name, top, left, values));
        Grid.Refresh();
    }
}
