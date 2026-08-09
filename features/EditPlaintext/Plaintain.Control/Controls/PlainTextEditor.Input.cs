using Avalonia.Input;
using Puffinote.Text.Document;

namespace Plaintain.Control.Controls;

public sealed partial class PlainTextEditor
{
    protected override void OnTextInput(TextInputEventArgs e)
    {
        // A ⌘/Ctrl chord is a command, not text. (Ctrl+Alt is left alone so AltGr still types.)
        bool command = _modifiers.HasFlag(KeyModifiers.Meta)
            || (_modifiers.HasFlag(KeyModifiers.Control) && !_modifiers.HasFlag(KeyModifiers.Alt));

        if (!command && !string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
        {
            ClearGoalColumn();
            if (!ReplaceEverySelection(e.Text))
            {
                Buffer.Insert(e.Text);
            }

            BringCaretIntoView();
        }

        e.Handled = true;
        base.OnTextInput(e);
    }

    private KeyModifiers _modifiers;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        _modifiers = e.KeyModifiers;
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool ctrl = control || meta; // the platform command modifier
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool isMac = OperatingSystem.IsMacOS();
        bool wordMod = isMac ? alt : control;
        var doc = Buffer.Document;
        int caret = Buffer.Selection.Caret;
        bool handled = true;
        bool vertical = e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown;

        switch (e.Key)
        {
            case Key.Back when wordMod:
                DeleteWord(-1);
                break;
            case Key.Delete when wordMod:
                DeleteWord(1);
                break;
            case Key.Back:
                if (!BackspaceEverySelection())
                {
                    Buffer.Backspace();
                }

                BringCaretIntoView();
                break;
            case Key.Delete:
                if (!DeleteEverySelection())
                {
                    Buffer.Delete();
                }

                BringCaretIntoView();
                break;

            case Key.Enter:
                if (!ReplaceEverySelection(NewlineOf(doc)))
                {
                    Buffer.Insert(NewlineOf(doc));
                }

                BringCaretIntoView();
                break;
            case Key.Tab when shift:
                Outdent();
                break;
            case Key.Tab:
                Indent();
                break;

            case Key.Left when wordMod:
                MoveCaret(WordBoundary(caret, -1), shift);
                break;
            case Key.Right when wordMod:
                MoveCaret(WordBoundary(caret, 1), shift);
                break;
            case Key.Left when isMac && meta:
                MoveCaret(SmartHomeOffset(), shift);
                break;
            case Key.Right when isMac && meta:
                MoveCaret(RowEndOffset(), shift);
                break;
            case Key.Left:
                MoveCaret(Buffer.Selection.IsEmpty || shift ? Buffer.PreviousCaret(caret) : Buffer.Selection.Start, shift);
                break;
            case Key.Right:
                MoveCaret(Buffer.Selection.IsEmpty || shift ? Buffer.NextCaret(caret) : Buffer.Selection.End, shift);
                break;

            case Key.Up when isMac && meta:
                MoveCaret(0, shift);
                break;
            case Key.Down when isMac && meta:
                MoveCaret(doc.Length, shift);
                break;
            case Key.Up:
                MoveByRow(-1, shift);
                break;
            case Key.Down:
                MoveByRow(1, shift);
                break;
            case Key.PageUp:
                MoveByRow(-PageRows(), shift);
                break;
            case Key.PageDown:
                MoveByRow(PageRows(), shift);
                break;

            case Key.Home when control:
                MoveCaret(0, shift);
                break;
            case Key.End when control:
                MoveCaret(doc.Length, shift);
                break;
            case Key.Home:
                MoveCaret(SmartHomeOffset(), shift);
                break;
            case Key.End:
                MoveCaret(RowEndOffset(), shift);
                break;

            // Escape unwinds one layer at a time: the extra selections first, then the primary one down to
            // a caret. Taking both at once would throw away a multi-selection someone was still building.
            case Key.Escape when HasMultipleSelections:
                ClearExtraSelections();
                break;
            case Key.Escape when !Buffer.Selection.IsEmpty:
                MoveCaret(Buffer.Selection.Caret, extend: false);
                break;

            case Key.A when ctrl:
                SelectAll();
                break;
            case Key.D when ctrl:
                AddNextOccurrence();
                break;
            case Key.C when ctrl:
                _ = CopyAsync();
                break;
            case Key.X when ctrl:
                _ = CutAsync();
                break;
            case Key.V when ctrl:
                _ = PasteAsync();
                break;
            case Key.Z when ctrl && shift:
                Redo();
                break;
            case Key.Z when ctrl:
                Undo();
                break;
            case Key.Y when ctrl:
                Redo();
                break;

            default:
                handled = false;
                break;
        }

        if (handled && !vertical)
        {
            ClearGoalColumn(); // any move that isn't ↑/↓ starts a new vertical run
        }

        e.Handled = handled;
        base.OnKeyDown(e);
    }

