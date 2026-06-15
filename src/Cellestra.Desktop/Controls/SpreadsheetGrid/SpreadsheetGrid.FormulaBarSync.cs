using System;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Public surface for live formula-bar ↔ inline-editor synchronization (task 9, HOOK ONLY).
/// The orchestrator wires these to MainWindow's formula bar at integration; this control stays
/// unaware of the formula bar. The members here are the entire contract:
/// <list type="bullet">
///   <item><see cref="InlineEditTextChanged"/> — fires on every user keystroke in the inline editor.</item>
///   <item><see cref="SetInlineEditorText"/> — pushes text from the formula bar into the editor.</item>
///   <item><see cref="IsInlineEditing"/> — whether an inline edit is currently active.</item>
/// </list>
/// </summary>
public sealed partial class SpreadsheetGrid
{
    /// <summary>
    /// Raised whenever the inline editor's text changes because the user is typing (or otherwise
    /// editing) in the cell. Not raised for programmatic updates the grid makes itself
    /// (autocomplete, point-mode reference insertion, <see cref="SetInlineEditorText"/>), so a
    /// formula bar can mirror it without feeding the change straight back.
    /// </summary>
    public event Action<string>? InlineEditTextChanged;

    /// <summary>Whether an inline cell edit is currently in progress.</summary>
    public bool IsInlineEditing => _editing;

    /// <summary>Whether an inline edit is in progress and its text is a formula (starts with '=').</summary>
    public bool IsInlineFormulaEdit => _editing && IsFormulaEdit();

    /// <summary>
    /// Commit the active inline edit through the grid's normal path (validation, SetInput, selection
    /// move), so the formula bar's Enter shares one commit lifecycle with in-cell editing (task 9).
    /// </summary>
    public void CommitInlineEdit(int moveRow, int moveColumn) => CommitEdit(moveRow, moveColumn);

    /// <summary>Discard the active inline edit (formula bar's Escape).</summary>
    public void CancelInlineEdit() => CancelEdit();

    /// <summary>
    /// Push text into the active inline editor from outside (the formula bar). Begins an edit on
    /// the active cell if one isn't already running. The update is flagged programmatic so it does
    /// not echo back through <see cref="InlineEditTextChanged"/>. The caret is kept where it was
    /// when possible, and clamped to the end of the new text otherwise, so typing in the formula
    /// bar doesn't make the inline caret jump around.
    /// </summary>
    public void SetInlineEditorText(string text)
    {
        text ??= string.Empty;

        if (!_editing)
        {
            // Mirror the bar without stealing focus: the user is typing in the formula bar. Flag the
            // seeded text as a bar echo so BeginEdit's TextChanged doesn't bounce straight back.
            _programmaticText = text;
            _barEcho = true;
            BeginEdit(text, focus: false);
            _editor.CaretIndex = text.Length;
            return;
        }

        if (_editor.Text == text)
        {
            return; // already in sync — nothing to do, and no spurious TextChanged
        }

        int caret = Math.Min(_editor.CaretIndex, text.Length);
        _programmaticText = text;  // suppress the resulting TextChanged from re-firing as "typing"
        _barEcho = true;           // ...and from echoing back to the bar
        _editor.Text = text;
        _editor.CaretIndex = caret;
    }
}
