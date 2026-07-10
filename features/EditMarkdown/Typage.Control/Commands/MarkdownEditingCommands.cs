using Avalonia.Input;
using Puffinote.Components.Commands;
using Typage.Control.Views;

namespace Typage.Control.Commands;

/// <summary>
/// The Markdown editor's own command catalog: the editing/formatting operations the embeddable
/// <see cref="MarkdownEditorView"/> offers, published as shared <see cref="AppCommand"/>s — mirroring how
/// <c>Cellestra.Editor</c> exposes its catalog through the view. A host app composes these into its Command
/// Palette, menu, and toolbar. The <em>shell</em>-level commands (file open/save, find bar, link/image
/// dialogs, view mode, preferences) deliberately stay with the host, since they need the window, dialogs,
/// and settings the control doesn't own.
/// </summary>
public static class MarkdownEditingCommands
{
    /// <summary>Build the editor catalog bound to <paramref name="view"/> (fresh instances each call).</summary>
    public static IReadOnlyList<AppCommand> Build(MarkdownEditorView view)
    {
        var ed = view.Editor;
        return
        [
            new("edit.undo", "Undo", "Step backward through your changes.", Shortcut.Cmd(Key.Z),
                () => view.Undo(), () => view.CanUndo, icon: "undo"),
            new("edit.redo", "Redo", "Re-apply a change you undid.", Shortcut.Cmd(Key.Z, KeyModifiers.Shift),
                () => view.Redo(), () => view.CanRedo, icon: "redo"),
            new("edit.cut", "Cut", "Move the selection to the clipboard.", Shortcut.Cmd(Key.X), () => _ = ed.CutAsync(), icon: "scissors"),
            new("edit.copy", "Copy", "Copy the selection to the clipboard.", Shortcut.Cmd(Key.C), () => _ = ed.CopyAsync(), icon: "copy"),
            new("edit.paste", "Paste", "Paste from the clipboard.", Shortcut.Cmd(Key.V), () => _ = ed.PasteAsync(), icon: "clipboard-paste"),
            new("edit.selectAll", "Select All", "Select the whole document.", Shortcut.Cmd(Key.A), ed.SelectAll),
            new("edit.moveLineUp", "Move Line Up", "Move the current line or block up.",
                Shortcut.Bare(Key.Up, KeyModifiers.Alt | KeyModifiers.Shift), ed.MoveLineUp),
            new("edit.moveLineDown", "Move Line Down", "Move the current line or block down.",
                Shortcut.Bare(Key.Down, KeyModifiers.Alt | KeyModifiers.Shift), ed.MoveLineDown),
            new("edit.duplicateLine", "Duplicate Line", "Duplicate the current line or selection.", Shortcut.Cmd(Key.D), ed.DuplicateLine),
            new("edit.deleteLine", "Delete Line", "Delete the current line or selection.", Shortcut.Cmd(Key.K, KeyModifiers.Shift), ed.DeleteLine),
            new("edit.selectWord", "Select Word", "Select the word under the caret.", null, ed.SelectCurrentWord),
            new("edit.selectParagraph", "Select Paragraph", "Select the paragraph under the caret.", null, ed.SelectCurrentParagraph),

            new("format.bold", "Bold", "Make the selection bold.", Shortcut.Cmd(Key.B), ed.ToggleBold, icon: "bold"),
            new("format.italic", "Italic", "Make the selection italic.", Shortcut.Cmd(Key.I), ed.ToggleItalic, icon: "italic"),
            new("format.code", "Inline Code", "Format the selection as inline code.", Shortcut.Cmd(Key.E), ed.ToggleInlineCode),
            new("format.strike", "Strikethrough", "Strike through the selection.", Shortcut.Cmd(Key.X, KeyModifiers.Shift),
                ed.ToggleStrikethrough, icon: "strikethrough"),
            new("format.highlight", "Highlight", "Highlight the selection (==text==).", Shortcut.Cmd(Key.H, KeyModifiers.Shift),
                ed.ToggleHighlight, icon: "paintbrush"),
            new("format.task", "Checkbox", "Add a task checkbox to this line, or toggle it.", Shortcut.Cmd(Key.L),
                ed.ToggleTask, icon: "square-check"),
            new("format.removeTask", "Remove Checkbox", "Remove the task checkbox from this line.", Shortcut.Cmd(Key.L, KeyModifiers.Shift),
                ed.RemoveTask, icon: "square-x"),
            new("format.quote", "Blockquote", "Toggle a blockquote on the line(s).", null, ed.ToggleBlockquote),
            new("format.bulletList", "Bulleted List", "Toggle a bulleted list on the line(s).", Shortcut.Cmd(Key.D8, KeyModifiers.Shift),
                ed.ToggleBulletList, icon: "list"),
            new("format.numberedList", "Numbered List", "Toggle a numbered list on the line(s).", Shortcut.Cmd(Key.D7, KeyModifiers.Shift),
                ed.ToggleNumberedList, icon: "list-x"),
            new("format.h1", "Heading 1", "Make this line a level-1 heading.", Shortcut.Cmd(Key.D1), () => ed.ApplyHeading(1), icon: "type"),
            new("format.h2", "Heading 2", "Make this line a level-2 heading.", Shortcut.Cmd(Key.D2), () => ed.ApplyHeading(2), icon: "type"),
            new("format.h3", "Heading 3", "Make this line a level-3 heading.", Shortcut.Cmd(Key.D3), () => ed.ApplyHeading(3), icon: "type"),
            new("format.h4", "Heading 4", "Make this line a level-4 heading.", Shortcut.Cmd(Key.D4), () => ed.ApplyHeading(4)),
            new("format.h5", "Heading 5", "Make this line a level-5 heading.", Shortcut.Cmd(Key.D5), () => ed.ApplyHeading(5)),
            new("format.h6", "Heading 6", "Make this line a level-6 heading.", Shortcut.Cmd(Key.D6), () => ed.ApplyHeading(6)),
            new("format.paragraph", "Paragraph", "Remove the heading from this line.", Shortcut.Cmd(Key.D0), ed.SetParagraph, icon: "remove-formatting"),

            new("insert.codeBlock", "Code Block", "Insert a fenced code block.", Shortcut.Cmd(Key.E, KeyModifiers.Shift), ed.InsertCodeBlock),
            new("insert.hr", "Horizontal Rule", "Insert a horizontal rule.", null, ed.InsertHorizontalRule),
        ];
    }
}
