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
        // Grid.SelectionName is the whole selection — A1:B2 for a range, a merge's span, or a bare address —
        // so the box answers "what have I got selected?" rather than only "where is the caret?".
        NameBox.Text = Grid.SelectionName;
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
    /// The tallest the expanded formula bar may grow. Without a ceiling a value with many line breaks
    /// in it would push the grid off the bottom of the window; past this it scrolls instead.
    /// </summary>
    private const int ExpandedFormulaBarLines = 8;

    /// <summary>
    /// Toggle the formula bar between one line and a taller multi-line area for editing long formulas
    /// (task: formula-bar expand/collapse). Expanded, the input grows to several wrapped lines.
    ///
    /// <para>Collapsed it is pinned to a single line (<c>MaxLines = 1</c>). That pin is what keeps a
    /// line break typed into the <i>cell</i> from resizing the bar: the two editors mirror each other's
    /// text, but ⇧↩ only ever grows the one the caret is in.</para>
    /// </summary>
    private void ToggleFormulaBarExpanded()
    {
        SetFormulaBarExpanded(!_formulaBarExpanded);
    }

    private void SetFormulaBarExpanded(bool expanded)
    {
        _formulaBarExpanded = expanded;
        if (_formulaBarExpanded)
        {
            FormulaInput.MinHeight = 84;
            FormulaInput.MaxLines = ExpandedFormulaBarLines;
            FormulaInput.TextWrapping = TextWrapping.Wrap;
            FormulaInput.VerticalContentAlignment = VerticalAlignment.Top;
            _formulaExpandIcon.IconKey = "chevron-up";
            ToolTip.SetTip(FormulaExpandButton, "Collapse formula bar");
        }
        else
        {
            FormulaInput.MinHeight = 0;
            FormulaInput.MaxLines = 1;
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
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                InsertNewlineInFormulaBar();
            }
            else
            {
                CommitFormulaBar(moveColumn: 0, moveRow: 1);
            }

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

    /// <summary>
    /// ⇧↩ in the formula bar breaks the line there, exactly as it does in the cell — it does not commit.
    /// Inserted by hand (the input keeps <c>AcceptsReturn="False"</c> so a bare ↩ can never reach the
    /// TextBox and swallow the commit), and the bar expands so the line you just made is visible: the
    /// editor the caret is in is the one that grows.
    /// </summary>
    private void InsertNewlineInFormulaBar()
    {
        string text = FormulaInput.Text ?? string.Empty;
        int caret = Math.Clamp(FormulaInput.CaretIndex, 0, text.Length);
        int start = Math.Min(FormulaInput.SelectionStart, FormulaInput.SelectionEnd);
        int end = Math.Max(FormulaInput.SelectionStart, FormulaInput.SelectionEnd);
        if (end > start)
        {
            text = text.Remove(start, end - start); // ⇧↩ over a selection replaces it, like typing does
            caret = start;
        }

        if (!_formulaBarExpanded)
        {
            SetFormulaBarExpanded(true);
        }

        FormulaInput.Text = text.Insert(caret, "\n");
        FormulaInput.CaretIndex = caret + 1;

        // Mirror into the cell by hand: the TextChanged mirror only runs for a focused bar, and setting
        // Text above may have moved focus during the expand.
        Grid.SetInlineEditorText(FormulaInput.Text);
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

        // One parse for both shapes: CellRange.TryParse reads a bare "A1" as a one-cell range, so a single
        // address and a rectangle take the same road. The box reads ranges as well as it writes them —
        // it shows "A1:B2" for a dragged selection, so typing that back has to select the same rectangle,
        // or the box's own output would be invalid as its input.
        if (CellRange.TryParse(text, out var reference))
        {
            // A reference may name a sheet ("Reports.A1:B2"), and the one thing we must not do with that is
            // apply it to whichever sheet happens to be in front — that silently selects, and then edits,
            // the wrong cells. Honour it, or say the sheet doesn't exist.
            if (reference.Sheet is { } sheetName)
            {
                if (Session.Workbook.FindSheet(sheetName) is not { } target)
                {
                    Narrate($"No sheet named “{sheetName}”");
                    SyncFormulaBar();
                    e.Handled = true;
                    return;
                }

                // SetActiveSheet raises StructureChanged, which rebuilds the tabs and refreshes the grid.
                Session.SetActiveSheet(target);
            }

            SelectFromNameBox(reference.Top, reference.Left, reference.Bottom, reference.Right);
            JumpFromNameBox();
        }
        else if (Session.Workbook.FindNamedRange(text) is { } named)
        {
            SelectFromNameBox(named.Target.Top, named.Target.Left, named.Target.Bottom, named.Target.Right);
            JumpFromNameBox();
        }
        else
        {
            // Unrecognized input: don't silently swallow it — tell the user, and restore the box to the
            // current selection so it doesn't keep showing the bad text.
            Narrate($"No cell, range, or named range “{text.Trim()}”");
            SyncFormulaBar();
        }

        e.Handled = true;
    }

    /// <summary>
    /// Select a rectangle named in the Name Box, with the active cell at its <b>top-left</b>.
    /// <para>
    /// That corner is the point of the distinction. Typing <c>B2:C4</c> is a way of saying "take me to B2
    /// and bring its block with me": the formula bar beside the box then shows B2's contents, and the scroll
    /// lands on B2 rather than on the far corner of a range that may be a screen away. <see cref="GridSelection.SetRange"/>
    /// leaves the active cell at the bottom-right instead, which is right for a drag (the active cell
    /// follows the pointer) and wrong for a reference someone typed, where the first corner is the one they
    /// wrote down.
    /// </para>
    /// </summary>
    private void SelectFromNameBox(int top, int left, int bottom, int right) =>
        Selection.Restore(top, left, bottom, right, activeRow: top, activeColumn: left);

    /// <summary>Finish a Name Box jump: scroll the new selection into view, refresh the chrome, focus the grid.</summary>
    private void JumpFromNameBox()
    {
        RevealActiveCell();
        OnSelectionChanged();
        Refresh();
        Grid.Focus();
    }
}
