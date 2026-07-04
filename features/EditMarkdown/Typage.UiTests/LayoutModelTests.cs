using Avalonia.Headless.XUnit;
using Typage.Control.Rendering;
using Typage.Engine.Document;
using Typage.Engine.Markdown;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Exercises the wrapped-layout grouping/measurement algorithm through <see cref="LayoutModel"/> against a
/// synthetic <see cref="ILayoutHost"/> — no <c>MarkdownEditor</c> control, window, or input. This is the
/// point of splitting the layout state and algorithm out of the control: the block grouping is now testable
/// in isolation. (Still an AvaloniaFact because measuring a block builds a FormattedText, which needs the
/// headless font subsystem.)
/// </summary>
public class LayoutModelTests
{
    // "alpha\nbravo" are two plain paragraph lines that flow into one block; the blank line collapses; the
    // heading and the line after it each stand alone.
    private const string Sample = "alpha\nbravo\n\n# Heading\ntail";

    private static LayoutModel Layout(FakeLayoutHost host) => new(host, new TextMeasurer(() => 4));

    [AvaloniaFact]
    public void PlainLinesFlowIntoOneBlock_BlankAndHeadingBreakIt()
    {
        var host = new FakeLayoutHost(Sample);
        var layout = Layout(host);

        Assert.Equal(layout.BlockOf(0), layout.BlockOf(1));    // two plain lines flow together
        Assert.NotEqual(layout.BlockOf(1), layout.BlockOf(2)); // a blank line breaks the paragraph
        Assert.NotEqual(layout.BlockOf(2), layout.BlockOf(3)); // the heading is its own block
        Assert.NotEqual(layout.BlockOf(3), layout.BlockOf(4)); // the heading doesn't absorb the next line

        // Block boundaries (with the trailing LineCount sentinel): {0,1}=block0, 2=block1, 3=block2, 4=block3.
        Assert.Equal(new[] { 0, 2, 3, 4, 5 }, layout.BlockStartLine);
    }

    [AvaloniaFact]
    public void CollapsedBlankTakesNoRows_AndTotalRowsSumsTheRest()
    {
        var host = new FakeLayoutHost(Sample); // WrapWidth 0 → no wrap, so each drawn block is one row
        var layout = Layout(host);
        layout.EnsureLayout();

        Assert.True(layout.RowCounts[layout.BlockOf(0)] > 0); // the paragraph draws
        Assert.Equal(0, layout.RowCounts[layout.BlockOf(2)]); // the collapsed blank draws nothing
        Assert.Equal(3, layout.TotalRows);                    // paragraph + heading + tail (blank = 0)
    }

    [AvaloniaFact]
    public void RawTextMode_DisablesParagraphFlow()
    {
        var host = new FakeLayoutHost(Sample) { RawTextMode = true };
        var layout = Layout(host);

        // Raw source shows every line on its own row — the two plain lines no longer flow together.
        Assert.NotEqual(layout.BlockOf(0), layout.BlockOf(1));
    }

    private sealed class FakeLayoutHost : ILayoutHost
    {
        public FakeLayoutHost(string text)
        {
            Document = TextDocument.FromString(text);
            Model = MarkdownModel.Build(Document);
        }

        public TextDocument Document { get; }
        public MarkdownModel Model { get; }
        public MarkdownTheme Theme => MarkdownTheme.Default;
        public double WrapWidth { get; set; } // 0 disables wrapping
        public bool RawTextMode { get; init; }
        public bool MarkersRevealed { get; init; }
        public double CheckboxGutter => 20;

        // Mirrors the control's rule: a single fully-blank line between two non-blank paragraphs (in normal
        // block state, non-raw view) collapses to zero rows.
        public bool IsCollapsedBlankLine(int line)
            => !RawTextMode && line > 0 && line < Document.LineCount
               && Document.GetLineText(line).Trim().Length == 0
               && Document.GetLineText(line - 1).Trim().Length != 0
               && Model.EnteringState(line).Kind == BlockKind.Normal;

        public string? OrderedMarkerText(int line, string lineText, int sLine, int enLine) => null;
    }
}
