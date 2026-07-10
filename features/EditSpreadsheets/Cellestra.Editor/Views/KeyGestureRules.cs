using Avalonia.Input;

namespace Cellestra.Editor;

/// <summary>
/// Pure rules for when a command's key gesture must yield to a focused text field (cell editor,
/// formula bar, name box, dialog field) rather than fire its global cell command. Extracted from
/// <see cref="SpreadsheetEditorView"/> so the gating logic is unit-testable without a window.
/// </summary>
internal static class KeyGestureRules
{
    /// <summary>
    /// True when a global binding must yield to a focused text input (#8): the gesture is a plain key
    /// (no ⌘/⌃) — like Delete or Backspace — and a <see cref="TextBox"/> (cell editor, formula bar, name
    /// box, dialog field) holds focus, so the key does ordinary in-textbox editing instead of firing the
    /// cell command (e.g. ClearContents). Modifier shortcuts (⌘C, ⌘V, ⌘Z…) still apply, and the textbox's
    /// own handlers take precedence over them anyway.
    /// </summary>
    internal static bool ShouldYieldToTextInput(KeyGesture? gesture, bool textInputFocused) =>
        textInputFocused && YieldsToTextInput(gesture);

    /// <summary>
    /// True when a gesture also has a meaning inside a focused text field and must therefore yield to
    /// it — so it gets no native-menu key equivalent and a global binding backs off while a
    /// <see cref="TextBox"/> (cell editor, formula bar, name box, dialog field) is focused. Two families
    /// qualify: <see cref="IsPlainKeyGesture"/> (Delete/Backspace/bare keys) and the standard editing
    /// chords (<see cref="IsTextEditingChord"/> — select-all, copy/cut/paste, undo/redo). Otherwise — on
    /// macOS in particular — the system claims the key ahead of the editor and, because a disabled native
    /// item still swallows its equivalent (Avalonia #7133), the editor never sees it: e.g. ⌘A would
    /// Select All cells instead of the text being edited.
    /// </summary>
    internal static bool YieldsToTextInput(KeyGesture? gesture) =>
        IsPlainKeyGesture(gesture) || IsTextEditingChord(gesture);

    /// <summary>
    /// True for a "plain-key" gesture — one with no ⌘/⌃ modifier (Delete, Backspace, a bare letter).
    /// </summary>
    internal static bool IsPlainKeyGesture(KeyGesture? gesture) =>
        gesture is not null && (gesture.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) == 0;

    /// <summary>
    /// True for the standard text-editing chords every text field handles itself: ⌘A select-all,
    /// ⌘C copy, ⌘X cut, ⌘V paste, ⌘Z undo and ⌘⇧Z redo (⌃ on non-mac). The modifier set must be
    /// exactly the primary accelerator (plus Shift only for redo) so sibling chords on the same letter
    /// are excluded — ⌘⇧X is Strikethrough (not Cut), ⌘⇧C is Format Painter (not Copy), ⌘⌥V is Paste
    /// Special (a cell-only op) — those keep firing their command even while editing.
    /// </summary>
    internal static bool IsTextEditingChord(KeyGesture? gesture)
    {
        if (gesture is null)
        {
            return false;
        }

        var mods = gesture.KeyModifiers;
        bool primaryOnly = mods == KeyModifiers.Meta || mods == KeyModifiers.Control;
        bool primaryShift = mods == (KeyModifiers.Meta | KeyModifiers.Shift) ||
            mods == (KeyModifiers.Control | KeyModifiers.Shift);

        return gesture.Key switch
        {
            Key.A or Key.C or Key.X or Key.V => primaryOnly,  // select-all / copy / cut / paste
            Key.Z => primaryOnly || primaryShift,             // undo / redo
            _ => false,
        };
    }
}
