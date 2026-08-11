using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Puffinote.Text.Document;
using Typage.Control.Rendering;
using Typage.Engine.Markdown;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The heading type scale and the vertical rhythm it rides on: per-block row heights, collapsing margins, and
/// the inner padding that makes a code block or a blockquote read as one panel. Exercised through
/// <see cref="LayoutModel"/> against a synthetic host, so the geometry is checked without a live control.
/// </summary>
public class VerticalRhythmTests
{
    private static LayoutModel Layout(FakeRhythmHost host) => new(host, new TextMeasurer(() => 4));

    private static readonly MarkdownTheme T = MarkdownTheme.Default;

    // ----- the type scale -----

    [AvaloniaFact]
    public void HeadingSizes_DescendMonotonically_AndH6SitsBelowBody()
    {
        // Every level is strictly smaller than the one above it: the scale never ties, which is what let the
        // old scale collapse H5/H6 into body text.
        for (var level = 1; level < 6; level++)
        {
            Assert.True(T.HeadingSize(level) > T.HeadingSize(level + 1),
                $"H{level} ({T.HeadingSize(level)}) should be larger than H{level + 1} ({T.HeadingSize(level + 1)})");
        }

        // H1 must be decisively bigger than H2 — the old scale's 24-vs-21 was the "weak hierarchy" complaint.
        Assert.True(T.HeadingSize(1) >= T.HeadingSize(2) * 1.2);

        // H6 is *smaller* than body, not equal to it. Previously both were BaseFontSize, so an H6 was
        // indistinguishable from a paragraph — the rendering defect this scale exists to fix.
        Assert.True(T.HeadingSize(6) < T.BaseFontSize);
    }

    [AvaloniaFact]
    public void EveryHeadingLevel_IsDistinguishableFromBody_BySizeWeightOrColour()
    {
        var body = T.Resolve(TokenType.Text);
        for (var level = 1; level <= 6; level++)
        {
            var h = T.OnHeading(T.Resolve(TokenType.HeadingText, level), level, TokenType.HeadingText);
            var differs = h.FontSize != body.FontSize
                          || h.Typeface.Weight != body.Typeface.Weight
                          || !ReferenceEquals(h.Foreground, body.Foreground);
            Assert.True(differs, $"H{level} renders identically to body text");
        }
    }

    [AvaloniaFact]
    public void HeadingScale_RidesOnBaseFontSize()
    {
        // The scale is authored against a 16px body and stored as ratios, so raising the base font size
        // rescales every level together — the "rem" behaviour a stylesheet would get for free.
        var bigger = new MarkdownTheme { BaseFontSize = T.BaseFontSize * 2 };
        Assert.Equal(T.HeadingSize(1) * 2, bigger.HeadingSize(1), 3);
        Assert.Equal(T.HeadingSize(6) * 2, bigger.HeadingSize(6), 3);
    }

    [AvaloniaFact]
    public void HighContrast_DoesNotDimTheLowerHeadings()
    {
        // The other palettes desaturate down the scale; high contrast must not — dimming H5/H6 is exactly what
        // a high-contrast user needs us not to do, so there the hierarchy rests on size and weight alone.
        var hc = MarkdownTheme.HighContrast;
        for (var level = 1; level <= 6; level++)
        {
            Assert.Equal(Colors.White, ((SolidColorBrush)hc.HeadingForegroundFor(level)).Color);
        }
    }

    // ----- the rhythm -----

    [AvaloniaFact]
    public void AHeadingRowIsTallerThanABodyRow()
    {
        var layout = Layout(new FakeRhythmHost("text\n\n# Heading\n\nmore"));
        layout.EnsureLayout();

        Assert.True(layout.RowHeight(layout.BlockOf(2)) > layout.RowHeight(layout.BlockOf(0)));
    }

    [AvaloniaFact]
    public void AHeadingGetsItsSpaceBefore_AndBondsDownwardToTheContentBelowIt()
    {
        var layout = Layout(new FakeRhythmHost("text\n\n# Heading\n\nmore"));
        layout.EnsureLayout();

        var para = layout.BlockOf(0);
        var heading = layout.BlockOf(2);
        var below = layout.BlockOf(4);

        var above = layout.BlockTop(heading) - (layout.BlockTop(para) + layout.BlockHeight(para));
        var beneath = layout.BlockTop(below) - (layout.BlockTop(heading) + layout.BlockHeight(heading));

        // The whole point of the heading's asymmetric margins: it groups with what it introduces.
        Assert.True(above > beneath, $"heading should bond downward (above {above}, below {beneath})");
        Assert.True(above > 0);
    }

