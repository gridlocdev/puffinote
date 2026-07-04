using Avalonia.Input;
using Typage.Engine.Document;
using Typage.Engine.Editing;
using Typage.Engine.Markdown;

namespace Typage.Control.Controls;

public partial class MarkdownEditor
{
    protected override void OnTextInput(TextInputEventArgs e)
    {
        // A Cmd/Ctrl+key combination is a command, not text — don't type the letter. (Ctrl+Alt is
        // left alone so AltGr characters still work.)
        var command = _modifiers.HasFlag(KeyModifiers.Meta)
            || (_modifiers.HasFlag(KeyModifiers.Control) && !_modifiers.HasFlag(KeyModifiers.Alt));

        if (!command && !string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
        {
            if (e.Text.Length != 1 || !TryAutoPair(e.Text[0]))
            {
                InsertTypedText(e.Text);
            }
        }

        e.Handled = true;
        base.OnTextInput(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        _modifiers = e.KeyModifiers;
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var ctrl = control || meta; // the platform "command" modifier for the formatting/clipboard shortcuts
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var isMac = OperatingSystem.IsMacOS();
        var wordMod = isMac ? alt : control; // word-wise nav/delete: ⌥ on macOS, Ctrl elsewhere
        var doc = Buffer.Document;
        var caret = Buffer.Selection.Caret;
        var handled = true;

        // Any key other than a continued line move ends the reorder drop-indicator session.
        if (!(alt && shift && e.Key is Key.Up or Key.Down))
        {
            ClearReorderIndicator();
        }

        switch (e.Key)
        {
            // --- deletion (⌘⌫ to line start on macOS, then word-wise) ---
            case Key.Back when isMac && meta:
                DeleteToRowStart(caret);
                break;
            case Key.Back when wordMod:
                DeleteWord(-1);
                break;
            case Key.Delete when wordMod:
                DeleteWord(1);
                break;
            case Key.Back:
                if (!TryBackspacePair() && !TryBackspaceCollapsedBlank() && !TryVisualBackspace())
                {
                    Buffer.Backspace();
                }
                break;
            case Key.Delete:
                if (!TryDeleteCollapsedBlank() && !TryVisualDelete())
                {
                    Buffer.Delete();
                }
                break;

            case Key.Enter when shift:
                Buffer.Insert(DetectNewline(Buffer.Document));
                break; // plain newline (doc's ending), no list continuation
            case Key.Enter:
                HandleEnter();
                break;
            case Key.Tab:
                HandleTab(shift);
                break;

            // --- horizontal navigation: word, then macOS ⌘-line, then by grapheme ---
            case Key.Left when wordMod:
                MoveCaret(WordBoundary(caret, -1), shift);
                break;
            case Key.Right when wordMod:
                MoveCaret(WordBoundary(caret, 1), shift);
                break;
            case Key.Left when isMac && meta:
                MoveCaret(RowStartOffset(caret), shift);
                break;
            case Key.Right when isMac && meta:
                MoveCaret(RowEndOffset(caret), shift);
                break;
            case Key.Left:
                MoveCaret(VisualStep(caret, -1), shift);
                break;
            case Key.Right:
                MoveCaret(VisualStep(caret, 1), shift);
                break;

            // --- vertical navigation: move lines, doc ends (⌘↑/↓ on macOS), else by visual row ---
            case Key.Up when alt && shift:
                MoveLines(-1);
                break;
            case Key.Down when alt && shift:
                MoveLines(1);
                break;
            case Key.Up when isMac && meta:
                MoveCaret(0, shift);
                break;
            case Key.Down when isMac && meta:
                MoveCaret(doc.Length, shift);
                break;
            case Key.Up:
                ArrowByLine(-1, shift);
                break;
            case Key.Down:
                ArrowByLine(1, shift);
                break;
            case Key.PageUp:
                MoveCaretByLine(-PageRows(), shift);
                break;
            case Key.PageDown:
                MoveCaretByLine(PageRows(), shift);
                break;

            // --- line / document extents ---
            case Key.Home when control:
                MoveCaret(0, shift);
                break;        // Ctrl+Home → document start
            case Key.End when control:
                MoveCaret(doc.Length, shift);
                break; // Ctrl+End → document end
            case Key.Home:
                MoveCaret(SmartHome(caret), shift);
                break;       // content start ⇄ visual-row start
            case Key.End:
                MoveCaret(RowEndOffset(caret), shift);
                break;     // end of the visual row

            case Key.Escape when !Buffer.Selection.IsEmpty:
                MoveCaret(Buffer.Selection.Caret, extend: false);
                break;

            // Clipboard. (⌘⇧X is strikethrough below — it must be matched before plain ⌘X = Cut.)
            case Key.C when ctrl:
                _ = CopyAsync();
                break;
            case Key.X when ctrl && shift:
                ToggleStrikethrough();
                break;
            case Key.X when ctrl:
                _ = CutAsync();
                break;
            case Key.V when ctrl:
                _ = PasteAsync();
                break;

            // Formatting (the shortcuts a Markdown editor is expected to have).
            case Key.B when ctrl:
                ToggleBold();
                break;
            case Key.I when ctrl:
                ToggleItalic();
                break;
            case Key.E when ctrl && shift:
                InsertCodeBlock();
                break; // ⌘⇧E fenced block (before ⌘E inline code)
            case Key.E when ctrl:
                ToggleInlineCode();
                break;
            // ⌘K is owned by the shell: it bubbles to the window, which opens the link dialog (display + location).
            case Key.L when ctrl:
                ToggleTask();
                break;
            case Key.D0 when ctrl:
                SetParagraph();
                break;
            case >= Key.D1 and <= Key.D6 when ctrl:
                ApplyHeading(e.Key - Key.D1 + 1);
                break;

            case Key.A when ctrl:
                SelectAll();
                break;
            case Key.Z when ctrl && shift:
                Buffer.Redo();
                break;
            case Key.Z when ctrl:
                Buffer.Undo();
                break;
            case Key.Y when ctrl:
                Buffer.Redo();
                break;
            default:
                handled = false;
                break;
        }

        e.Handled = handled;
        base.OnKeyDown(e);
    }

    // ----- editing assists (PRD §7.2): list/quote continuation, indent, auto-pairing -----

    /// <summary>Enter: continue or exit a list/blockquote, auto-indent, or insert a plain newline (PRD §7.2).</summary>
    private void HandleEnter()
    {
        var sel = Buffer.Selection;
        var doc = Buffer.Document;

        // A plain paragraph break is "\n\n", so the two halves are separate Markdown paragraphs (a single
        // newline is a soft break the tokenizer flows back together — the old "teleport"). Two refinements
        // keep the *visible* result to exactly one new line break in every position:
        //   • Only the part before the caret being real content warrants the paragraph "\n\n"; at a line
        //     start / on a blank line a single "\n" is enough (there is no collapsible blank above to absorb
        //     the second newline, so it would otherwise open a double gap).
        //   • If a newline already follows the break point, that paragraph break already exists, so we add
        //     just one "\n". The caret then skips the (now collapsed) first blank to land on the visible row.
        // The blank a "\n\n" leaves renders as regular spacing (IsCollapsedBlankLine).
        // Insert the document's own line ending (CRLF stays CRLF) so editing never mixes \n into a \r\n
        // file — golden rule #5, byte-for-byte line endings.
        var nl = DetectNewline(doc);
        var breakLine = doc.GetPosition(sel.Start).Line;
        var beforeBreak = doc.GetText(doc.GetLineStart(breakLine), sel.Start - doc.GetLineStart(breakLine));
        var beforeContent = beforeBreak.Trim().Length > 0;
        // A newline already following the break point — '\n', or the '\r' of a CRLF pair — means the
        // paragraph break exists, so add just one line ending rather than opening a double gap.
        var afterNewline = sel.End < doc.Length && doc[sel.End] is '\n' or '\r';
        var paraBreak = beforeContent && !afterNewline ? nl + nl : nl;
        var paraCaret = sel.Start + (beforeContent ? 2 : 1) * nl.Length; // past the break, skipping a collapsed first blank

        void PlaceParaCaret() => Buffer.Selection = Selection.At(Math.Clamp(paraCaret, 0, Buffer.Document.Length));

        if (!sel.IsEmpty)
        {
            Buffer.ReplaceRange(sel.Start, sel.Length, paraBreak); // replace the selection with the break
            PlaceParaCaret();
            return;
        }

        var line = breakLine;
        var lineStart = doc.GetLineStart(line);
        var lineText = doc.GetLineText(line);
        var caretCol = sel.Caret - lineStart;

        // Enter with the caret left of a non-empty list/quote item's content (anywhere in its marker
        // prefix — e.g. on the left of a checkbox or bullet) must slide the whole item, indentation and
        // marker intact, down by one line — not splice a duplicate "- "/"1. "/"[ ] " into it. Insert the
        // newline at the line start so nothing of the prefix is split, and let the caret ride the item.
        var prefix = LinePrefix.Parse(lineText);
        if ((prefix.HasList || prefix.HasQuote) && caretCol < prefix.Length
            && lineText.AsSpan(prefix.Length).Trim().Length > 0)
        {
            Buffer.ReplaceRange(lineStart, 0, nl);
            Buffer.Selection = Selection.At(sel.Caret + nl.Length);
            return;
        }

        var result = LinePrefix.ComputeEnter(lineText, caretCol);

        switch (result.Kind)
        {
            case EnterKind.Exit:
                Buffer.ReplaceRange(lineStart + result.ExitStart, result.ExitLength, string.Empty);
                break;
            case EnterKind.Continue:
                Buffer.ReplaceRange(sel.Caret, 0, result.Text.Replace("\n", nl)); // match the doc's line ending
                RenumberOrderedFrom(line + 1); // resequence following items if we split an ordered list
                break;
            default:
                Buffer.ReplaceRange(sel.Caret, 0, paraBreak);
                PlaceParaCaret();
                break;
        }
    }

    /// <summary>Tab/Shift+Tab: indent or outdent list items / selected lines; otherwise insert spaces.</summary>
    private void HandleTab(bool shift)
    {
        var sel = Buffer.Selection;
        var doc = Buffer.Document;
        var startLine = doc.GetPosition(sel.Start).Line;
        var endLine = doc.GetPosition(sel.End).Line;
        var caretLine = doc.GetPosition(sel.Caret).Line;
        var multiLine = endLine > startLine;
        var prefix = LinePrefix.Parse(doc.GetLineText(caretLine));

        // Plain text, single caret, not on a list item → a Tab inserts one indent unit (a tab where the
        // line already uses tabs, otherwise spaces).
        if (!shift && !multiLine && sel.IsEmpty && !prefix.HasList)
        {
            Buffer.Insert(IndentUnit(caretLine, caretLine));
            return;
        }

        // Shift+Tab on a single nestable item (list/quote/task) that is already at the outermost level
        // (no indentation left to remove) exits the construct: strip the whole marker prefix, leaving the
        // content as a plain line — so an empty item outdents to an empty line (PRD §7.2 list exit).
        if (shift && !multiLine && (prefix.HasList || prefix.HasQuote) && prefix.Indent == 0)
        {
            var lineStart = doc.GetLineStart(caretLine);
            var caret = sel.Caret;
            Buffer.ReplaceRange(lineStart, prefix.Length, string.Empty);
            Buffer.Selection = Selection.At(Math.Max(lineStart, caret - prefix.Length));
            RenumberOrderedRun(caretLine); // exiting an item may renumber the remaining ordered list
            return;
        }

        // IndentLines folds the ordered-list renumber into its own single edit (so indent + renumber is one
        // undo step), keeping the source's per-level numbering in step with the preview.
        IndentLines(startLine, endLine, outdent: shift);
    }

    /// <summary>
    /// The indentation unit to add — a tab when the region (or the line above it) already uses tab
    /// indentation, otherwise <see cref="IndentWidth"/> spaces — so indenting preserves whichever style was
    /// entered in the text.
    /// </summary>
    private string IndentUnit(int startLine, int endLine)
    {
        var doc = Buffer.Document;
        bool StartsWithTab(int l)
        {
            var t = doc.GetLineText(l);
            return t.Length > 0 && t[0] == '\t';
        }

        if (startLine > 0 && StartsWithTab(startLine - 1))
        {
            return "\t";
        }

        for (var l = startLine; l <= endLine; l++)
        {
            if (StartsWithTab(l))
            {
                return "\t";
            }
        }

        return new string(' ', Math.Max(1, IndentWidth));
    }

    /// <summary>
    /// Add or remove one indent level on lines <paramref name="startLine"/>..<paramref name="endLine"/> as a
    /// single edit, then renumber the ordered items in the rest of the run so changed nesting levels keep
    /// 1-based, per-level numbering in the source (matching the preview). One <see cref="DocumentBuffer.ReplaceRange"/>.
    /// </summary>
    private void IndentLines(int startLine, int endLine, bool outdent)
    {
        var doc = Buffer.Document;
        var sel = Buffer.Selection;
        var unit = IndentUnit(startLine, endLine);
        var maxSpaces = Math.Max(1, IndentWidth);

        // Extend the rewrite down over the rest of this non-blank run so renumbering can also fix following
        // items whose level changed; only [startLine, endLine] is actually indented/outdented.
        var runEnd = endLine;
        while (runEnd + 1 < doc.LineCount && !IsBlankLine(runEnd + 1))
        {
            runEnd++;
        }

        var newLines = new List<string>(runEnd - startLine + 1);
        var headDelta = new int[runEnd - startLine + 1]; // chars inserted(+)/removed(-) at each line's head
        for (var l = startLine; l <= runEnd; l++)
        {
            var text = LineContent(doc, l);
            if (l > endLine)
            {
                newLines.Add(text); // past the selected lines: untouched here, only (maybe) renumbered below
                continue;
            }

            if (outdent)
            {
                // Remove one indent level: a single leading tab, or up to IndentWidth leading spaces.
                var remove = 0;
                if (text.Length > 0 && text[0] == '\t')
                {
                    remove = 1;
                }
                else
                {
                    while (remove < maxSpaces && remove < text.Length && text[remove] == ' ')
                    {
                        remove++;
                    }
                }

                newLines.Add(text[remove..]);
                headDelta[l - startLine] = -remove;
            }
            else if (text.Length == 0 && startLine != endLine)
            {
                newLines.Add(text); // don't indent blank lines inside a multi-line block
            }
            else
            {
                newLines.Add(unit + text);
                headDelta[l - startLine] = unit.Length;
            }
        }

        var info = RenumberOrdered(newLines, startLine, doc.GetLineText);

        ReplaceLines(startLine, runEnd, newLines);

        // Re-place the selection by line+column: a line's head insert/remove shifts its content, and a
        // renumber past the marker shifts a caret sitting in the item's content.
        var moved = Buffer.Document;
        int Place(int line, int col)
        {
            var i = line - startLine;
            if (i >= 0 && i < newLines.Count)
            {
                col = Math.Max(0, col + headDelta[i]);
                if (col > info[i].MarkerStart)
                {
                    col += info[i].DigitDelta;
                }
            }

            return moved.GetLineStart(line) + col;
        }

        var startL = doc.GetPosition(sel.Start).Line;
        var endL = doc.GetPosition(sel.End).Line;
        var ns = Place(startL, sel.Start - doc.GetLineStart(startL));
        var ne = Place(endL, sel.End - doc.GetLineStart(endL));
        Buffer.Selection = sel.Anchor <= sel.Caret ? new Selection(ns, ne) : new Selection(ne, ns);
    }

    /// <summary>Wrap a selection in a pair, type through a closer, or auto-close a bracket. Returns true if it consumed the key.</summary>
    private bool TryAutoPair(char c)
    {
        var sel = Buffer.Selection;
        var doc = Buffer.Document;

        // Wrap a non-empty selection in the typed pair, keeping the original text selected inside it.
        if (!sel.IsEmpty && AutoPairs.TryGetValue(c, out var closeWrap))
        {
            var inner = doc.GetText(sel.Start, sel.Length);
            Buffer.ReplaceRange(sel.Start, sel.Length, c + inner + closeWrap);
            Buffer.Selection = new Selection(sel.Start + 1, sel.Start + 1 + inner.Length);
            return true;
        }

        if (!sel.IsEmpty)
        {
            return false;
        }

        var caret = sel.Caret;
        var next = caret < doc.Length ? doc[caret] : '\0';

        // Type-through: typing a ')'/']' already sitting under the caret just steps over it.
        if ((c == ')' || c == ']') && next == c)
        {
            Buffer.Selection = Selection.At(caret + 1);
            return true;
        }

        // Auto-close a bracket/backtick when the caret is at a boundary (end of line, whitespace, or a
        // closer). Emphasis (* and _) is deliberately not auto-closed — too noisy mid-word.
        if ((c == '(' || c == '[' || c == '`')
            && (next is '\0' or '\r' or '\n' or ')' or ']' or '}' || char.IsWhiteSpace(next)))
        {
            Buffer.ReplaceRange(caret, 0, $"{c}{AutoPairs[c]}");
            Buffer.Selection = Selection.At(caret + 1);
            return true;
        }

        return false;
    }

    /// <summary>Backspace between an empty auto-pair (e.g. <c>(|)</c>) removes both halves. Returns true if it did.</summary>
    private bool TryBackspacePair()
    {
        var sel = Buffer.Selection;
        var doc = Buffer.Document;
        if (!sel.IsEmpty || sel.Caret == 0 || sel.Caret >= doc.Length)
        {
            return false;
        }

        var prev = doc[sel.Caret - 1];
        var next = doc[sel.Caret];
        if (prev is '(' or '[' or '`' && AutoPairs.TryGetValue(prev, out var close) && next == close)
        {
            Buffer.ReplaceRange(sel.Caret - 1, 2, string.Empty);
            return true;
        }

        return false;
    }

    internal void TypeForTest(char c)
    {
        if (c < ' ' || !TryAutoPair(c))
        {
            InsertTypedText(c.ToString());
        }
    }

    private void InsertTypedText(string text)
    {
        if (TryExpandTypedEmptyLineIntoParagraph(text, out var inserted, out var caretAfter))
        {
            var sel = Buffer.Selection;
            Buffer.ReplaceRange(sel.Start, sel.Length, inserted, caretAfter);
            return;
        }

        Buffer.Insert(text);
    }

    /// <summary>
    /// Typing the first visible content into a real empty line directly above a paragraph must keep that
    /// following paragraph separated. Without this, the one newline that made the line visible is reused as
    /// the typed line's trailing newline, collapsing two paragraphs into one source soft break.
    /// </summary>
    private bool TryExpandTypedEmptyLineIntoParagraph(string text, out string inserted, out int caretAfter)
    {
        inserted = text;
        caretAfter = 0;

        if (string.IsNullOrEmpty(text) || text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0)
        {
            return false;
        }

        var sel = Buffer.Selection;
        if (!sel.IsEmpty)
        {
            return false;
        }

        var doc = Buffer.Document;
        var line = doc.GetPosition(sel.Caret).Line;
        if (!IsEmptyLine(line) || line + 1 >= doc.LineCount || IsBlankLine(line + 1)
            || _model.EnteringState(line).Kind != BlockKind.Normal)
        {
            return false;
        }

        inserted = text + DetectNewline(doc);
        caretAfter = sel.Start + text.Length;
        return true;
    }

    internal void BackspaceForTest()
    {
        if (!TryBackspacePair() && !TryBackspaceCollapsedBlank() && !TryVisualBackspace())
        {
            Buffer.Backspace();
        }
    }

    internal void DeleteForTest()
    {
        if (!TryDeleteCollapsedBlank() && !TryVisualDelete())
        {
            Buffer.Delete();
        }
    }
}
