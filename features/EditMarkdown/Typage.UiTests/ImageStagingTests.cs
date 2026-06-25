using System.IO;
using System.Linq;
using Typage.Desktop.Services;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The pure pieces of <see cref="ImageStaging"/>: detecting staged image URLs in a document, computing
/// relative/​permanent paths, and rewriting URLs. The actual file moves happen in the shell on save; here
/// we cover the path/text logic that decides what gets written.
/// </summary>
public class ImageStagingTests
{
    private static string Staged(string name) => Path.Combine(ImageStaging.StagingRoot, name);

    [Fact]
    public void IsStaged_TrueOnlyForPathsUnderTheStagingRoot()
    {
        Assert.True(ImageStaging.IsStaged(Staged("abc__photo.png")));
        Assert.False(ImageStaging.IsStaged("images/photo.png")); // relative
        Assert.False(ImageStaging.IsStaged("https://example.com/p.png"));
        Assert.False(ImageStaging.IsStaged(string.Empty));
    }

    [Fact]
    public void OriginalName_RecoversTheNameAfterTheId()
    {
        Assert.Equal("photo.png", ImageStaging.OriginalName(Staged("deadbeef__photo.png")));
        Assert.Equal("plain.png", ImageStaging.OriginalName(Staged("plain.png"))); // no id separator
    }

    [Fact]
    public void StagedReferences_FindsOnlyStagedImageUrls_WithTheirSpans()
    {
        var url = Staged("id__pic.png");
        var text = $"intro\n![a](images/local.png) and ![b]({url}) end";

        var refs = ImageStaging.StagedReferences(text);
        var r = Assert.Single(refs);
        Assert.Equal(url, r.Url);
        // The reported span is exactly the URL inside the parentheses.
        Assert.Equal(url, text.Substring(r.Start, r.Length));
    }

    [Fact]
    public void EncodeUrl_PercentEncodesTheLinkBreakingCharacters()
    {
        Assert.Equal("a%20b/c.png", ImageStaging.EncodeUrl("a b/c.png"));
        Assert.Equal("x%28y%29.png", ImageStaging.EncodeUrl("x(y).png"));
        Assert.Equal("100%25.png", ImageStaging.EncodeUrl("100%.png"));
        Assert.Equal("a b (1).png", ImageStaging.DecodeUrl(ImageStaging.EncodeUrl("a b (1).png")));
    }

    [Fact]
    public void StagedReferences_FindsAnEncodedPathWithSpaces()
    {
        // Mirrors macOS, where the scratch root sits under ".../Application Support/...". The encoded URL
        // must survive as one Markdown destination, and decode back to the real (spaced) staged path.
        var real = Staged("id__my photo.png");
        var encoded = ImageStaging.EncodeUrl(real);
        Assert.DoesNotContain(" ", encoded); // no space → the link won't truncate

        var text = $"![pic]({encoded})";
        var r = Assert.Single(ImageStaging.StagedReferences(text));
        Assert.Equal(real, r.Url);                                  // decoded back to the real path
        Assert.Equal(encoded, text.Substring(r.Start, r.Length));  // span covers the encoded URL
    }

    [Fact]
    public void RelativeUrl_IsForwardSlashedAndAnchoredAtTheDocumentFolder()
    {
        var doc = Path.Combine(Path.GetTempPath(), "notes", "note.md");
        var img = Path.Combine(Path.GetTempPath(), "notes", "images", "x.png");
        Assert.Equal("images/x.png", ImageStaging.RelativeUrl(doc, img));
    }

    [Fact]
    public void RewriteUrls_ReplacesEachSpan_RightToLeft()
    {
        var a = Staged("1__a.png");
        var b = Staged("2__b.png");
        var text = $"![a]({a}) ![b]({b})";
        var refs = ImageStaging.StagedReferences(text);

        var edits = refs.Select(r => (r.Start, r.Length, "images/" + ImageStaging.OriginalName(r.Url)));
        var rewritten = ImageStaging.RewriteUrls(text, edits);

        Assert.Equal("![a](images/a.png) ![b](images/b.png)", rewritten);
    }

    [Fact]
    public void UniqueDestination_AvoidsCollisions()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgs");
        var taken = new[] { Path.Combine(dir, "p.png"), Path.Combine(dir, "p-1.png") };
        var dest = ImageStaging.UniqueDestination(dir, "p.png", taken.Contains);
        Assert.Equal(Path.Combine(dir, "p-2.png"), dest);
    }

    [Fact]
    public void UniqueDestination_UsesTheBareNameWhenFree()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgs");
        var dest = ImageStaging.UniqueDestination(dir, "p.png", _ => false);
        Assert.Equal(Path.Combine(dir, "p.png"), dest);
    }

    [Fact]
    public void Reconcile_PhysicallyCopiesStagedImagesIntoImagesFolder_AndRewritesLinks()
    {
        // Stage two real files (one with a space, like a macOS name), reference them in a document, then
        // reconcile against a target doc path and verify the files land in ./images/ and the links update.
        var srcA = Path.Combine(Path.GetTempPath(), $"a-{System.Guid.NewGuid():N}.png");
        var srcB = Path.Combine(Path.GetTempPath(), $"b {System.Guid.NewGuid():N}.png"); // spaced name
        File.WriteAllBytes(srcA, new byte[] { 1 });
        File.WriteAllBytes(srcB, new byte[] { 2 });
        var stagedA = ImageStaging.Stage(srcA, "ida");
        var stagedB = ImageStaging.Stage(srcB, "idb");

        var docDir = Path.Combine(Path.GetTempPath(), $"doc-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(docDir);
        var docPath = Path.Combine(docDir, "note.md");

        try
        {
            var text = $"![a]({ImageStaging.EncodeUrl(stagedA)})\n![b]({ImageStaging.EncodeUrl(stagedB)})";
            var staged = ImageStaging.StagedReferences(text);
            Assert.Equal(2, staged.Count);

            var (newText, error) = ImageStaging.Reconcile(text, docPath, staged);
            Assert.Null(error);

            // The image files were physically copied into <docDir>/images/.
            Assert.True(File.Exists(Path.Combine(docDir, "images", Path.GetFileName(srcA))));
            Assert.True(File.Exists(Path.Combine(docDir, "images", Path.GetFileName(srcB))));

            // The links now point at the relative, encoded images/ paths (no scratch path remains).
            Assert.Contains($"![a](images/{Path.GetFileName(srcA)})", newText);
            Assert.Contains($"![b](images/{ImageStaging.EncodeUrl(Path.GetFileName(srcB))})", newText);
            Assert.DoesNotContain(ImageStaging.StagingRoot, newText);
        }
        finally
        {
            foreach (var p in new[] { srcA, srcB, stagedA, stagedB })
            {
                if (File.Exists(p))
                {
                    File.Delete(p);
                }
            }

            Directory.Delete(docDir, recursive: true);
        }
    }

    [Fact]
    public void Stage_CopiesIntoTheStagingRoot_AndIsDetectedAsStaged()
    {
        var src = Path.Combine(Path.GetTempPath(), $"src-{System.Guid.NewGuid():N}.png");
        File.WriteAllBytes(src, new byte[] { 1, 2, 3 });
        try
        {
            var staged = ImageStaging.Stage(src, "testid");
            try
            {
                Assert.True(File.Exists(staged));
                Assert.True(ImageStaging.IsStaged(staged));
                Assert.Equal(Path.GetFileName(src), ImageStaging.OriginalName(staged));
            }
            finally
            {
                File.Delete(staged);
            }
        }
        finally
        {
            File.Delete(src);
        }
    }
}
