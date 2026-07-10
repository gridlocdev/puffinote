using Avalonia.Input;
using Puffinote.Components.Commands;

namespace Cellestra.Editor;

public partial class SpreadsheetEditorView
{
    private void RegisterKeyBindings()
    {
        foreach (var command in Commands.All)
        {
            if (command.Gesture is null)
            {
                continue;
            }

            var captured = command;
            var binding = new RelayCommand(
                () => captured.Execute(),
                () => !IsModalOpen && !BlockedByTextInput(captured.Gesture) && captured.IsEnabled);
            _gatedCommands.Add(binding);
            KeyBindings.Add(new KeyBinding
            {
                Gesture = command.Gesture,
                // While a modal overlay (Command Palette / sheet switcher) is open, the grid behind it
                // must not react to command shortcuts — only the overlay's own keys apply. So every
                // global binding backs off, letting the focused overlay field handle the key (e.g. ⌘A
                // selects the palette text, not all cells). Likewise, a plain-key gesture (no ⌘/⌃, e.g.
                // Delete) must not fire its global cell command while a text input is focused — there it
                // is ordinary in-textbox editing, not "clear the cell" (#8).
                Command = binding,
            });
        }
    }

    /// <summary>
    /// Re-publish CanExecute for every gated shortcut so a native macOS menu item refreshes its
    /// enabled state. Called whenever a modal overlay opens or closes: without it the menu's cached
    /// "enabled" persists and macOS keeps firing a ⌘-shortcut's key equivalent (e.g. ⌘A → Select All)
    /// against the grid behind the modal instead of letting the overlay handle the key.
    /// </summary>
    private void RefreshCommandStates()
    {
        foreach (var command in _gatedCommands)
        {
            command.RaiseCanExecuteChanged();
        }

        Grid.InputBlocked = IsModalOpen;

        // A host's native-menu items gate on the same modal/text-input state; let it re-publish them.
        CommandStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True when a command shortcut may run now — no modal overlay is open and no focused text
    /// input claims the gesture. Exposed so a host can gate its native-menu items on the editor's state
    /// (it adds the command's own IsEnabled check).</summary>
    public bool CanRunShortcut(KeyGesture? shortcut) => !IsModalOpen && !BlockedByTextInput(shortcut);

    // An active inline edit counts the same as a focused text field: while a cell is being edited
    // (in the cell or mirrored from the formula bar), a plain key like Delete is in-editor text
    // editing, not "clear the cell". This explicit flag is steadier than focus detection alone and,
    // because the gate lives in CanExecute, it makes the binding/native-menu hotkey inert so the key
    // flows through to the editor — Avalonia evaluates these gestures before the focused element ever
    // sees the KeyDown, so e.Handled in the editor can't claw it back after the fact.
    private bool BlockedByTextInput(KeyGesture? gesture) =>
        KeyGestureRules.ShouldYieldToTextInput(gesture, IsTextInputFocused || Grid.IsInlineEditing);
}
