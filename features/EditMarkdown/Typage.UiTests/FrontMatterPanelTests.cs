using Typage.Control.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Detection of the leading front-matter "Properties" block (PRD §7.4). The panel is drawn under
/// the text without altering glyph layout, so this verifies the range the backdrop spans; the draw
/// itself can't be pixel-tested headlessly.
/// </summary>
public class FrontMatterPanelTests
{
    [AvaloniaFact]
    public void DetectsFrontMatterRange()
    {
        var editor = new MarkdownEditor { Text = "---\ntitle: Demo\ntags: [a, b]\n---\n# Body" };
        var block = editor.FrontMatterBlockForTest();
        Assert.NotNull(block);
        Assert.Equal(0, block!.Value.Start);
        Assert.Equal(3, block.Value.End); // the closing "---" is on line 3
    }

    [AvaloniaFact]
    public void NoBlockWithoutFrontMatter()
    {
        var editor = new MarkdownEditor { Text = "# Just a heading\n\nBody text" };
        Assert.Null(editor.FrontMatterBlockForTest());
    }

    [AvaloniaFact]
    public void LoneDashesAreNotFrontMatter()
    {
        // A leading "---" with no closing fence is a horizontal rule, not front matter.
        var editor = new MarkdownEditor { Text = "---\n\nbody" };
        Assert.Null(editor.FrontMatterBlockForTest());
    }
}
