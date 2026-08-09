using Avalonia.Input;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

public sealed partial class SpreadsheetGrid
{
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Remember the chord on the key that may generate the next OnTextInput, so a Cmd/Ctrl combo
        // (reserved for shortcuts) never begins a cell edit even when the OS still emits its character.
        _lastKeyModifiers = e.KeyModifiers;
        base.OnKeyDown(e);
        if (InputBlocked)
        {
            return; // a modal is open above the grid — let the key reach the overlay, not the grid
        }

        if (IsInlineEditing)
        {
            return;
        }

        // Esc abandons an in-flight radial gesture before any other Esc behaviour.
        if (e.Key == Key.Escape && (_radialActive || _radialPending))
        {
            CancelRadial();
            e.Handled = true;
            return;
        }

        // Esc first dismisses an open filter/validation dropdown popup (a lightweight overlay, not a
        // modal) before any marquee/selection/paint Esc behaviour kicks in.
        if (e.Key == Key.Escape && _popupOpen)
        {
            ClosePopup();
            e.Handled = true;
            return;
        }

        // Esc disarms the Format Painter (cancel without painting) before any other Esc behavior.
        if (e.Key == Key.Escape && TryCancelPaint())
        {
            e.Handled = true;
            return;
        }

        bool extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        // ⌘+Arrow (Ctrl+Arrow off macOS) jumps to the edge of the data block, Excel-style; with Shift
        // it extends the selection to that edge instead of moving.
        bool jump = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);

        // ⌥⇧+Arrow (Option=Alt on macOS) nudges the selection's contents one cell in that direction,
        // rewriting any formula that pointed at the moved cells so the references follow — the same
        // move semantics as a cut-and-paste. Requires Alt+Shift with no ⌘/Ctrl jump modifier.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && extend && !jump
            && TryArrowDelta(e.Key, out int moveRow, out int moveColumn))
        {
            MoveSelectionRequested?.Invoke(moveRow, moveColumn);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                _tabOriginColumn = null;
                Step(-1, 0, extend, jump);
                break;
            case Key.Down:
                _tabOriginColumn = null;
                Step(1, 0, extend, jump);
                break;
            case Key.Left:
                _tabOriginColumn = null;
                Step(0, -1, extend, jump);
                break;
            case Key.Right:
                _tabOriginColumn = null;
                Step(0, 1, extend, jump);
                break;
            case Key.Tab:
                MoveStep(0, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1, false);
                break;
            case Key.Return:
                // Cmd+Return opens the active cell's hyperlink (mirroring the Cmd-click gesture);
                // plain Return keeps its established meaning — paste a marquee, else move down — so
                // hyperlink activation never steals Enter from navigation/commit.
                if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                    && TryActivateHyperlink(Selection.ActiveRow, Selection.ActiveColumn))
                {
                    break;
                }

                if (HasMarquee)
                {
                    EnterPasteRequested?.Invoke(this, EventArgs.Empty); // paste once at the active cell (#3)
                }
                else
                {
                    MoveStep(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1, 0, false);
                }

                break;
            case Key.F2:
                BeginEdit(null);
                break;
            case Key.Delete or Key.Back:
                ClearSelection();
                break;
            case Key.Escape:
                // A first Esc cancels a pending copy/cut marquee (keeping the selection); once there's
                // no marquee, Esc collapses a multi-cell or disjoint selection back to its origin cell.
                if (HasMarquee)
                {
                    InvalidateClipboard();
                }
                else if (!Selection.IsSingleCell || Selection.IsDiscontiguous)
                {
                    Selection.CollapseToOrigin();

                    // Esc moves the selection like any other navigation key, so the host has to hear about
                    // it: without this the Name Box and the status bar kept describing the selection Esc
                    // just threw away, and the commands gated on one stayed enabled.
                    RaiseSelectionChanged();
                }

                break;
            default:
                // Every printable key starts its edit from OnTextInput, seeded with the character it
                // produced. Opening an *empty* editor here and leaving the character to find its own way
                // in — which is what a separate A–Z/0–9 branch did — meant the first keystroke of an edit
                // travelled by a different road than every one after it, arriving only if the text-input
                // event was re-routed to a TextBox that had been focused mid-event. It is also the only
                // road that knows what "$" or "é" is: this branch only ever recognised A–Z and 0–9.
                return;
        }

        EnsureVisible(Selection.ActiveRow, Selection.ActiveColumn);
        e.Handled = true;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (InputBlocked)
        {
            return; // a modal is open above the grid — don't begin an edit behind it
        }

        // A Cmd/Ctrl chord is reserved for shortcuts; the character some platforms still emit for an
        // unbound combo (e.g. ⌘J) must not start a cell edit.
        if (_lastKeyModifiers.HasFlag(KeyModifiers.Meta) || _lastKeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (!IsInlineEditing && !string.IsNullOrEmpty(e.Text) && e.Text[0] >= ' ')
        {
            BeginEdit(e.Text);
            e.Handled = true;
        }
    }

    /// <summary>The (rowDelta, columnDelta) for an arrow key, or false for any other key.</summary>
    private static bool TryArrowDelta(Key key, out int rowDelta, out int columnDelta)
    {
        (rowDelta, columnDelta) = key switch
        {
            Key.Up => (-1, 0),
            Key.Down => (1, 0),
            Key.Left => (0, -1),
            Key.Right => (0, 1),
            _ => (0, 0),
        };

        return rowDelta != 0 || columnDelta != 0;
    }

    /// <summary>
    /// Move (or extend) the active cell by one step, skipping over any merged region: a step starts
    /// from the current merge's edge and lands on the cell past it, and entering a merge selects the
    /// whole span. Falls back to a plain one-cell step when there are no merges in the way.
    /// </summary>
    private void MoveStep(int rowDelta, int columnDelta, bool extend)
    {
        var sheet = Sheet;
        int row = Selection.ActiveRow;
        int column = Selection.ActiveColumn;
        var (targetRow, targetColumn) = sheet?.StepOverMerges(row, column, rowDelta, columnDelta)
            ?? (Math.Clamp(row + rowDelta, 0, SheetDimensions.MaxRows - 1),
                Math.Clamp(column + columnDelta, 0, SheetDimensions.MaxColumns - 1));
        Move(targetRow, targetColumn, extend);
    }

    /// <summary>One arrow press: a ⌘-jump to the data edge, or a single merge-aware step.</summary>
    private void Step(int rowDelta, int columnDelta, bool extend, bool jump)
    {
        if (jump)
        {
            JumpEdge(rowDelta, columnDelta, extend);
        }
        else
        {
            MoveStep(rowDelta, columnDelta, extend);
        }
    }

    /// <summary>⌘+Arrow: move (or, with Shift, extend) to the next data edge in a direction (#1).</summary>
    private void JumpEdge(int rowDelta, int columnDelta, bool extend)
    {
        var sheet = Sheet;
        if (sheet is null)
        {
            MoveStep(rowDelta, columnDelta, extend);
            return;
        }

        var (targetRow, targetColumn) = Cellestra.Core.Data.EdgeNavigator.FindEdge(
            sheet, Selection.ActiveRow, Selection.ActiveColumn, rowDelta, columnDelta);
        Move(targetRow, targetColumn, extend);
    }

    /// <summary>Test seam: drive the merge-aware arrow navigation directly (no key event plumbing).</summary>
    internal void NavigateForTest(int rowDelta, int columnDelta, bool extend) => MoveStep(rowDelta, columnDelta, extend);

    /// <summary>Test seam: drive ⌘+Arrow edge-jump navigation directly (no key event plumbing).</summary>
    internal void JumpEdgeForTest(int rowDelta, int columnDelta, bool extend) => JumpEdge(rowDelta, columnDelta, extend);

    /// <summary>Test seam: select a cell the way a click/arrow does, expanding onto a merge.</summary>
    internal void SelectForTest(int row, int column)
    {
        SelectCellOrMerge(row, column);
        RaiseSelectionChanged();
    }

    private void Move(int row, int column, bool extend)
    {
        if (extend)
        {
            Selection.ExtendTo(row, column);
        }
        else
        {
            SelectCellOrMerge(row, column);
        }

        RaiseSelectionChanged();
    }

    /// <summary>
    /// Select a single cell, or — when it falls inside a merged region — the whole merge (active cell
    /// at the merge's anchor). This keeps the active outline and Name Box reporting the full span.
    /// </summary>
    private void SelectCellOrMerge(int row, int column)
    {
        if (Sheet?.FindMerge(row, column) is { } merge)
        {
            // active = anchor (top-left); the range covers the whole merge.
            Selection.Restore(merge.Range.Top, merge.Range.Left, merge.Range.Bottom, merge.Range.Right,
                merge.Range.Top, merge.Range.Left);
        }
        else
        {
            Selection.MoveTo(row, column);
        }
    }

    private void ClearSelection()
    {
        if (_session is null)
        {
            return;
        }

        _session.Execute(Cellestra.Core.Editing.EditCommands.ClearContents(_session.ActiveSheet.Name, Selection.Range));
        Redraw();
        RaiseSelectionChanged();
    }

    /// <summary>Scroll the active cell into view (used after a programmatic move, e.g. a Find result).</summary>
    internal void ScrollToActive() => EnsureVisible(Selection.ActiveRow, Selection.ActiveColumn);

    private void EnsureVisible(int row, int column)
    {
        var sheet = Sheet;
        int frozenRows = sheet?.FrozenRows ?? 0;
        int frozenColumns = sheet?.FrozenColumns ?? 0;

        // Frozen lines are always on screen; only scroll for cells beyond them. Any adjustment lands the
        // target on a whole-cell boundary (offset reset to 0) so keyboard navigation never leaves the
        // active cell half-clipped under the fold.
        if (row >= frozenRows)
        {
            if (row < _scrollRow)
            {
                _scrollRow = row;
                ScrollRowOffsetForTest = 0;
            }
            else if (row == _scrollRow && ScrollRowOffsetForTest > 0)
            {
                ScrollRowOffsetForTest = 0; // the anchor itself was partly scrolled off the top — reveal it
            }
            else if (_visibleRows.Count > 0 && (row > _visibleRows[^1].Index || RowPartlyBelowFold(row)))
            {
                _scrollRow += Math.Max(1, row - _visibleRows[^1].Index);
                ScrollRowOffsetForTest = 0;
            }
        }

        if (column >= frozenColumns)
        {
            if (column < _scrollColumn)
            {
                _scrollColumn = column;
                ScrollColumnOffsetForTest = 0;
            }
            else if (column == _scrollColumn && ScrollColumnOffsetForTest > 0)
            {
                ScrollColumnOffsetForTest = 0;
            }
            else if (_visibleColumns.Count > 0 && (column > _visibleColumns[^1].Index || ColumnPartlyPastFold(column)))
            {
                _scrollColumn += Math.Max(1, column - _visibleColumns[^1].Index);
                ScrollColumnOffsetForTest = 0;
            }
        }

        _scrollRow = Math.Max(_scrollRow, frozenRows);
        _scrollColumn = Math.Max(_scrollColumn, frozenColumns);
        if (sheet is not null)
        {
            NormalizeVerticalScroll(sheet);
            NormalizeHorizontalScroll(sheet);
        }

        Redraw();
        InvalidateArrange(); // keyboard navigation may have scrolled — refresh the bars
    }

    /// <summary>True when <paramref name="row"/> is the last visible row but the fold clips its bottom.</summary>
    private bool RowPartlyBelowFold(int row)
    {
        if (_visibleRows.Count == 0 || _visibleRows[^1].Index != row)
        {
            return false;
        }

        var (_, y, h) = _visibleRows[^1];
        return y + h > Bounds.Height + 0.5;
    }

    /// <summary>True when <paramref name="column"/> is the last visible column but the fold clips its right edge.</summary>
    private bool ColumnPartlyPastFold(int column)
    {
        if (_visibleColumns.Count == 0 || _visibleColumns[^1].Index != column)
        {
            return false;
        }

        var (_, x, w) = _visibleColumns[^1];
        return x + w > Bounds.Width + 0.5;
    }

    /// <summary>Freeze rows above and columns left of the active cell (or unfreeze if already frozen).</summary>
    public void ToggleFreezePanes()
    {
        var sheet = Sheet;
        if (sheet is null)
        {
            return;
        }

        if (sheet.FrozenRows > 0 || sheet.FrozenColumns > 0)
        {
            sheet.FrozenRows = 0;
            sheet.FrozenColumns = 0;
        }
        else
        {
            sheet.FrozenRows = Selection.ActiveRow;
            sheet.FrozenColumns = Selection.ActiveColumn;
        }

        Redraw();
    }
}
