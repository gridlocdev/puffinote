using System.Text;
using Puffinote.Text.Document;

namespace Plaintain.Control.Controls;

/// <summary>
/// Multiple selections — ⌘D, as VS Code spells it: with nothing selected it takes the word under the
/// caret; after that each press finds the next occurrence of what is selected and selects that too, so a
/// rename across a file is one gesture repeated rather than a Find-and-Replace you have to get right in
/// one go.
///
/// <para>The primary selection stays <see cref="DocumentBuffer.Selection"/> — everything that already
/// reads it (the caret, the status bar, the clipboard, Find) keeps working unchanged, and an editor with
/// one selection behaves exactly as it did. The extras live only here.</para>
///
/// <para><b>One edit, not N.</b> Typing with five selections is one undoable step, because five is what
/// the user did once. It is applied as a single replacement spanning from the first selection to the
/// last, rebuilt with the new text at each: bounded by the distance between the outermost selections
/// rather than by the document, and it keeps ⌘Z reversing the whole thing the way it looked.</para>
/// </summary>
public sealed partial class PlainTextEditor
{
    /// <summary>The secondary selections, ascending. The primary is not in here.</summary>
    private readonly List<Selection> _extraSelections = [];

    /// <summary>True when there is more than one selection — the state ⌘D builds and Esc takes down.</summary>
    public bool HasMultipleSelections => _extraSelections.Count > 0;

    /// <summary>Every selection, primary included, in document order.</summary>
    internal IReadOnlyList<Selection> AllSelections()
    {
        var all = new List<Selection>(_extraSelections.Count + 1) { Buffer.Selection };
        all.AddRange(_extraSelections);
        all.Sort((a, b) => a.Start.CompareTo(b.Start));
        return all;
    }

    /// <summary>Test seam: the secondary selections, in document order.</summary>
    internal IReadOnlyList<Selection> ExtraSelectionsForTest => _extraSelections;

