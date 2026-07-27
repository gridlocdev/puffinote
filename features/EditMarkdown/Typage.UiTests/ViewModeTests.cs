using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
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
    public void FocusMode_HidesAndRestoresChrome()
    {
        var window = TestShell.NewWindow();
        Assert.True(window.StatusBarVisibleForTest);
        Assert.True(window.ToolbarVisibleForTest);

        window.ToggleFocusModeForTest();
        Assert.False(window.StatusBarVisibleForTest);
        Assert.False(window.ToolbarVisibleForTest);

        window.ToggleFocusModeForTest();
        Assert.True(window.StatusBarVisibleForTest);
        Assert.True(window.ToolbarVisibleForTest); // restored to the toolbar preference
    }
}
