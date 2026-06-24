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
