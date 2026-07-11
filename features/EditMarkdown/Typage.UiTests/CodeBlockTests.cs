using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Typage.Control.Rendering;
using Typage.Control.Views;
using Typage.Engine.Markdown;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Code-block presentation (item 6/7): block code text carries no tight per-run background — the
/// continuous block panel behind the fence/indent is its only background — while inline code keeps
/// its pill. Indented code blocks render through the same panel path.
/// </summary>
public class CodeBlockTests
{
    [AvaloniaFact]
    public void CodeBlockText_HasNoPerRunBackground()
    {
        Assert.Null(MarkdownTheme.Dark.Resolve(TokenType.CodeText).Background);
        Assert.Null(MarkdownTheme.Light.Resolve(TokenType.CodeText).Background);
        Assert.Null(MarkdownTheme.HighContrast.Resolve(TokenType.CodeText).Background);
    }

    [AvaloniaFact]
    public void InlineCode_KeepsItsPillBackground()
    {
        Assert.NotNull(MarkdownTheme.Dark.Resolve(TokenType.InlineCode).Background);
    }

    [AvaloniaFact]
    public void TrailingBlankBelowIndentedCode_IsNotPanelled()
    {
        // The indented run is lines 0-1; line 2 is the empty line the caret sits on while typing. It must
        // not be highlighted as code (the block has ended — nothing more indented follows).
        var e = new Typage.Control.Controls.MarkdownEditor { Text = "    code one\n    code two\n\nprose" };
        Assert.True(e.IsCodeLineForTest(0));
        Assert.True(e.IsCodeLineForTest(1));
        Assert.False(e.IsCodeLineForTest(2), "the trailing blank below the indented block isn't code");
        Assert.False(e.IsCodeLineForTest(3));
    }

    [AvaloniaFact]
    public void InteriorBlankBetweenIndentedCodeLines_StaysPanelled()
    {
        // A blank line *between* two indented code lines is interior — it keeps the panel continuous.
        var e = new Typage.Control.Controls.MarkdownEditor { Text = "    code one\n\n    code two\nprose" };
        Assert.True(e.IsCodeLineForTest(0));
        Assert.True(e.IsCodeLineForTest(1), "the blank between two indented code lines stays code");
        Assert.True(e.IsCodeLineForTest(2));
        Assert.False(e.IsCodeLineForTest(3));
    }

    [AvaloniaFact]
    public void AHeadingBelowAnIndentedCodeBlock_IsNotPanelled()
    {
        // The heading is still *entered* with the IndentedCode state (the block ends lazily, on this very line),
        // so trusting that state panelled the heading as part of the code above it.
        var e = new Typage.Control.Controls.MarkdownEditor { Text = "text\n\n    indented code\n\n## Heading\n\nmore" };
        Assert.True(e.IsCodeLineForTest(2));
        Assert.False(e.IsCodeLineForTest(4), "the heading below an indented code block isn't code");
        Assert.False(e.IsCodeLineForTest(6), "nor is the prose under it");
    }

    [AvaloniaFact]
    public void InlineCode_ReservesTheChipsPadding_OnBothSides()
    {
        // The chip is drawn bled past its text, so it needs that room reserved in the layout: the concealed
        // backticks hold it open. Without it the chip was painted over the tail of the word before it — the code
        // looked "cut off" at the edges of its own pill.
        var theme = MarkdownTheme.Default;
        var e = new Typage.Control.Controls.MarkdownEditor { Text = "say `code` here\nx" };
        var v = e.BuildVisualLineForTest(0);

        var pads = v.Runs.Where(r => r.Decoration == DecorationKind.ChipPad).ToList();
        Assert.Equal(2, pads.Count);
        Assert.All(pads, p => Assert.Equal(theme.InlineCodePadding, p.Width, 3));

        var before = v.Runs.First(r => r.Text == "say ");
        var code = v.Runs.First(r => r.Text == "code");
        var after = v.Runs.First(r => r.Text == " here");

        Assert.Equal(theme.InlineCodePadding, code.X - (before.X + before.Width), 3);
        Assert.Equal(theme.InlineCodePadding, after.X - (code.X + code.Width), 3);
        Assert.True(theme.InlineCodePadding > 0);
    }

    [AvaloniaFact]
    public void TheChipOfAWrappedInlineCodeSpan_IsContinuous()
    {
        // With wrapping on, a code span lays out one word/space piece at a time and each piece draws its own chip.
        // Sizing those chips by FormattedText.Width — which omits trailing whitespace — collapsed the chip of the
        // space between two code words to nothing, splitting `inline code` into two chips with a hole between them.
        var view = new MarkdownEditorView();
        var window = new Window { Width = 400, Height = 200, Content = view };
        window.Show();
        view.Text = "say `inline code` here";
        window.CaptureRenderedFrame(); // force a layout pass, so wrapping (and its piece-splitting) is live

        var editor = view.Editor;
        var v = editor.BuildVisualLineForTest(0);
        var pieces = v.Runs.Where(r => r.Kind == VisualRunKind.Text && r.Style.Background is not null).ToList();

        Assert.Contains(pieces, r => r.Text == " "); // the span really did split on its space
        var pad = MarkdownTheme.Default.InlineCodePadding;
        var chips = pieces.ConvertAll(r => editor.ChipRectForTest(r, 0, 26));

        // Each chip spans its piece's laid-out advance — the space piece included, which is the one
        // FormattedText.Width reports as zero-width.
        for (var i = 0; i < chips.Count; i++)
        {
            Assert.Equal(pieces[i].Width + (pad * 2), chips[i].Width, 3);
        }

        // So consecutive chips meet: no gap, and the span reads as one pill.
        for (var i = 1; i < chips.Count; i++)
        {
            Assert.True(chips[i - 1].Right >= chips[i].X,
                $"a gap opens between the chips of \"{pieces[i - 1].Text}\" and \"{pieces[i].Text}\"");
        }
    }

    [AvaloniaFact]
    public void TheInlineCodeChip_StaysOffTheWordsBesideIt()
    {
        // The chip is drawn bled past its text; the bleed must land in the padding the layout reserved (the
        // concealed backticks), not on the neighbouring words — whose glyphs it used to paint over.
        var editor = new Typage.Control.Controls.MarkdownEditor { Text = "say `code` here\nx" };
        var v = editor.BuildVisualLineForTest(0);

        var before = v.Runs.First(r => r.Text == "say ");
        var code = v.Runs.First(r => r.Text == "code");
        var after = v.Runs.First(r => r.Text == " here");
        var chip = editor.ChipRectForTest(code, 0, 26);

        Assert.True(chip.X >= before.X + before.Width, "the chip paints over the word before it");
        Assert.True(chip.Right <= after.X, "the chip paints over the word after it");
        Assert.True(chip.Width > code.Width, "the chip should be padded around its code, not flush with it");
    }

    [AvaloniaFact]
    public void IndentedCodeBlock_Renders_WithoutThrowing()
    {
        var view = new MarkdownEditorView();
        var window = new Window { Width = 400, Height = 240, Content = view };
        window.Show();

        view.Text = "a paragraph\n\n    indented code line\n    second code line\n\nmore prose\n";
        window.CaptureRenderedFrame(); // exercises the code panel over an indented block

        Assert.True(view.Editor.RawMeasureCount > 0);
    }
}
