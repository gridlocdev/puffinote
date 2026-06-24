using Typage.Control.Controls;
using Typage.Desktop.Services;
using Typage.Desktop.Views;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Typage.UiTests;

/// <summary>The raw-source view toggle and distraction-free focus mode (PRD §7.4).</summary>
public class ViewModeTests
{
    [AvaloniaFact]
    public void Editor_ShowRawSource_IsOffByDefault()
    {
        Assert.False(new MarkdownEditor().ShowRawSource);
    }

    [AvaloniaFact]
    public void Window_AppliesRawSourceDefaultFromSettings()
    {
        var settings = new SettingsService();
        settings.Settings.ShowRawSource = true;
        var window = new MainWindow(settings);
        Assert.True(window.EditorView.Editor.ShowRawSource);
    }

    [AvaloniaFact]
    public void RawSourceToggle_FlipsEditorAndSetting()
    {
        var settings = new SettingsService();
        var window = new MainWindow(settings);
        Assert.False(window.EditorView.Editor.ShowRawSource);

        window.ToggleRawSourceForTest();
        Assert.True(window.EditorView.Editor.ShowRawSource);
        Assert.True(settings.Settings.ShowRawSource);
    }

    [AvaloniaFact]
    public void FocusMode_HidesAndRestoresChrome()
    {
        var window = new MainWindow(new SettingsService());
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
