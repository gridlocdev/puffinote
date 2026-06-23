using Typage.Engine.Document;

namespace Typage.Engine.Editing;

/// <summary>
/// The editable document model: the current immutable <see cref="TextDocument"/> snapshot, the
/// caret/selection, the undo history, and a dirty flag. This is the headless, Avalonia-free
/// surface the control drives — the UI never mutates a <see cref="TextDocument"/> directly; it
/// goes through these methods so every change is undoable and the dirty/caret state stays
/// consistent (PRD §10.2: file I/O lives in the shell, editing logic lives here).
/// </summary>
public sealed class DocumentBuffer
{
    private readonly EditHistory _history = new();
    private TextDocument _document;
    private Selection _selection;

    public DocumentBuffer(string? text = null)
    {
        _document = TextDocument.FromString(text);
        _selection = Selection.At(0);
        SavedDocument = _document;
    }

    /// <summary>Raised after any change to the document or selection.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised after a change to the document text (not for selection-only moves).</summary>
    public event EventHandler? TextChanged;

    /// <summary>The current immutable snapshot.</summary>
    public TextDocument Document => _document;

    /// <summary>The snapshot last marked saved; the dirty flag compares against it.</summary>
    public TextDocument SavedDocument { get; private set; }

    /// <summary>The caret/selection.</summary>
    public Selection Selection
    {
        get => _selection;
        set
        {
            var clamped = Clamp(value);
            if (clamped == _selection)
            {
                return;
            }

            // A caret jump ends the current typing-coalescing run (PRD §7.2).
            _history.BreakCoalescing();
            _selection = clamped;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>True when the buffer differs from the last-saved snapshot (drives the title/status dirty mark).</summary>
    public bool IsDirty => !ReferenceEquals(_document, SavedDocument);

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    /// <summary>Replace the selection (or insert at the caret) with <paramref name="text"/>.</summary>
    public void Insert(string text)
    {
        var sel = _selection;
        ReplaceRange(sel.Start, sel.Length, text ?? string.Empty);
    }

    /// <summary>Delete the selection if any, else the grapheme before the caret (Backspace).</summary>
    public void Backspace()
    {
        var sel = _selection;
        if (!sel.IsEmpty)
        {
            ReplaceRange(sel.Start, sel.Length, string.Empty);
            return;
        }

        if (sel.Caret == 0)
        {
            return;
        }

        var prev = PreviousCaret(sel.Caret);
        ReplaceRange(prev, sel.Caret - prev, string.Empty);
    }

    /// <summary>Delete the selection if any, else the grapheme after the caret (Delete/forward-delete).</summary>
    public void Delete()
    {
        var sel = _selection;
        if (!sel.IsEmpty)
        {
            ReplaceRange(sel.Start, sel.Length, string.Empty);
            return;
        }

        if (sel.Caret >= _document.Length)
        {
            return;
        }

        var next = NextCaret(sel.Caret);
        ReplaceRange(sel.Caret, next - sel.Caret, string.Empty);
    }

    /// <summary>Replace <c>[start, start+length)</c> with <paramref name="text"/> as one undoable edit.</summary>
    public void ReplaceRange(int start, int length, string text)
    {
        var edit = TextEdit.Replace(_document, start, length, text ?? string.Empty);
        if (edit.OldLength == 0 && edit.NewLength == 0)
        {
            return;
        }

        var before = _selection;
        _document = edit.Apply(_document);
        _selection = Selection.At(edit.CaretAfter);
        _history.Record(edit, before, _selection);
        RaiseTextChanged();
    }

    /// <summary>Undo the most recent step. Returns false if there was nothing to undo.</summary>
    public bool Undo()
    {
        var entry = _history.PopUndo();
        if (entry is null)
        {
            return false;
        }

        _document = entry.Value.Edit.Invert().Apply(_document);
        _selection = Clamp(entry.Value.Before);
        RaiseTextChanged();
        return true;
    }

    /// <summary>Redo the most recently undone step. Returns false if there was nothing to redo.</summary>
    public bool Redo()
    {
        var entry = _history.PopRedo();
        if (entry is null)
        {
            return false;
        }

        _document = entry.Value.Edit.Apply(_document);
        _selection = Clamp(entry.Value.After);
        RaiseTextChanged();
        return true;
    }

    /// <summary>Replace the whole document (open / revert / external reload). Clears undo history.</summary>
    public void Load(string? text, bool markSaved = true)
    {
        _document = TextDocument.FromString(text);
        if (markSaved)
        {
            SavedDocument = _document;
        }

        _selection = Selection.At(0);
        _history.Clear();
        RaiseTextChanged();
    }

    /// <summary>Mark the current snapshot as the saved baseline (after a successful save).</summary>
    public void MarkSaved() => SavedDocument = _document;

    /// <summary>The caret-legal offset one grapheme after <paramref name="offset"/>.</summary>
    public int NextCaret(int offset) => Grapheme.NextBoundary(_document.GetText(), offset);

    /// <summary>The caret-legal offset one grapheme before <paramref name="offset"/>.</summary>
    public int PreviousCaret(int offset) => Grapheme.PreviousBoundary(_document.GetText(), offset);

    private Selection Clamp(Selection sel)
    {
        var len = _document.Length;
        return new Selection(Math.Clamp(sel.Anchor, 0, len), Math.Clamp(sel.Caret, 0, len));
    }

    private void RaiseTextChanged()
    {
        TextChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
