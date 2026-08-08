using Avalonia.Input;
using Puffinote.Components.Commands;
using Typage.Control.Controls;
using Typage.Control.Views;

namespace Typage.Control.Commands;

/// <summary>
/// The Markdown editor's one command catalog: every editing, formatting, find, view and insert operation the
/// embeddable <see cref="MarkdownEditorView"/> offers, published as shared <see cref="AppCommand"/>s —
/// mirroring how <c>Cellestra.Editor</c> exposes its catalog through the view. Every host composes these into
/// its Command Palette, menu, toolbar <em>and key bindings</em>, so the standalone app and the Puffinote shell
/// present the same commands under the same shortcuts, by construction rather than by two lists agreeing.
/// <para>
/// What stays with a host is only what needs a host: opening and saving <em>files</em>, recent documents,
/// preferences, the window itself. Anything that acts on the open document — including the dialogs that do,
/// like the link prompt — is here, because it is the editor's, not the shell's.
/// </para>
/// <para>
/// <b>⌘K is not ours.</b> It opens the host's command palette (Puffinote reserves it globally, and the
/// standalone app matches). The link prompt therefore takes <b>⌘⌥K</b>, which is also what Cellestra's
/// <c>insert.hyperlink</c> uses — one family, one meaning per chord.
/// </para>
/// </summary>
public static class MarkdownEditingCommands
{
    /// <summary>
    /// Build the editor catalog bound to <paramref name="view"/>. Call it once per view and cache the result
    /// (<see cref="MarkdownEditorView.EditingCommands"/> does), so a bar, a palette and a key binding all
    /// share one command instance.
    /// </summary>
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
            new("edit.find", "Find…", "Find text in the document.", Shortcut.Cmd(Key.F),
                () => view.ShowFind(replace: false), icon: "search"),
            new("edit.replace", "Find & Replace…", "Find text and replace it (plain or regex).", Shortcut.Cmd(Key.F, KeyModifiers.Alt),
                () => view.ShowFind(replace: true), icon: "replace"),

            new("format.bold", "Bold", "Make the selection bold.", Shortcut.Cmd(Key.B), ed.ToggleBold, isActive: () => ed.IsBoldActive, icon: "bold"),
            new("format.italic", "Italic", "Make the selection italic.", Shortcut.Cmd(Key.I), ed.ToggleItalic, isActive: () => ed.IsItalicActive, icon: "italic"),
            new("format.code", "Inline Code", "Format the selection as inline code.", Shortcut.Cmd(Key.E), ed.ToggleInlineCode, isActive: () => ed.IsInlineCodeActive, icon: "code"),
            new("format.strike", "Strikethrough", "Strike through the selection.", Shortcut.Cmd(Key.X, KeyModifiers.Shift),
                ed.ToggleStrikethrough, isActive: () => ed.IsStrikethroughActive, icon: "strikethrough"),
            new("format.highlight", "Highlight", "Highlight the selection (==text==).", Shortcut.Cmd(Key.H, KeyModifiers.Shift),
                ed.ToggleHighlight, isActive: () => ed.IsHighlightActive, icon: "paintbrush"),
            new("format.task", "Checkbox", "Add a task checkbox to this line, or toggle it done.", Shortcut.Cmd(Key.L),
                ed.ToggleTask, isActive: () => ed.IsTaskActive, icon: "square-check"),
            // The four states between "not started" and "done" — in progress, blocked, deferred, cancelled — are
            // chosen, not toggled, so they get a picker rather than four more shortcuts to remember (PRD §8.3.2).
            new("format.taskStatus", "Task Status…", "Set this task's status: not started, in progress, or done.",
                Shortcut.Cmd(Key.L, KeyModifiers.Alt), ed.OpenTaskStatusPicker, isActive: () => ed.IsTaskActive, icon: "circle-dot"),
            new("format.removeTask", "Remove Checkbox", "Remove the task checkbox from this line.", Shortcut.Cmd(Key.L, KeyModifiers.Shift),
                ed.RemoveTask, icon: "square-x"),
            new("format.quote", "Blockquote", "Toggle a blockquote on the line(s).", null, ed.ToggleBlockquote, isActive: () => ed.IsQuoteActive, icon: "text-quote"),
            new("format.bulletList", "Bulleted List", "Toggle a bulleted list on the line(s).", Shortcut.Cmd(Key.OemPeriod),
                ed.ToggleBulletList, isActive: () => ed.IsBulletListActive, icon: "list"),
            new("format.numberedList", "Numbered List", "Toggle a numbered list on the line(s).", Shortcut.Cmd(Key.OemQuestion),
                ed.ToggleNumberedList, isActive: () => ed.IsNumberedListActive, icon: "list-ordered"),
            // Each level gets its own numbered glyph. They used to share one "type" (and H4–H6 had none at
            // all), so the picker read as three identical icons over three blanks — the one thing the list is
            // there to tell apart, the level, was the one thing the icons didn't say.
            new("format.h1", "Heading 1", "Make this line a level-1 heading.", Shortcut.Cmd(Key.D1), () => ed.ApplyHeading(1), isActive: () => ed.CaretHeadingLevel == 1, icon: "heading-1"),
            new("format.h2", "Heading 2", "Make this line a level-2 heading.", Shortcut.Cmd(Key.D2), () => ed.ApplyHeading(2), isActive: () => ed.CaretHeadingLevel == 2, icon: "heading-2"),
            new("format.h3", "Heading 3", "Make this line a level-3 heading.", Shortcut.Cmd(Key.D3), () => ed.ApplyHeading(3), isActive: () => ed.CaretHeadingLevel == 3, icon: "heading-3"),
            new("format.h4", "Heading 4", "Make this line a level-4 heading.", Shortcut.Cmd(Key.D4), () => ed.ApplyHeading(4), isActive: () => ed.CaretHeadingLevel == 4, icon: "heading-4"),
            new("format.h5", "Heading 5", "Make this line a level-5 heading.", Shortcut.Cmd(Key.D5), () => ed.ApplyHeading(5), isActive: () => ed.CaretHeadingLevel == 5, icon: "heading-5"),
            new("format.h6", "Heading 6", "Make this line a level-6 heading.", Shortcut.Cmd(Key.D6), () => ed.ApplyHeading(6), isActive: () => ed.CaretHeadingLevel == 6, icon: "heading-6"),
            // ⌘0 completes the heading set — ⌘1–⌘6 apply a level, ⌘0 takes it away — so it stays here, and
            // zoom's Actual Size takes ⌥⌘0 instead (see ZoomCommands).
            new("format.paragraph", "Paragraph", "Remove the heading from this line.", Shortcut.Cmd(Key.D0), ed.SetParagraph, icon: "remove-formatting"),

