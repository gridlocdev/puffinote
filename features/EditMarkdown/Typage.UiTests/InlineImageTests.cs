using Avalonia.Headless.XUnit;
using Puffinote.Text.Document;
using Typage.Control;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Engine.Markdown;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Inline images: which lines become a picture, where a URL's file is looked for, how big it is drawn, and
/// what the block that holds it looks like to the layout. The bitmap itself is never decoded here — the
/// headless suite has no pixels (see the feature guide) — so the size a picture would take is fed in through
/// the layout host, which is exactly the seam the control fills with its bitmap cache.
/// </summary>
public class InlineImageTests
{
    private static (MarkdownModel Model, TextDocument Doc) Parse(string text)
    {
        var doc = TextDocument.FromString(text);
        return (MarkdownModel.Build(doc), doc);
    }

    private static string? UrlOn(string text, int line)
    {
        var (model, doc) = Parse(text);
        return InlineImages.ImageLineUrl(model, doc, line);
    }

    [Fact]
    public void ImageLineUrl_OnlyWhenTheImageIsAloneOnTheLine()
    {
        Assert.Equal("photo.png", UrlOn("![alt](photo.png)", 0));
        Assert.Equal("photo.png", UrlOn("   ![alt](photo.png)   ", 0)); // surrounding whitespace is not text
        Assert.Equal("image-20260811143025.png", UrlOn("![](image-20260811143025.png)", 0)); // no alt text

        Assert.Null(UrlOn("see ![alt](photo.png)", 0));       // inside a sentence: stays inline text
        Assert.Null(UrlOn("![alt](photo.png) ![b](c.png)", 0)); // two pictures is a row, not a block
        Assert.Null(UrlOn("[alt](photo.png)", 0));            // a link, not an image
        Assert.Null(UrlOn("- ![alt](photo.png)", 0));         // a list item that happens to hold a picture
        Assert.Null(UrlOn("plain text", 0));
    }

