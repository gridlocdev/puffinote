using Puffinote.Text.Document;

namespace Plaintain.Control.Controls;

/// <summary>
/// The other places the selected text appears, tinted faintly while the selection is up — select a
/// variable name and every other mention of it lights up, without opening Find and without changing
/// anything. It answers "where else is this?", which is most of what Find gets opened for in a log.
///
/// <para>It is also a preview of ⌘D: what these tint are exactly the occurrences
/// <see cref="AddNextOccurrence"/> will take, in the order it will take them, so the faint ones become
/// real selections one press at a time. That is why the rule for what counts as an occurrence is the
/// same one — literal, case-sensitive, ordinal, non-overlapping — rather than a second, nearly-identical
/// notion of "the same text".</para>
///
/// <para><b>What is not tinted.</b> A selection spanning a line break, and a selection that is all
/// whitespace. The first is a structural gesture rather than a chunk you are eyeing (⌘A would light the
/// document), and it is what lets the scan stay inside one line's text — see below. The second would
/// tint nearly every row of an indented file. There is no minimum length: one character is a legitimate
/// thing to ask about, and ⌘D takes one just as happily.</para>
///
/// <para><b>Cost.</b> Nothing here walks the document. Occurrences are found per <i>visible row</i>,
/// while that row is being painted and out of the line text the painter already holds, so a 50 MB log
/// costs the same as a 50 line one — the rule the geometry is built on (ARCHITECTURE §3.2), and the
/// mistake the status bar made (§3.4).</para>
/// </summary>
public sealed partial class PlainTextEditor
{
    /// <summary>
    /// The selected chunk whose other occurrences are being tinted, or null when nothing is. Resolved
    /// once per paint rather than per row: reading it copies the selected text out of the document.
    /// </summary>
    private string? _occurrenceTerm;

    /// <summary>One row's occurrences. A field, not a local: this is refilled for every visible row of
    /// every frame, and a fresh list per row is the kind of allocation that shows up as a stutter while
    /// scrolling (the same reason <see cref="DrawRowSelection"/> reads the extras list directly).</summary>
    private readonly List<SearchMatch> _occurrenceScratch = [];

    /// <summary>
    /// What the selection is asking to see more of, or null when it is asking nothing — the eligibility
    /// rule this file's summary describes.
    /// </summary>
    private string? OccurrenceTerm()
    {
        if (!HighlightSelectionOccurrences)
        {
            return null;
        }

        var doc = Buffer.Document;
        var selection = Buffer.Selection;
        int start = Math.Clamp(selection.Start, 0, doc.Length);
        int end = Math.Clamp(selection.End, 0, doc.Length);
        if (end <= start)
        {
            return null;
        }

        string term = doc.GetText(start, end - start);
        return term.Contains('\n', StringComparison.Ordinal) || term.Contains('\r', StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(term)
                ? null
                : term;
    }

    /// <summary>
    /// Append the occurrences of the selected chunk on <paramref name="line"/> — whose text is
    /// <paramref name="lineText"/>, already in the caller's hand — as document offsets, skipping the
    /// ones that are themselves selected.
    /// </summary>
    private void CollectOccurrences(int line, string lineText, List<SearchMatch> into)
    {
        if (_occurrenceTerm is not { } term)
        {
            return;
        }

        int lineStart = Buffer.Document.GetLineStart(line);
        for (int at = 0; at <= lineText.Length - term.Length;)
        {
            int hit = lineText.IndexOf(term, at, StringComparison.Ordinal);
            if (hit < 0)
            {
                break;
            }

            if (!IsSelected(lineStart + hit, term.Length))
            {
                into.Add(new SearchMatch(lineStart + hit, term.Length));
            }

            at = hit + term.Length; // non-overlapping, as Find counts matches
        }
    }

    /// <summary>
    /// Whether <c>[start, start+length)</c> is (or overlaps) a selection — the primary or a ⌘D extra.
    /// A selected occurrence is not an "other" one, and painting the faint tint under the loud one would
    /// only ever be seen where the two disagree.
    /// </summary>
    private bool IsSelected(int start, int length)
    {
        int end = start + length;
        bool Overlaps(Selection selection) => selection.Start < end && start < selection.End;

        if (Overlaps(Buffer.Selection))
        {
            return true;
        }

        foreach (var extra in _extraSelections)
        {
            if (Overlaps(extra))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Test seam: every occurrence that would be tinted, over the whole document — the paint path finds
    /// the same ranges a visible row at a time, through the same two methods. A headless run has no
    /// pixels to assert on, and the ranges are the part with a rule in it.
    /// </summary>
    internal IReadOnlyList<SearchMatch> OccurrenceHighlightsForTest()
    {
        _occurrenceTerm = OccurrenceTerm();
        var doc = Buffer.Document;
        var all = new List<SearchMatch>();
        for (int line = 0; line < doc.LineCount; line++)
        {
            CollectOccurrences(line, doc.GetLineText(line), all);
        }

        return all;
    }
}
