using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Control.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Each code block (fenced or indented) shows a copy button at its top-right; clicking it copies the
/// block's source. These cover the copied-text extraction and that a button is recorded per code run.
/// </summary>
public class CodeCopyButtonTests
{
    [AvaloniaFact]
    public void FencedBlock_CopiesInnerLines_WithoutTheFences()
    {
        var e = new MarkdownEditor { Text = "```\nx = 1\ny = 2\n```" };
        Assert.Equal("x = 1\ny = 2", e.CodeBlockTextForTest(0, 3));
    }

    [AvaloniaFact]
    public void FencedBlock_WithLanguage_StillStripsTheFences()
    {
        var e = new MarkdownEditor { Text = "```csharp\nvar a = 1;\n```" };
        Assert.Equal("var a = 1;", e.CodeBlockTextForTest(0, 2));
    }

    [AvaloniaFact]
    public void IndentedBlock_CopiesLinesDedentedByOneLevel()
    {
        // A blank line above opens the indented block (CommonMark §4.4); copy lines 1-2 dedented.
        var e = new MarkdownEditor { Text = "para\n\n    code one\n    code two\n" };
        Assert.Equal("code one\ncode two", e.CodeBlockTextForTest(2, 3));
    }

    [AvaloniaFact]
    public void TabIndentedBlock_IsDedentedByTheTab()
    {
        var e = new MarkdownEditor { Text = "para\n\n\tcode\n" };
        Assert.Equal("code", e.CodeBlockTextForTest(2, 2));
    }

    [AvaloniaFact]
    public void IndentedBlock_WithAWhitespaceOnlyInteriorBlank_CopiesAcrossTheBlank()
    {
        // A "blank" interior line with a couple of spaces must not split the block, so copying the run
        // still spans both code lines with the empty interior line between them.
        var e = new MarkdownEditor { Text = "para\n\n    a\n  \n    b\n" };
        Assert.Equal("a\n\nb", e.CodeBlockTextForTest(2, 4));
    }

    [AvaloniaFact]
    public void Rendering_RecordsOneButton_SpanningTheWholeFencedRun()
    {
        var view = new MarkdownEditorView();
        var window = new Window { Width = 480, Height = 320, Content = view };
        window.Show();

        view.Text = "intro\n\n```\nx = 1\ny = 2\n```\n";
        window.CaptureRenderedFrame();

        var buttons = view.Editor.CodeCopyButtonsForTest();
        var fenced = buttons.FirstOrDefault(b => b.First == 2);
        Assert.Equal(2, fenced.First); // the opening fence line
        Assert.Equal(5, fenced.Last);  // through the closing fence line
    }
}