    [Fact]
    public void ResolvePath_AnchorsRelativeUrlsAgainstTheDocument_AndRefusesRemoteOnes()
    {
        var doc = Path.Combine(Path.GetTempPath(), "notes", "today.md");
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "notes", "images", "p.png"));
        Assert.Equal(expected, InlineImages.ResolvePath("images/p.png", doc));
        Assert.Equal(expected, InlineImages.ResolvePath("images%2Fp.png".Replace("%2F", "/"), doc));

        // An unsaved document has no folder to anchor against, so a relative URL names nothing.
        Assert.Null(InlineImages.ResolvePath("images/p.png", null));

        // The editor never fetches over the network: a remote image stays the text of its reference.
        Assert.Null(InlineImages.ResolvePath("https://example.com/p.png", doc));
        Assert.Null(InlineImages.ResolvePath("http://example.com/p.png", null));

        // Absolute paths and file:// URLs are taken as they stand (with or without a document).
        var absolute = Path.Combine(Path.GetTempPath(), "p.png");
        Assert.Equal(absolute, InlineImages.ResolvePath(absolute, null));
        Assert.Equal(absolute, InlineImages.ResolvePath(ImageStaging.EncodeUrl(absolute), null));
    }

    [Fact]
    public void Fit_ScalesDownToTheColumn_ButNeverUp()
    {
        Assert.Equal(new InlineImage(400, 200), InlineImages.Fit(400, 200, 600)); // fits: natural size
        Assert.Equal(new InlineImage(300, 150), InlineImages.Fit(600, 300, 300)); // wider: scaled to the column
        Assert.Equal(new InlineImage(64, 64), InlineImages.Fit(64, 64, 0));       // no column (wrapping off)
        Assert.Equal(new InlineImage(0, 0), InlineImages.Fit(0, 0, 400));         // nothing to draw
    }

    [AvaloniaFact]
    public void PastedImageName_IsTimestampedAndDeconflicted()
    {
        var when = new DateTime(2026, 8, 11, 14, 30, 25, DateTimeKind.Utc);
        Assert.Equal("image-20260811143025.png", ImageStaging.PastedImageName(when, ".png"));
        Assert.Equal("image-20260811143025.jpg", ImageStaging.PastedImageName(when, "jpg"));

        // A second paste inside the same second doesn't overwrite the first.
        var taken = new HashSet<string>(StringComparer.Ordinal) { Path.Combine("/notes", "image-20260811143025.png") };
        Assert.Equal(
            Path.Combine("/notes", "image-20260811143025-1.png"),
            ImageStaging.UniqueDestination("/notes", ImageStaging.PastedImageName(when, ".png"), taken.Contains));
    }

    [AvaloniaFact]
    public void ImageLine_IsItsOwnBlock_AndTakesThePicturesHeight()
    {
        // The picture sits between two paragraph lines that would otherwise flow around it.
        var host = new FakeImageHost("before\n![a](p.png)\nafter");
        host.Images[1] = new InlineImage(300, 180);
        var layout = new LayoutModel(host, new TextMeasurer(() => 4));
        layout.EnsureLayout();

        Assert.NotEqual(layout.BlockOf(0), layout.BlockOf(1)); // doesn't join the paragraph above
        Assert.NotEqual(layout.BlockOf(1), layout.BlockOf(2)); // and doesn't absorb the line below

        var block = layout.BlockOf(1);
        Assert.Equal(MarkdownBlockKind.Image, layout.BlockKinds[block]);
        Assert.Equal(180, layout.RowHeight(block));            // the row is as tall as the drawn picture
        Assert.Equal(180, layout.BlockHeight(block));          // one row, no inner padding
    }

    [AvaloniaFact]
    public void ImageBlock_ConcealsItsSourceBehindOneAtomicPictureRun()
    {
        const string source = "![a](p.png)";
        var host = new FakeImageHost(source);
        host.Images[0] = new InlineImage(240, 120);
        var layout = new LayoutModel(host, new TextMeasurer(() => 4));
        layout.EnsureLayout();

        var visual = layout.BlockVisual(0);
        var run = Assert.Single(visual.Runs);
        Assert.Equal(VisualRunKind.Decoration, run.Kind);
        Assert.Equal(DecorationKind.Image, run.Decoration);
        Assert.Equal("p.png", run.Text);        // the URL, for the draw path's bitmap lookup
        Assert.Equal(240, run.Width);
        Assert.Equal(source.Length, run.Len);   // the whole reference, atomically

        // The caret can sit on either side of the picture but never inside the reference.
        Assert.False(visual.IsInsideConcealed(0));
        Assert.True(visual.IsInsideConcealed(4));
        Assert.False(visual.IsInsideConcealed(source.Length));
        Assert.Equal(0, visual.ColumnToX(0));
        Assert.Equal(240, visual.ColumnToX(source.Length));
    }

    [AvaloniaFact]
    public void UnresolvedImage_StaysTextInItsOwnBlock()
    {
        // The file is missing (the host reports no size), so the reference renders as ordinary text — but the
        // grouping doesn't change, because whether a picture loads must not decide how a document is laid out.
        var host = new FakeImageHost("![alt](gone.png)");
        var layout = new LayoutModel(host, new TextMeasurer(() => 4));
        layout.EnsureLayout();

        Assert.Equal(MarkdownBlockKind.Image, layout.BlockKinds[0]);
        Assert.Equal(MarkdownTheme.Default.BodyRowHeight, layout.RowHeight(0));
        Assert.Contains(layout.BlockVisual(0).Runs, r => r.Kind == VisualRunKind.Text && r.Text == "alt");
    }

    [AvaloniaFact]
    public void RawSourceViews_DrawNoPictures()
    {
        foreach (var host in new[]
        {
            new FakeImageHost("![a](p.png)") { RawText = true },
            new FakeImageHost("![a](p.png)") { Revealed = true },
        })
        {
            host.Images[0] = new InlineImage(240, 120);
            var layout = new LayoutModel(host, new TextMeasurer(() => 4));
            layout.EnsureLayout();
            Assert.NotEqual(MarkdownBlockKind.Image, layout.BlockKinds[0]);
        }
    }

    [AvaloniaFact]
    public void Editor_ExposesTheDocumentPathItAnchorsImagesAgainst()
    {
        var path = Path.Combine(Path.GetTempPath(), "note.md");
        var editor = new MarkdownEditor { Text = "![a](p.png)", DocumentPath = path };
        Assert.Equal(path, editor.DocumentPath);

        // No file at p.png, so nothing is drawn — and, crucially, nothing throws while laying it out.
        Assert.Equal(1, editor.RowCountForTest(0));
    }

    /// <summary>A layout host whose picture sizes are declared per line, standing in for decoded bitmaps.</summary>
    private sealed class FakeImageHost : ILayoutHost
    {
        public FakeImageHost(string text)
        {
            Document = TextDocument.FromString(text);
            Model = MarkdownModel.Build(Document);
        }

        public TextDocument Document { get; }
        public MarkdownModel Model { get; }
        public MarkdownTheme Theme => MarkdownTheme.Default;
        public double WrapWidth => 0;
        public bool RawTextMode => RawText;
        public bool MarkersRevealed => Revealed || RawText;
        public bool RawText { get; init; }
        public bool Revealed { get; init; }
        public double CheckboxGutter => 20;
        public Dictionary<int, InlineImage> Images { get; } = [];

        public int ListLevel(int line) => 0;

        public string? OrderedMarkerText(int line, string lineText, int sLine, int enLine) => null;

        public InlineImage? ImageOnLine(int line) => Images.TryGetValue(line, out var image) ? image : null;
    }
}
