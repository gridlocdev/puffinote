using System.Linq;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Strikethrough conceals its <c>~~</c> markers and carries a stroked strikethrough decoration on the
/// content run. The line itself is drawn by hand in the draw pass (Avalonia's text decorations don't render
/// dependably across backends — the same reason Cellestra overlays them), which can't be pixel-checked
/// headlessly; here we lock in the data the draw pass consumes.
/// </summary>
public class StrikethroughRenderingTests
{
    private static MarkdownEditor CaretAtEnd(string text)
    {
        var editor = new MarkdownEditor { Text = text };
        editor.Buffer.Selection = Selection.At(editor.Buffer.Document.Length);
        return editor;
    }

    [AvaloniaFact]
    public void Tildes_AreConcealed_AndOnlyTheWordShows()
    {
        var runs = CaretAtEnd("~~paragraph~~ done").BuildVisualLineForTest(0).Runs;
        var visible = string.Concat(runs.Where(r => r.Kind == VisualRunKind.Text).Select(r => r.Text));
        Assert.DoesNotContain("~", visible);
        Assert.Contains("paragraph", visible);
    }

    [AvaloniaFact]
    public void StruckText_CarriesAStrokedStrikethroughDecoration()
    {
        var run = CaretAtEnd("~~paragraph~~ done").BuildVisualLineForTest(0).Runs
            .First(r => r.Kind == VisualRunKind.Text && r.Text == "paragraph");

        var decoration = Assert.Single(run.Style.Decorations!);
        Assert.Equal(TextDecorationLocation.Strikethrough, decoration.Location);
        Assert.NotNull(decoration.Stroke); // an explicit stroke so the hand-drawn line has a colour
    }

    [AvaloniaFact]
    public void PlainText_HasNoStrikethrough()
    {
        var run = CaretAtEnd("plain text").BuildVisualLineForTest(0).Runs
            .First(r => r.Kind == VisualRunKind.Text);
        Assert.Null(run.Style.Decorations);
    }
}
