using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Core.Model;
using Puffinote.Components.Controls.Icons;

namespace Cellestra.Editor;

public partial class SpreadsheetEditorView
{
    /// <summary>Mirror the active cell into the Name Box and formula input (its value/formula may have changed).</summary>
    private void SyncFormulaBar()
    {
        // Grid.ActiveCellName reports a selected merge as its whole span (e.g. B2:C3); fall back to the
        // single active address for ordinary cells.
        NameBox.Text = Grid.ActiveCellName;
        var cell = Session.ActiveSheet.Find(Selection.ActiveRow, Selection.ActiveColumn);
        _syncingFormulaBar = true; // a programmatic refresh, not the user typing — don't echo into the cell
        FormulaInput.Text = cell is null ? string.Empty : (cell.IsFormula ? "=" + cell.Formula : cell.Value.ToInvariantString());
        _syncingFormulaBar = false;
    }

    // Guards the two-way formula-bar ↔ inline-editor mirror (task 9) against feedback loops.
    private bool _syncingFormulaBar;

    /// <summary>Cell → formula bar: reflect live keystrokes in the inline editor up into the formula input.</summary>
    private void OnInlineEditTextChanged(string text)
    {
        if (_syncingFormulaBar)
        {
            return;
        }

        _syncingFormulaBar = true;
        int caret = FormulaInput.CaretIndex;
        FormulaInput.Text = text;
        FormulaInput.CaretIndex = Math.Min(caret, text.Length);
        _syncingFormulaBar = false;
    }

    /// <summary>Formula bar → cell: mirror typing in the formula input into the inline editor ("vice versa").</summary>
    private void FormulaInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        // Only when the user is actually typing in the bar — not during a programmatic SyncFormulaBar
        // refresh or an echo from the cell side (which would otherwise begin a spurious edit).
        if (_syncingFormulaBar || !FormulaInput.IsFocused)
        {
            return;
        }