    [AvaloniaFact]
    public void AdjacentMarginsCollapse_TheyDoNotSum()
    {
        var layout = Layout(new FakeRhythmHost("text\n\n# Heading\n\nmore"));
        layout.EnsureLayout();

        var para = layout.BlockOf(0);
        var heading = layout.BlockOf(2);
        var gap = layout.BlockTop(heading) - (layout.BlockTop(para) + layout.BlockHeight(para));

        var paraAfter = T.MetricsFor(MarkdownBlockKind.Paragraph).SpaceAfter;
        var headingBefore = T.MetricsFor(MarkdownBlockKind.Heading, 1).SpaceBefore;

        // The larger of the two, not their sum. Summing would put the paragraph's 12px under the H1's 48px and
        // open a 60px canyon mid-document.
        Assert.Equal(Math.Max(paraAfter, headingBefore), gap, 3);
        Assert.True(gap < paraAfter + headingBefore);
    }

    [AvaloniaFact]
    public void TheFirstBlockTakesNoSpaceBefore()
    {
        // A document that opens on an H1 must not start with a 48px void — that reads as broken padding.
        var layout = Layout(new FakeRhythmHost("# Heading\n\ntext"));
        layout.EnsureLayout();

        Assert.Equal(0, layout.BlockTop(0), 3);
    }

    [AvaloniaFact]
    public void ACollapsedBlankSeparator_IsTransparentToTheRhythm()
    {
        // The blank line between two paragraphs takes no height *and* no gap of its own: the paragraphs collapse
        // against each other directly. Were it opaque, the paragraph's SpaceAfter would be paid twice (once into
        // the blank, once out of it) and every paragraph break would be double-spaced.
        var layout = Layout(new FakeRhythmHost("alpha\n\nbravo"));
        layout.EnsureLayout();

        var first = layout.BlockOf(0);
        var second = layout.BlockOf(2);
        var gap = layout.BlockTop(second) - (layout.BlockTop(first) + layout.BlockHeight(first));

        Assert.Equal(T.MetricsFor(MarkdownBlockKind.Paragraph).SpaceAfter, gap, 3);
    }

    [AvaloniaFact]
    public void AHardBreakIsTighterThanAParagraphBreak()
    {
        // ①(d): a hard break used to be visually identical to a new paragraph. It must now be strictly tighter —
        // that is the whole difference between "\" and a blank line.
        var hard = Layout(new FakeRhythmHost("alpha\\\nbravo"));
        hard.EnsureLayout();
        var hardGap = hard.BlockTop(hard.BlockOf(1)) - (hard.BlockTop(hard.BlockOf(0)) + hard.BlockHeight(hard.BlockOf(0)));

        var para = Layout(new FakeRhythmHost("alpha\n\nbravo"));
        para.EnsureLayout();
        var paraGap = para.BlockTop(para.BlockOf(2)) - (para.BlockTop(para.BlockOf(0)) + para.BlockHeight(para.BlockOf(0)));

        Assert.True(hardGap < paraGap, $"hard break ({hardGap}) should be tighter than a paragraph break ({paraGap})");
    }

    [AvaloniaFact]
    public void ACodeBlocksLinesCloseUp_AndOnlyTheRunsEdgesCarryPadding()
    {
        var layout = Layout(new FakeRhythmHost("text\n\n```\na\nb\n```\n\nmore"));
        layout.EnsureLayout();

        var open = layout.BlockOf(2);   // ```
        var mid = layout.BlockOf(3);    // a
        var close = layout.BlockOf(5);  // ```

        // Every line of the fence is its own block, but they must abut exactly — any gap would slice the panel.
        for (var b = open; b < close; b++)
        {
            Assert.Equal(layout.BlockTop(b) + layout.BlockHeight(b), layout.BlockTop(b + 1), 3);
        }

        // Only the run's outer edges pad, so the panel gets inner breathing room without spacing its lines apart.
        Assert.True(layout.BlockMetricsOf[open].PadTop > 0);
        Assert.Equal(0, layout.BlockMetricsOf[mid].PadTop, 3);
        Assert.Equal(0, layout.BlockMetricsOf[mid].PadBottom, 3);
        Assert.True(layout.BlockMetricsOf[close].PadBottom > 0);
    }

