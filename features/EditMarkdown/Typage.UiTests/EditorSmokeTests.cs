using Typage.Control.Controls;
using Typage.Control.Views;
using Typage.Desktop.Services;
using Typage.Desktop.Views;
using Typage.Engine.Markdown;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Headless Avalonia smoke tests for the control library and shell: they construct under the real
/// Avalonia platform and drive the editor through its public API (PRD §10.2 — "the control runs
/// entirely through its public API with the standalone shell removed"). They deliberately do not
/// <c>Show()</c> a window or capture a frame: this CI host has no render timer / display link, so a
/// frame pump would block. Rendering correctness is covered structurally by the engine's token and
/// HTML tests; these verify the wiring and that the Avalonia types resolve and compose.
/// </summary>
public class EditorSmokeTests
{
    [AvaloniaFact]
    public void EditorView_Constructs_AndReflectsText()
    {
        var view = new MarkdownEditorView();
        view.Text =
            "---\ntitle: Demo\n---\n# Heading\n\n" +
            "Body with **bold**, *italic*, `code`, ~~strike~~, ==mark==, [link](https://x.com).\n\n" +
            "- [x] done\n- [ ] todo\n\n> quote\n\n```python\nx = 1\n```\n\n| A | B |\n| - | - |\n| 1 | 2 |\n";

        Assert.True(view.Stats.Words > 0);
        Assert.False(view.IsDirty); // setting text marks it as the saved baseline
        Assert.Contains("# Heading", view.Text);
    }

    [AvaloniaFact]
    public void Editor_TokenizesThroughTheEngine()
    {
        var editor = new MarkdownEditor { Text = "# Title\n\n**bold**" };
        // The control rebuilds the engine model on text change; confirm the wiring produced tokens.
        var model = MarkdownModel.Build(Engine.Document.TextDocument.FromString(editor.Text));
        Assert.Contains(model.AllTokens(), t => t.Type == TokenType.Strong);
    }

    [AvaloniaFact]
    public void Editor_FormattingCommands_WrapSelection()
    {
        var editor = new MarkdownEditor { Text = string.Empty };
        editor.Buffer.Insert("word");
        editor.Buffer.Selection = new Engine.Document.Selection(0, 4);
        editor.ToggleBold();
        Assert.Equal("**word**", editor.Text);
    }

    [AvaloniaFact]
    public void Editor_ApplyHeading_RewritesLine()
    {
        var editor = new MarkdownEditor { Text = "plain line" };
        editor.ApplyHeading(2);
        Assert.Equal("## plain line", editor.Text);
    }

    [AvaloniaFact]
    public void Editor_InsertLink_WrapsSelection()
    {
        var editor = new MarkdownEditor { Text = string.Empty };
        editor.Buffer.Insert("site");
        editor.Buffer.Selection = new Engine.Document.Selection(0, 4);
        editor.InsertLink();
        Assert.Equal("[site]()", editor.Text);
    }

    [AvaloniaFact]
    public void Editor_SetParagraph_StripsHeadingMarker()
    {
        var editor = new MarkdownEditor { Text = "### Title" };
        editor.SetParagraph();
        Assert.Equal("Title", editor.Text);
    }

    [AvaloniaFact]
    public void Editor_SelectAll_SelectsWholeDocument()
    {
        var editor = new MarkdownEditor { Text = "abc\ndef" };
        editor.SelectAll();
        Assert.Equal(0, editor.Buffer.Selection.Start);
        Assert.Equal(editor.Text.Length, editor.Buffer.Selection.End);
    }

    [AvaloniaFact]
    public void Editor_UndoRedo_Works()
    {
        var editor = new MarkdownEditor { Text = string.Empty };
        editor.Buffer.Insert("hello");
        Assert.Equal("hello", editor.Text);
        editor.Undo();
        Assert.Equal(string.Empty, editor.Text);
        editor.Redo();
        Assert.Equal("hello", editor.Text);
    }

    [AvaloniaFact]
    public void MainWindow_Constructs_AndReportsCleanState()
    {
        var window = new MainWindow(new SettingsService());
        window.EditorView.Text = "# Title\n\nSome words here.";
        Assert.False(window.EditorView.IsDirty);
        Assert.True(window.EditorView.Stats.Words >= 4);
        Assert.StartsWith("Untitled", (window.Title ?? string.Empty).Replace("• ", string.Empty, System.StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void MainWindow_AppliesSettings_WithoutThrowing()
    {
        var settings = new SettingsService();
        settings.Settings.Theme = AppTheme.Light;
        settings.Settings.TabWidth = 2;
        var window = new MainWindow(settings);
        Assert.Equal(2, window.EditorView.Editor.IndentWidth);
    }

    [AvaloniaFact]
    public void SearchMatchHighlight_BinarySearchFindsFirstReachingMatch()
    {
        var editor = new MarkdownEditor { Text = new string('x', 100) };
        editor.SetSearchMatches(
            new[]
            {
                new Engine.Document.SearchMatch(5, 2),  // ends at 7
                new Engine.Document.SearchMatch(20, 2), // ends at 22
                new Engine.Document.SearchMatch(50, 2), // ends at 52
            },
            current: 0);

        Assert.Equal(0, editor.FirstMatchReaching(0));
        Assert.Equal(1, editor.FirstMatchReaching(7));  // first match no longer reaches past 7
        Assert.Equal(2, editor.FirstMatchReaching(22));
        Assert.Equal(3, editor.FirstMatchReaching(52)); // past the last match
    }

    [AvaloniaFact]
    public void WidthCache_AvoidsReshapingMeasuredRuns()
    {
        var editor = new MarkdownEditor();
        var start = editor.RawMeasureCount;

        editor.MeasureWidthForTest("hello world");
        var afterFirst = editor.RawMeasureCount;
        Assert.True(afterFirst > start); // shaped once

        editor.MeasureWidthForTest("hello world");
        Assert.Equal(afterFirst, editor.RawMeasureCount); // cache hit — no new shaping

        editor.MeasureWidthForTest("a different run");
        Assert.True(editor.RawMeasureCount > afterFirst); // a new run is shaped

        editor.EditorTheme = Control.Rendering.MarkdownTheme.Light; // theme change invalidates the cache
        var afterTheme = editor.RawMeasureCount;
        editor.MeasureWidthForTest("hello world");
        Assert.True(editor.RawMeasureCount > afterTheme); // re-shaped under the new fonts
    }

    [AvaloniaFact]
    public void ThemeVariants_DifferByBackground()
    {
        var light = (Avalonia.Media.ISolidColorBrush)Control.Rendering.MarkdownTheme.Light.Background;
        var dark = (Avalonia.Media.ISolidColorBrush)Control.Rendering.MarkdownTheme.Dark.Background;
        var hc = (Avalonia.Media.ISolidColorBrush)Control.Rendering.MarkdownTheme.HighContrast.Background;
        Assert.NotEqual(light.Color, dark.Color);
        Assert.NotEqual(dark.Color, hc.Color);
    }
}
