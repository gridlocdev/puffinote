using Avalonia.Media;
using Typage.Control.Rendering;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The pure offset↔visual map (<see cref="VisualLine"/>) in isolation — no editor, no window. A fake
/// measurer makes every char one unit wide, so x and column space line up and the concealment math is
/// checkable exactly. These pin the invariants the surface relies on (PRD §7.4 offset↔visual map).
/// </summary>
public class VisualLineTests
{
    // One unit per char; ignores style. Lets us assert exact x's.
    private static readonly System.Func<string, RunStyle, double> Unit = (s, _) => s.Length;

    private static readonly RunStyle Plain = new(new Typeface(FontFamily.Default), 12, Brushes.Black, null, false);
    private static readonly RunStyle Mark = new(new Typeface(FontFamily.Default), 12, Brushes.Gray, null, true);

    private static VisualLine Build(string text, params VisualLine.Segment[] segs) => VisualLine.Build(text, segs, Unit);

    // "**bold**": markers [0,2) and [6,8) concealed, content [2,6) shown.
    private static VisualLine Bold() => Build(
        "**bold**",
        new VisualLine.Segment(0, 2, Mark, true),
        new VisualLine.Segment(2, 6, Plain, false),
        new VisualLine.Segment(6, 8, Mark, true));

    [Fact]
    public void ConcealedMarkers_HaveZeroWidth()
    {
        var v = Bold();
        Assert.Equal(4, v.Width); // only the 4 content chars contribute width
        Assert.Equal(0, v.ColumnToX(0)); // before the hidden opening marker
        Assert.Equal(0, v.ColumnToX(2)); // content start sits at x=0 (markers collapsed)
        Assert.Equal(4, v.ColumnToX(6)); // content end
        Assert.Equal(4, v.ColumnToX(8)); // after the hidden closing marker
    }

    [Fact]
    public void ColumnInsideAConcealedRun_SnapsToItsLeftEdge()
    {
        var v = Bold();
        Assert.Equal(0, v.ColumnToX(1)); // between the two opening '*'s → left edge
        Assert.Equal(4, v.ColumnToX(7)); // inside the closing '**' → its left edge
        Assert.True(v.IsInsideConcealed(1));
        Assert.True(v.IsInsideConcealed(7));
        Assert.False(v.IsInsideConcealed(2)); // boundaries are valid stops
    }

    [Fact]
    public void XToColumn_NeverLandsStrictlyInsideAConcealedRun()
    {
        var v = Bold();
        for (var x = -2d; x <= 6; x += 0.25)
        {
            Assert.False(v.IsInsideConcealed(v.XToColumn(x)));
        }
    }

    [Fact]
    public void XToColumn_IsTheInverseOfColumnToX_AtUniquelyMappedStops()
    {
        // Interior content columns map to distinct x's (column 0 collides with the content start, and
        // column 6 with the line end, because the markers around them collapse to zero width).
        var v = Bold();
        foreach (var col in new[] { 2, 3, 4, 5 })
        {
            Assert.Equal(col, v.XToColumn(v.ColumnToX(col)));
        }
    }

    [Fact]
    public void ColumnToX_IsMonotonicAcrossTheLine()
    {
        var v = Bold();
        var prev = double.NegativeInfinity;
        for (var col = 0; col <= v.RawLength; col++)
        {
            var x = v.ColumnToX(col);
            Assert.True(x >= prev);
            prev = x;
        }
    }

    [Fact]
    public void AllConcealed_CollapsesToZeroWidth_AndOffsetZero()
    {
        // A horizontal-rule-like line: the whole content is a hidden marker.
        var v = Build("---", new VisualLine.Segment(0, 3, Mark, true));
        Assert.Equal(0, v.Width);
        Assert.False(v.IsInsideConcealed(v.XToColumn(50))); // a click resolves to a valid (edge) stop
    }

    // ----- word wrap (1 unit per char, wrapWidth in the same units) -----

    private static VisualLine Wrapped(string text, double wrapWidth) =>
        VisualLine.Build(text, [new VisualLine.Segment(0, text.Length, Plain, false)], Unit, wrapWidth);

    [Fact]
    public void NoWrapWidth_KeepsASingleRow()
    {
        var v = Wrapped("aaa bbb ccc ddd", 0);
        Assert.Equal(1, v.RowCount);
    }

    [Fact]
    public void LongLine_WrapsAtWordBoundaries()
    {
        // "aaa bbb ccc": "aaa bbb" (width 7) fits; "ccc" overflows and starts a new row.
        var v = Wrapped("aaa bbb ccc", 7);
        Assert.Equal(2, v.RowCount);
        Assert.Equal(0, v.RowOf(0));   // "aaa" on row 0
        Assert.Equal(1, v.RowOf(8));   // "ccc" begins on row 1
    }

    [Fact]
    public void WrappedRow_RestartsXAtTheLeft()
    {
        var v = Wrapped("aaa bbb ccc", 7);
        Assert.Equal(0, v.ColumnToX(8)); // "ccc" starts at x=0 on its own row
    }

    [Fact]
    public void XToColumn_IsScopedToTheGivenRow()
    {
        var v = Wrapped("aaa bbb ccc", 7);
        // x=0 on row 1 resolves to the start of "ccc" (column 8), not column 0.
        Assert.Equal(8, v.XToColumn(1, 0));
        Assert.Equal(0, v.XToColumn(0, 0));
    }

    [Fact]
    public void Spans_BreakAcrossRows_ForASelection()
    {
        var v = Wrapped("aaa bbb ccc", 7);
        var rows = new List<(int Row, double X1, double X2)>(v.Spans(0, 11));
        Assert.Equal(2, rows.Count); // the selection covers both wrapped rows
        Assert.Equal(0, rows[0].Row);
        Assert.Equal(1, rows[1].Row);
    }

    [Fact]
    public void TaskItem_WrapsWithAHangingIndentUnderTheCheckboxGutter()
    {
        // A task item shaped as the layout builder emits it: a concealed "- " marker, then a checkbox
        // decoration owning a gutter, then the content. Regression guard for the wrap gutter — the concealed
        // marker sits ahead of the decoration and must NOT end the leading-decoration scan, or the checkbox
        // width is lost and wrapped rows hang at the far left (x=0) instead of under the item's text.
        const double box = 3;
        var v = VisualLine.Build(
            "- [ ] aaa bbb ccc",
            [
                new VisualLine.Segment(0, 2, Mark, true),                                          // "- " concealed
                new VisualLine.Segment(2, 6, Plain, false, DecorationKind.CheckboxUnchecked, box), // "[ ] " checkbox
                new VisualLine.Segment(6, 17, Plain, false),                                       // "aaa bbb ccc"
            ],
            Unit,
            wrapWidth: 11);

        Assert.Equal(2, v.RowCount);
        Assert.Equal(1, v.RowOf(14));       // "ccc" (col 14) wraps to the second row
        Assert.Equal(box, v.ColumnToX(14)); // and hangs under the checkbox gutter, not at x=0
    }
}
