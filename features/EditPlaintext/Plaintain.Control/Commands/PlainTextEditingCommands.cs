using Avalonia.Input;
using Plaintain.Control.Views;
using Puffinote.Components.Commands;

namespace Plaintain.Control.Commands;

/// <summary>
/// Plaintain's one command catalog: every operation the embeddable <see cref="PlainTextEditorView"/>
/// offers, published as shared <see cref="AppCommand"/>s — the same arrangement Typage and Cellestra
/// use. A host composes these into its menu, palette, and key bindings, so the standalone app and the
/// Puffinote pane present the same commands under the same chords by construction, not by two lists
/// happening to agree.
///
/// <para>What stays with a host is only what needs a host: opening and saving <i>files</i>, recent
/// documents, preferences, the window. Everything that acts on the open document is here.</para>
///
/// <para><b>⌘K is not ours</b> — it opens the host's command palette, as it does in the siblings.</para>
/// </summary>
public static class PlainTextEditingCommands
{
    /// <summary>Build the catalog bound to <paramref name="view"/>. Call once per view and cache it
    /// (<see cref="PlainTextEditorView.EditingCommands"/> does).</summary>
    public static IReadOnlyList<AppCommand> Build(PlainTextEditorView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var editor = view.Editor;

        return
        [
            new("edit.undo", "Undo", "Step backward through your changes.", Shortcut.Cmd(Key.Z),
                view.Undo, () => view.CanUndo, icon: "undo"),
            new("edit.redo", "Redo", "Re-apply a change you undid.", Shortcut.Cmd(Key.Z, KeyModifiers.Shift),
                view.Redo, () => view.CanRedo, icon: "redo"),
            new("edit.cut", "Cut", "Move the selection to the clipboard.", Shortcut.Cmd(Key.X),
                () => _ = editor.CutAsync(), icon: "scissors"),
            new("edit.copy", "Copy", "Copy the selection to the clipboard.", Shortcut.Cmd(Key.C),
                () => _ = editor.CopyAsync(), icon: "copy"),
            new("edit.paste", "Paste", "Paste from the clipboard.", Shortcut.Cmd(Key.V),
                () => _ = editor.PasteAsync(), icon: "clipboard-paste"),
            new("edit.selectAll", "Select All", "Select the whole document.", Shortcut.Cmd(Key.A), editor.SelectAll),
            new("edit.find", "Find…", "Find text in the document.", Shortcut.Cmd(Key.F),
                view.ShowFind, icon: "search"),

            // ⌥⌘R, not the ⌥⌘W this used to be: Puffinote binds ⌥⌘W to Close All Tabs, and inside a
            // Plaintain tab both bindings sit on the same window — so one of them silently did nothing, with
            // no warning, because the shell's conflict scanner only watches its *reserved* keys and Close
            // All Tabs is not one. The shell's is the older claim, so this moved.
            //
            // It stays in the ⌥⌘ family that Line Numbers below is already in — these are the view toggles —
            // and it keeps ⌘ rather than dropping to a bare ⌥Z the way VS Code spells this: on macOS ⌥ plus a
            // letter *types a character* (⌥Z is Ω), and a plaintext editor is the last place to bind a chord
            // that might insert one. R for "wRap" is a weaker mnemonic than W; a chord that works is worth more.
            new("view.wordWrap", "Word Wrap", "Fold long lines at the window's edge.",
                Shortcut.Cmd(Key.R, KeyModifiers.Alt), view.ToggleWordWrap,
                isActive: () => editor.WordWrap, icon: "wrap-text"),
            new("view.lineNumbers", "Line Numbers", "Show the line-number gutter.",
                Shortcut.Cmd(Key.L, KeyModifiers.Alt), view.ToggleLineNumbers,
                isActive: () => editor.ShowLineNumbers, icon: "list-ordered"),
            new("view.goToLine", "Go to Line…", "Jump to a line by number.", Shortcut.Cmd(Key.G),
                () => GoToLineRequested?.Invoke(view), icon: "list"),

            .. ZoomCommands.Build(() => view.Zoom, zoom => view.Zoom = zoom),
        ];
    }

    /// <summary>
    /// Raised by "Go to Line…" so a host can ask for the number in its own idiom — a sheet on macOS, an
    /// overlay in Puffinote. The command belongs to the editor (it acts on the document); only the
    /// prompt belongs to whoever owns the window.
    /// </summary>
    public static event Action<PlainTextEditorView>? GoToLineRequested;
}