            // ⌘⌥K, not ⌘K: the palette owns ⌘K in both shells (see the type remarks).
            new("format.link", "Insert Link…", "Insert a link with display text and a location.", Shortcut.Cmd(Key.K, KeyModifiers.Alt),
                () => _ = view.ShowLinkDialogAsync(), icon: "link"),

            // Editing one is its own command, not a mode of inserting: it applies only where there *is* a link
            // (so it greys out elsewhere), and it opens with that link's text and destination already in the
            // fields. It carries no chord of its own — it's reached from the palette, the menu, or the link's
            // own right-click menu, which is where a reader already goes to change a link.
            new("format.editLink", "Edit Link…", "Edit the link the caret is in.", null,
                () => _ = view.ShowEditLinkDialogAsync(), canExecute: () => view.CanEditLink, icon: "pencil"),
            new("insert.image", "Image…", "Pick an image file to insert.", null, () => _ = view.InsertImageAsync(), icon: "image"),

            // "square-code" is the inline-code chevrons boxed in — the box is what says *block* rather than
            // inline; "minus" is the rule itself.
            new("insert.codeBlock", "Code Block", "Insert a fenced code block.", Shortcut.Cmd(Key.E, KeyModifiers.Shift), ed.InsertCodeBlock, icon: "square-code"),
            new("insert.hr", "Horizontal Rule", "Insert a horizontal rule.", null, ed.InsertHorizontalRule, icon: "minus"),

            // The view modes are a radio set, so each lights when it's the one in force.
            new("view.viewRich", "View Rich View", "Live preview: formatted, with Markdown markers hidden.", null,
                () => view.ViewMode = MarkdownEditor.ViewMode.Rich,
                isActive: () => view.ViewMode == MarkdownEditor.ViewMode.Rich, icon: "eye"),
            new("view.viewFormatted", "View Formatted View", "Formatted, with the raw Markdown markers revealed.", null,
                () => view.ViewMode = MarkdownEditor.ViewMode.Formatted,
                isActive: () => view.ViewMode == MarkdownEditor.ViewMode.Formatted, icon: "pilcrow"),
            new("view.viewRaw", "View Raw Text", "Plain-text source: no rich formatting or decorations.", null,
                () => view.ViewMode = MarkdownEditor.ViewMode.RawText,
                isActive: () => view.ViewMode == MarkdownEditor.ViewMode.RawText, icon: "file-text"),

            .. ZoomCommands.Build(() => view.Zoom, zoom => view.Zoom = zoom),

            new("file.exportHtml", "Export HTML…", "Render the document to a self-contained HTML file.", null,
                () => _ = view.ExportHtmlAsync(), icon: "file-down"),
            new("help.reference", "Markdown Reference", "Open the offline syntax & shortcut reference.", null,
                view.ShowReference, icon: "book-open"),
        ];
    }
}