    /// <summary>
    /// The line ending this document already uses, so an edit never mixes an LF into a CRLF file
    /// (the family's byte-for-byte rule). A document with none yet takes the platform's.
    /// </summary>
    internal static string NewlineOf(TextDocument doc)
    {
        for (int i = 0; i < doc.Length; i++)
        {
            if (doc[i] == '\n')
            {
                return i > 0 && doc[i - 1] == '\r' ? "\r\n" : "\n";
            }
        }

        return Environment.NewLine;
    }

    public void SelectAll()
    {
        Buffer.Selection = new Selection(0, Buffer.Document.Length);
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (Buffer.Undo())
        {
            BringCaretIntoView();
        }
    }

    public void Redo()
    {
        if (Buffer.Redo())
        {
            BringCaretIntoView();
        }
    }

    private void DeleteWord(int direction)
    {
        var sel = Buffer.Selection;
        if (!sel.IsEmpty)
        {
            Buffer.Insert(string.Empty);
            return;
        }

        int target = WordBoundary(sel.Caret, direction);
        int start = Math.Min(target, sel.Caret);
        int length = Math.Abs(target - sel.Caret);
        if (length > 0)
        {
            Buffer.ReplaceRange(start, length, string.Empty);
        }
    }

    /// <summary>The indent one Tab inserts: spaces, four wide, to the next tab stop.</summary>
    private void Indent()
    {
        var sel = Buffer.Selection;
        var doc = Buffer.Document;
        int firstLine = doc.GetPosition(sel.Start).Line;
        int lastLine = doc.GetPosition(sel.End).Line;

        if (sel.IsEmpty || firstLine == lastLine)
        {
            int column = ColumnOf(firstLine, sel.Start);
            Buffer.Insert(new string(' ', TabWidth - (column % TabWidth)));
            BringCaretIntoView();
            return;
        }

        // A multi-line selection indents every line it touches, which is what Tab means once more than
        // one line is selected — replacing them with a tab character would be a very expensive typo.
        for (int line = lastLine; line >= firstLine; line--)
        {
            Buffer.ReplaceRange(Buffer.Document.GetLineStart(line), 0, new string(' ', TabWidth));
        }

        Buffer.Selection = new Selection(
            Buffer.Document.GetLineStart(firstLine),
            Buffer.Document.GetLineEnd(lastLine));
        InvalidateVisual();
    }

    private void Outdent()
    {
        var sel = Buffer.Selection;
        var doc = Buffer.Document;
        int firstLine = doc.GetPosition(sel.Start).Line;
        int lastLine = doc.GetPosition(sel.End).Line;

        for (int line = lastLine; line >= firstLine; line--)
        {
            string text = Buffer.Document.GetLineText(line);
            int strip = 0;
            if (text.Length > 0 && text[0] == '\t')
            {
                strip = 1;
            }
            else
            {
                while (strip < TabWidth && strip < text.Length && text[strip] == ' ')
                {
                    strip++;
                }
            }

            if (strip > 0)
            {
                Buffer.ReplaceRange(Buffer.Document.GetLineStart(line), strip, string.Empty);
            }
        }

        InvalidateVisual();
    }
}