        _syncingFormulaBar = true;
        Grid.SetInlineEditorText(FormulaInput.Text ?? string.Empty);
        _syncingFormulaBar = false;
    }

    private bool _formulaBarExpanded;

    // The expand/collapse chevron, drawn as a crisp Lucide glyph (the old "⌄"/"⌃" text glyphs were
    // clipped by the button's fixed width). Its IconKey flips with _formulaBarExpanded.
    private readonly LucideIcon _formulaExpandIcon = new() { IconKey = "chevron-down", Size = 14 };

    /// <summary>
    /// Toggle the formula bar between one line and a taller multi-line area for editing long formulas
    /// (task: formula-bar expand/collapse). Expanded, the input grows to several wrapped lines.
    /// </summary>
    private void ToggleFormulaBarExpanded()
    {
        _formulaBarExpanded = !_formulaBarExpanded;
        if (_formulaBarExpanded)
        {
            FormulaInput.MinHeight = 84;
            FormulaInput.TextWrapping = TextWrapping.Wrap;
            FormulaInput.VerticalContentAlignment = VerticalAlignment.Top;
            _formulaExpandIcon.IconKey = "chevron-up";
            ToolTip.SetTip(FormulaExpandButton, "Collapse formula bar");
        }
        else
        {
            FormulaInput.MinHeight = 0;
            FormulaInput.ClearValue(TextBox.HeightProperty);
            FormulaInput.TextWrapping = TextWrapping.NoWrap;
            FormulaInput.VerticalContentAlignment = VerticalAlignment.Center;
            _formulaExpandIcon.IconKey = "chevron-down";
            ToolTip.SetTip(FormulaExpandButton, "Expand formula bar");
        }
    }

    private void FormulaInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            CommitFormulaBar(moveColumn: 0, moveRow: 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            // Commit-and-move like the inline editor: Tab steps right, Shift+Tab left (task 15a).
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            CommitFormulaBar(moveColumn: shift ? -1 : 1, moveRow: 0);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (Grid.IsInlineEditing)
            {
                Grid.CancelInlineEdit();
            }

            OnSelectionChanged();
            Grid.Focus();
        }
        else if (e.Key == Key.F4)
        {
            var (text, caret) = Cellestra.Core.Formulas.References.ReferenceCycler.Cycle(
                FormulaInput.Text ?? string.Empty, FormulaInput.CaretIndex);
            FormulaInput.Text = text;
            FormulaInput.CaretIndex = caret;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete &&
                 (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control | KeyModifiers.Alt)) == 0)
        {
            // Forward-delete the character (or selection) to the right of the caret. Handled here so the
            // global "Clear Contents" shortcut (also Delete) can't preempt it and wipe the whole cell.
            DeleteForwardInFormulaBar();
            e.Handled = true;
        }
    }

    /// <summary>Delete the selection (if any) or the single character to the right of the caret in the formula input.</summary>
    private void DeleteForwardInFormulaBar()
    {
        string text = FormulaInput.Text ?? string.Empty;
        int start = Math.Min(FormulaInput.SelectionStart, FormulaInput.SelectionEnd);
        int end = Math.Max(FormulaInput.SelectionStart, FormulaInput.SelectionEnd);

        if (start != end)
        {
            FormulaInput.Text = text.Remove(start, end - start);
            FormulaInput.CaretIndex = start;
        }
        else if (start < text.Length)
        {
            FormulaInput.Text = text.Remove(start, 1);
            FormulaInput.CaretIndex = start;
        }
    }

    /// <summary>
    /// Commit what's in the formula bar through the grid's one validated path (so data validation runs
    /// either way), then move the selection. Whether or not a mirrored inline edit is already open, we go
    /// through <see cref="SpreadsheetGrid.SpreadsheetGrid.CommitInlineEdit"/> — when none is open we first
    /// push the bar's text in as an edit so the same validate-then-SetInput lifecycle applies (task 15b).
    /// </summary>
    private void CommitFormulaBar(int moveColumn, int moveRow)
    {
        if (!Grid.IsInlineEditing)
        {
            // Mirror the bar's text into a (focus-less) inline edit so the commit runs validation rather
            // than calling EditCommands.SetInput directly and bypassing it.
            Grid.SetInlineEditorText(FormulaInput.Text ?? string.Empty);
        }

        Grid.CommitInlineEdit(moveRow, moveColumn);
        Grid.Focus();
    }

    /// <summary>
    /// Clicking away from the formula bar (to a non-grid surface) leaves a mirrored inline edit open and
    /// uncommitted; commit it on blur so the typed value isn't silently lost (task 15).
    /// </summary>
    private void FormulaInputLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Collapse any selection/caret so the bar doesn't keep showing a highlighted I-beam once focus
        // returns to a cell — Avalonia leaves the selection brush drawn on an unfocused TextBox (#5).
        FormulaInput.SelectionStart = FormulaInput.SelectionEnd = 0;
        FormulaInput.CaretIndex = 0;

        if (_syncingFormulaBar)
        {
            return; // a programmatic refresh moved focus, not the user committing
        }

        if (Grid.IsInlineEditing)
        {
            // Focus moving to the grid while a formula is open is a point-mode reference click
            // (task 1): let the grid insert the reference instead of committing here. Any other blur
            // (e.g. clicking a toolbar button) still commits the edit in place.
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (Grid.IsInlineFormulaEdit && ReferenceEquals(focused, Grid))
            {
                return;
            }

            Grid.CommitInlineEdit(0, 0); // commit in place, no selection move
        }
    }

    private void NameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return)
        {
            return;
        }

        string text = NameBox.Text ?? string.Empty;
        if (CellAddress.TryParse(text, out var address))
        {
            Selection.MoveTo(address.Row, address.Column);
            JumpFromNameBox();
        }
        else if (Session.Workbook.FindNamedRange(text) is { } named)
        {
            Selection.SetRange(named.Target.Top, named.Target.Left, named.Target.Bottom, named.Target.Right);
            JumpFromNameBox();
        }
        else
        {
            // Unrecognized input: don't silently swallow it — tell the user, and restore the box to the
            // current address so it doesn't keep showing the bad text.
            Narrate($"No cell or named range “{text.Trim()}”");
            SyncFormulaBar();
        }

        e.Handled = true;
    }

    /// <summary>Finish a Name Box jump: scroll the new selection into view, refresh the chrome, focus the grid.</summary>
    private void JumpFromNameBox()
    {
        RevealActiveCell();
        OnSelectionChanged();
        Refresh();
        Grid.Focus();
    }
}