    [AvaloniaFact]
    public void WhatFollowsAnIndentedCodeBlock_IsNotSweptIntoIt()
    {
        // An indented code block ends *lazily*, at the first dedented line — so the heading below one is still
        // entered with the IndentedCode block state, and classifying blocks by that state alone put the heading
        // (and the prose under it) inside the code panel, stretched to the code row's height.
        var layout = Layout(new FakeRhythmHost("text\n\n    indented code\n\n## Heading\n\nmore"));
        layout.EnsureLayout();

        var code = layout.BlockOf(2);
        var heading = layout.BlockOf(4);
        var prose = layout.BlockOf(6);

        Assert.Equal(MarkdownBlockKind.Code, layout.BlockKinds[code]);
        Assert.Equal(MarkdownBlockKind.Heading, layout.BlockKinds[heading]);
        Assert.Equal(MarkdownBlockKind.Paragraph, layout.BlockKinds[prose]);

        // The code run therefore closes on the code line itself: it carries the panel's bottom padding, and the
        // heading sits clear of the panel with its own row height and its own space above.
        Assert.True(layout.BlockMetricsOf[code].PadBottom > 0);
        Assert.True(layout.BlockTop(heading) > layout.BlockTop(code) + layout.BlockHeight(code));
        Assert.Equal(T.MetricsFor(MarkdownBlockKind.Heading, 2).RowHeight, layout.RowHeight(heading), 3);
    }

    [AvaloniaFact]
    public void TheBlankBelowAnIndentedCodeBlock_CollapsesLikeAnyParagraphSeparator()
    {
        // It reads as code to the block state (the block hasn't formally ended), but it is an ordinary paragraph
        // separator — so it collapses to zero rows instead of wedging an empty row under the panel.
        var layout = Layout(new FakeRhythmHost("text\n\n    indented code\n\n## Heading"));
        layout.EnsureLayout();

        Assert.Equal(0, layout.RowCounts[layout.BlockOf(3)]);

        // A blank *between* two indented lines is interior to the block and keeps its row inside the panel.
        var interior = Layout(new FakeRhythmHost("text\n\n    one\n\n    two"));
        interior.EnsureLayout();
        Assert.Equal(1, interior.RowCounts[interior.BlockOf(3)]);
        Assert.Equal(MarkdownBlockKind.Code, interior.BlockKinds[interior.BlockOf(3)]);
    }

    [AvaloniaFact]
    public void TwoBlockquotesSeparatedByABlank_StayTwoQuotes()
    {
        // The blank between them collapses to zero height, which would make them *look* adjacent to the
        // run-closing rule and weld them into one bar. They must stay apart.
        var layout = Layout(new FakeRhythmHost("> one\n\n> two"));
        layout.EnsureLayout();

        var first = layout.BlockOf(0);
        var second = layout.BlockOf(2);
        var gap = layout.BlockTop(second) - (layout.BlockTop(first) + layout.BlockHeight(first));

        Assert.True(gap > 0, "a blank line between two blockquotes must keep them separate");
    }

    [AvaloniaFact]
    public void ContentHeight_IsTheSumOfEveryBlockAndItsGap()
    {
        var layout = Layout(new FakeRhythmHost("# Title\n\ntext\n\n- item\n- item"));
        layout.EnsureLayout();

        var last = layout.RowCounts.Length - 1;
        Assert.Equal(layout.BlockTop(last) + layout.BlockHeight(last), layout.ContentHeight, 3);
        Assert.True(layout.ContentHeight > 0);
    }

    [AvaloniaFact]
    public void BlockAtY_FindsTheBlockUnderAPixel()
    {
        // The hit-test path resolves a click by pixel, not by row — a uniform-row search would drift out of
        // alignment the moment a heading or code block changed the row pitch above the click.
        var layout = Layout(new FakeRhythmHost("# Title\n\ntext\n\nmore"));
        layout.EnsureLayout();

        for (var b = 0; b < layout.RowCounts.Length; b++)
        {
            if (layout.BlockHeight(b) <= 0)
            {
                continue; // a collapsed blank occupies no pixels, so nothing can land on it
            }

            var mid = layout.BlockTop(b) + (layout.BlockHeight(b) / 2);
            Assert.Equal(b, layout.BlockAtY(mid));
        }
    }

    private sealed class FakeRhythmHost : ILayoutHost
    {
        public FakeRhythmHost(string text)
        {
            Document = TextDocument.FromString(text);
            Model = MarkdownModel.Build(Document);
        }

        public TextDocument Document { get; }
        public MarkdownModel Model { get; }
        public MarkdownTheme Theme => MarkdownTheme.Default;
        public double WrapWidth => 0; // no wrap: one row per block, so heights are the metrics alone
        public bool RawTextMode => false;
        public bool MarkersRevealed => false;
        public double CheckboxGutter => 20;

        public int ListLevel(int line) => 0;

        public string? OrderedMarkerText(int line, string lineText, int sLine, int enLine) => null;

        /// <summary>The drawn image sizes this host reports, by line — how a test puts a picture on a line
        /// without a bitmap to decode (the headless suite has no pixels to decode one with).</summary>
        public Dictionary<int, InlineImage> Images { get; } = [];

        public InlineImage? ImageOnLine(int line) => Images.TryGetValue(line, out var image) ? image : null;
    }
}
