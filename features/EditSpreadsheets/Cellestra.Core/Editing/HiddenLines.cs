using Cellestra.Core.Model;

namespace Cellestra.Core.Editing;

/// <summary>
/// Which lines an "Unhide Rows"/"Unhide Columns" acts on (PRD §8.14).
/// <para>
/// A hidden line can't be clicked, so a selection can never contain the very lines you want back: after
/// hiding rows 5–7 the only headers left to right-click are 4 and 8. Unhide therefore reaches past the
/// selection's edges across the <b>contiguous runs of hidden lines touching them</b>, and never past a
/// visible line — so it can only ever restore what the neighbouring click implies.
/// </para>
/// <para>
/// It returns those runs rather than one span because the span between them may be enormous (a
/// whole-sheet selection spans a million rows) and unhiding a visible line means materializing it. The
/// work stays proportional to what is actually hidden.
/// </para>
/// </summary>
public static class HiddenLines
{
    /// <summary>The runs of hidden rows an Unhide Rows over <paramref name="top"/>…<paramref name="bottom"/> restores, in order.</summary>
    public static IReadOnlyList<(int First, int Last)> RowsToUnhide(Worksheet sheet, int top, int bottom) =>
        Runs([.. sheet.Rows.Where(r => r.Value.Hidden).Select(r => r.Key)], top, bottom, SheetDimensions.MaxRows);

    /// <summary>The runs of hidden columns an Unhide Columns over <paramref name="left"/>…<paramref name="right"/> restores, in order.</summary>
    public static IReadOnlyList<(int First, int Last)> ColumnsToUnhide(Worksheet sheet, int left, int right) =>
        Runs([.. sheet.Columns.Where(c => c.Value.Hidden).Select(c => c.Key)], left, right, SheetDimensions.MaxColumns);

    private static IReadOnlyList<(int First, int Last)> Runs(HashSet<int> hidden, int first, int last, int max)
    {
        if (hidden.Count == 0)
        {
            return [];
        }

        // Reach outward only while the next line is itself hidden — each step is one more hidden line,
        // so the walk is bounded by how many there are, not by how far the selection stretches.
        while (first > 0 && hidden.Contains(first - 1))
        {
            first--;
        }

        while (last < max - 1 && hidden.Contains(last + 1))
        {
            last++;
        }

        var runs = new List<(int First, int Last)>();
        foreach (int line in hidden.Where(l => l >= first && l <= last).Order())
        {
            if (runs.Count > 0 && runs[^1].Last == line - 1)
            {
                runs[^1] = (runs[^1].First, line);
            }
            else
            {
                runs.Add((line, line));
            }
        }

        return runs;
    }
}