    /// <summary>
    /// Collapse back to the primary selection alone. Returns whether there was anything to collapse, so a
    /// key handler knows whether Escape was spent here.
    /// </summary>
    public bool ClearExtraSelections()
    {
        if (_extraSelections.Count == 0)
        {
            return false;
        }

        _extraSelections.Clear();
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// ⌘D. With nothing selected, select the word under the caret — that is the gesture's first press and
    /// it is what makes the second one mean anything. Otherwise find the next occurrence of the selected
    /// text, wrapping, and add it; the newest occurrence becomes the primary so the view follows it.
    /// </summary>
    public void AddNextOccurrence()
    {
        var doc = Buffer.Document;
        if (Buffer.Selection.IsEmpty && _extraSelections.Count == 0)
        {
            var (start, end) = WordRangeAt(Buffer.Selection.Caret);
            if (end > start)
            {
                Buffer.Selection = new Selection(start, end);
                BringCaretIntoView();
            }

            return;
        }

        var primary = Buffer.Selection;
        if (primary.IsEmpty)
        {
            return; // nothing to look for; a caret is not a search term
        }

        string needle = doc.GetText(primary.Start, primary.Length);
        var taken = AllSelections();
        int from = taken[^1].End;

        int found = IndexOfFrom(doc, needle, from, taken);
        if (found < 0)
        {
            return; // every occurrence is already selected — the gesture has finished its job
        }

        _extraSelections.Add(primary);
        _extraSelections.Sort((a, b) => a.Start.CompareTo(b.Start));
        Buffer.Selection = new Selection(found, found + needle.Length);
        BringCaretIntoView();
        InvalidateVisual();
    }

    /// <summary>
    /// The next occurrence of <paramref name="needle"/> at or after <paramref name="from"/> that is not
    /// already one of <paramref name="taken"/>, wrapping past the end of the document once. Wrapping is
    /// what lets the gesture keep going after the last match rather than going quiet with occurrences
    /// still unselected above the caret.
    /// </summary>
    private static int IndexOfFrom(TextDocument doc, string needle, int from, IReadOnlyList<Selection> taken)
    {
        // One pass forward from `from`, then one from the start up to it. Searched over the document's
        // text rather than incrementally, which is the same thing the find bar does.
        string text = doc.GetText();
        for (int at = text.IndexOf(needle, Math.Min(from, text.Length), StringComparison.Ordinal);
             at >= 0;
             at = at + 1 <= text.Length ? text.IndexOf(needle, at + 1, StringComparison.Ordinal) : -1)
        {
            if (!IsTaken(taken, at, needle.Length))
            {
                return at;
            }
        }

        for (int at = text.IndexOf(needle, 0, StringComparison.Ordinal);
             at >= 0 && at < from;
             at = at + 1 <= text.Length ? text.IndexOf(needle, at + 1, StringComparison.Ordinal) : -1)
        {
            if (!IsTaken(taken, at, needle.Length))
            {
                return at;
            }
        }

        return -1;
    }

    private static bool IsTaken(IReadOnlyList<Selection> taken, int start, int length)
    {
        foreach (var selection in taken)
        {
            if (selection.Start == start && selection.Length == length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The word around <paramref name="offset"/>, as a half-open range (equal bounds = no word).</summary>
    private (int Start, int End) WordRangeAt(int offset)
    {
        var doc = Buffer.Document;
        if (doc.Length == 0)
        {
            return (0, 0);
        }

        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

        // A caret sitting just past a word belongs to that word — that is where it lands after typing one.
        int at = Math.Clamp(offset, 0, doc.Length);
        if ((at >= doc.Length || !IsWord(doc[at])) && at > 0 && IsWord(doc[at - 1]))
        {
            at--;
        }

        if (at >= doc.Length || !IsWord(doc[at]))
        {
            return (offset, offset);
        }

        int start = at;
        while (start > 0 && IsWord(doc[start - 1]))
        {
            start--;
        }

        int end = at;
        while (end < doc.Length && IsWord(doc[end]))
        {
            end++;
        }

        return (start, end);
    }

    /// <summary>
    /// Replace every selection with <paramref name="text"/> as one undoable edit, and leave a caret after
    /// each. Returns false when there is only one selection, so the caller uses the ordinary single-caret
    /// path — which coalesces into the undo entry before it, as typing should.
    /// </summary>
    private bool ReplaceEverySelection(string text)
    {
        if (_extraSelections.Count == 0)
        {
            return false;
        }

        return ReplaceRanges(AllSelections(), text);
    }

    /// <summary>
    /// The mechanism: one replacement spanning the outermost selections, rebuilt with <paramref name="text"/>
    /// at each of them. Ranges must be ascending and non-overlapping.
    /// </summary>
    private bool ReplaceRanges(IReadOnlyList<Selection> ranges, string text)
    {
        if (ranges.Count == 0)
        {
            return false;
        }

        var doc = Buffer.Document;
        int spanStart = ranges[0].Start;
        int spanEnd = ranges[^1].End;
        if (spanEnd <= spanStart && text.Length == 0)
        {
            return false; // nothing to remove and nothing to write — e.g. Backspace at offset 0
        }

        var rebuilt = new StringBuilder(spanEnd - spanStart);
        var after = new List<Selection>(ranges.Count);
        int cursor = spanStart;
        foreach (var range in ranges)
        {
            rebuilt.Append(doc.GetText(cursor, range.Start - cursor));
            rebuilt.Append(text);

            // Offsets inside the rebuilt span are spanStart + what has been written so far, because the
            // span's new text is exactly what this builder holds.
            after.Add(Selection.At(spanStart + rebuilt.Length));
            cursor = range.End;
        }

        rebuilt.Append(doc.GetText(cursor, spanEnd - cursor));

        var last = after[^1];
        Buffer.ReplaceRange(spanStart, spanEnd - spanStart, rebuilt.ToString(), caretAfter: last.Caret);

        _extraSelections.Clear();
        for (int i = 0; i < after.Count - 1; i++)
        {
            _extraSelections.Add(after[i]);
        }

        Buffer.Selection = last;
        BringCaretIntoView();
        InvalidateVisual();
        return true;
    }

    /// <summary>Backspace at every selection: the selected text, or the grapheme before an empty one.</summary>
    private bool BackspaceEverySelection()
    {
        if (_extraSelections.Count == 0)
        {
            return false;
        }

        var ranges = new List<Selection>();
        foreach (var selection in AllSelections())
        {
            if (!selection.IsEmpty)
            {
                ranges.Add(selection);
                continue;
            }

            int before = Buffer.PreviousCaret(selection.Caret);
            if (before < selection.Caret)
            {
                ranges.Add(new Selection(before, selection.Caret));
            }
        }

        return Trim(ranges) && ReplaceRanges(ranges, string.Empty);
    }

    /// <summary>Delete at every selection: the selected text, or the grapheme after an empty one.</summary>
    private bool DeleteEverySelection()
    {
        if (_extraSelections.Count == 0)
        {
            return false;
        }

        var ranges = new List<Selection>();
        foreach (var selection in AllSelections())
        {
            if (!selection.IsEmpty)
            {
                ranges.Add(selection);
                continue;
            }

            int next = Buffer.NextCaret(selection.Caret);
            if (next > selection.Caret)
            {
                ranges.Add(new Selection(selection.Caret, next));
            }
        }

        return Trim(ranges) && ReplaceRanges(ranges, string.Empty);
    }

    /// <summary>
    /// Merge ranges that have run into each other — two carets a character apart both delete backwards
    /// onto the same character — so the rebuild never sees overlapping spans. Returns false when nothing
    /// is left to do.
    /// </summary>
    private static bool Trim(List<Selection> ranges)
    {
        for (int i = ranges.Count - 1; i > 0; i--)
        {
            if (ranges[i].Start < ranges[i - 1].End)
            {
                ranges[i - 1] = new Selection(ranges[i - 1].Start, Math.Max(ranges[i - 1].End, ranges[i].End));
                ranges.RemoveAt(i);
            }
        }

        return ranges.Count > 0;
    }
}
