namespace Typage.Engine.Editing;

/// <summary>
/// Resequences the numbers of a contiguous run of ordered list items (PRD §7.2 "renumbers").
/// Pure and headless: given the document's lines and a starting line, it renumbers the run of
/// same-level ordered items from that line down, so inserting or removing an item keeps the
/// visible numbering 1, 2, 3… The run ends at the first line that isn't an ordered item at the
/// same indentation and blockquote depth (a nested sub-list or a blank line ends it).
/// </summary>
public static class OrderedListRenumber
{
    /// <summary>The renumbered slice <c>[StartLine, EndLineExclusive)</c> and its rewritten lines.</summary>
    public readonly record struct Result(int StartLine, int EndLineExclusive, IReadOnlyList<string> Lines);

    /// <summary>
    /// Renumber the ordered-list run beginning at <paramref name="startLine"/>, sequentially from
    /// that item's own number. Returns null when <paramref name="startLine"/> isn't an ordered item
    /// or nothing needs changing.
    /// </summary>
    public static Result? Compute(IReadOnlyList<string> lines, int startLine)
    {
        if (startLine < 0 || startLine >= lines.Count)
        {
            return null;
        }

        var first = LinePrefix.Parse(lines[startLine]);
        if (first.List != ListKind.Ordered)
        {
            return null;
        }

        var rewritten = new List<string>();
        var changed = false;
        var expected = first.Number;
        var line = startLine;
        for (; line < lines.Count; line++)
        {
            var p = LinePrefix.Parse(lines[line]);
            if (p.List != ListKind.Ordered || p.Indent != first.Indent || p.Quote != first.Quote)
            {
                break;
            }

            var original = lines[line];
            if (p.Number != expected)
            {
                rewritten.Add(
                    original[..p.MarkerStart]
                    + expected.ToString()
                    + original[(p.MarkerStart + p.DigitCount)..]);
                changed = true;
            }
            else
            {
                rewritten.Add(original);
            }

            expected++;
        }

        return changed ? new Result(startLine, line, rewritten) : null;
    }
}
