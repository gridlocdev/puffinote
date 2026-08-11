using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Text.Document;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Xunit;

namespace Typage.UiTests;

/// <summary>The raw-source view toggle and distraction-free focus mode (PRD §7.4).</summary>
public class ViewModeTests
{
    [AvaloniaFact]
    public void Editor_View_DefaultsToRich()
    {
        var e = new MarkdownEditor();
        Assert.Equal(MarkdownEditor.ViewMode.Rich, e.View);
        Assert.False(e.ShowRawSource);
    }

    [AvaloniaFact]
    public void ShowRawSource_ShimMapsToFormattedAndRich()
    {
        var e = new MarkdownEditor { ShowRawSource = true };
        Assert.Equal(MarkdownEditor.ViewMode.Formatted, e.View);
        e.ShowRawSource = false;
        Assert.Equal(MarkdownEditor.ViewMode.Rich, e.View);
        // RawText also reads as "raw source" (markers revealed) but is not reachable via the bool setter.
        e.View = MarkdownEditor.ViewMode.RawText;
        Assert.True(e.ShowRawSource);
    }

    [AvaloniaFact]
    public void Window_AppliesViewModeDefaultFromSettings()
    {
        var settings = TestShell.NewSettings();
        settings.Settings.View = MarkdownEditor.ViewMode.RawText;
        var window = TestShell.NewWindow(settings);
        Assert.Equal(MarkdownEditor.ViewMode.RawText, window.EditorView.Editor.View);
    }

    [AvaloniaFact]
    public void SetViewMode_UpdatesEditorAndSetting()
    {
        // State the starting mode rather than trusting the persisted default: a sibling test opens a window in
        // RawText, and the shell persists a view change to the config file the whole run shares
        // (TestConfigIsolation gives the run one throwaway dir, not one per test). What this test is about is
        // the *change* — that setting a mode reaches both the editor and the settings — so it says where it
        // starts from instead of depending on which test ran before it.
        var settings = TestShell.NewSettings();
        settings.Settings.View = MarkdownEditor.ViewMode.Rich;
        var window = TestShell.NewWindow(settings);
        Assert.Equal(MarkdownEditor.ViewMode.Rich, window.EditorView.Editor.View);

        window.SetViewModeForTest(MarkdownEditor.ViewMode.RawText);
        Assert.Equal(MarkdownEditor.ViewMode.RawText, window.EditorView.Editor.View);
        Assert.Equal(MarkdownEditor.ViewMode.RawText, settings.Settings.View);
    }

    [AvaloniaFact]
    public void SwitchingViewMode_KeepsTheReadersPlace_AndTheCaret()
    {
        // Every mode has its own row heights, paragraph grouping and collapsed blanks, so the same scroll
        // offset in pixels lands on different text after a switch — the view used to jump somewhere else in
        // the document. The text at the top of the viewport is what must not move.
        // Hard-wrapped prose: one flowing block in the WYSIWYG view, sixty separate rows in the source one —
        // as different as the two layouts get, which is the point.
        var text = string.Join("\n", Enumerable.Range(0, 60).Select(i => $"sentence {i} with a few words in it"));
        var editor = new MarkdownEditor { Text = text };
        var window = new Window { Width = 500, Height = 300, Content = editor };
        window.Show();
        window.CaptureRenderedFrame(); // arrange, so there is a viewport to scroll inside

        ((IScrollable)editor).Offset = new Vector(0, 900);
        window.CaptureRenderedFrame();

        var document = editor.Buffer.Document;
        var anchor = editor.TopOfViewportOffsetForTest() ?? 0;
        Assert.True(document.GetPosition(anchor).Line > 0, "the test needs to be scrolled away from the top");

        editor.Buffer.Selection = Selection.At(anchor + 3);
        var caret = editor.Buffer.Selection.Caret;

        foreach (var mode in new[] { MarkdownEditor.ViewMode.RawText, MarkdownEditor.ViewMode.Formatted, MarkdownEditor.ViewMode.Rich })
        {
            editor.View = mode;
            window.CaptureRenderedFrame();

            // The text that was at the top is still in the first row of the viewport — not scrolled past
            // (negative) and not pushed down the page, whatever the new layout did to the rows above it.
            Assert.InRange(editor.ViewportYForTest(anchor), 0, MarkdownTheme.Default.BodyRowHeight);
            Assert.Equal(caret, editor.Buffer.Selection.Caret); // the caret is an offset; the document didn't change
        }
    }

    [AvaloniaFact]
    public void FocusMode_HidesAndRestoresChrome()
    {
        var window = TestShell.NewWindow();
        Assert.True(window.StatusBarVisibleForTest);
        Assert.Equal(CommandBarVisibility.Visible, window.ToolbarStateForTest);

        window.ToggleFocusModeForTest();
        Assert.False(window.StatusBarVisibleForTest);
        Assert.Equal(CommandBarVisibility.Hidden, window.ToolbarStateForTest);

        window.ToggleFocusModeForTest();
        Assert.True(window.StatusBarVisibleForTest);
        Assert.Equal(CommandBarVisibility.Visible, window.ToolbarStateForTest); // restored to the toolbar preference
    }

    [AvaloniaFact]
    public void FocusMode_RestoresACollapsedToolbarWithoutOverwritingThePreference()
    {
        var settings = TestShell.NewSettings();
        var window = TestShell.NewWindow(settings);

        window.SetToolbarForTest(CommandBarVisibility.Collapsed);
        Assert.Equal(CommandBarVisibility.Collapsed, settings.Settings.CommandBar);

        // Focus mode borrows the toolbar's row; that is not the reader choosing to hide it, so the
        // preference must survive the round trip and come back as the slim handle, not the full bar.
        window.ToggleFocusModeForTest();
        Assert.Equal(CommandBarVisibility.Hidden, window.ToolbarStateForTest);
        Assert.Equal(CommandBarVisibility.Collapsed, settings.Settings.CommandBar);

        window.ToggleFocusModeForTest();
        Assert.Equal(CommandBarVisibility.Collapsed, window.ToolbarStateForTest);
        Assert.Equal(CommandBarVisibility.Collapsed, settings.Settings.CommandBar);
    }
}
