using System.Collections.Generic;
using Avalonia.Media;
using Typage.Control.Rendering;
using Typage.Engine.Markdown;
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
}
