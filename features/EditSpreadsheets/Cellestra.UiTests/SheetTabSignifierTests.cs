using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Cellestra.Editor;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The active sheet tab is marked by its surface and an accent underline — never by a heavier font.
/// Bold made the active tab wider than the same tab inactive, so switching sheets shuffled the strip
/// sideways under the pointer (and under the drag-to-reorder bounds maths).
/// </summary>
public class SheetTabSignifierTests
{
    private static SpreadsheetEditorView TwoSheetView(ThemeVariant variant)
    {
        var view = new SpreadsheetEditorView();
        var window = new Window { Content = view, RequestedThemeVariant = variant };
        window.Show();
        view.AddSheet(); // a second sheet, which becomes active — so tab 0 is inactive, tab 1 active
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    private static FontWeight LabelWeight(Border tab) => ((TextBlock)tab.Child!).FontWeight;

    [AvaloniaFact]
    public void Active_and_inactive_tabs_share_one_font_weight()
    {
        var view = TwoSheetView(ThemeVariant.Dark);

        var inactive = view.TabForTest(0)!;
        var active = view.TabForTest(1)!;

        Assert.Equal(FontWeight.Normal, LabelWeight(active));
        Assert.Equal(LabelWeight(inactive), LabelWeight(active));
    }

    [AvaloniaFact]
    public void The_active_tab_carries_the_accent_underline_and_no_other_tab_does()
    {
        var view = TwoSheetView(ThemeVariant.Dark);

        var inactive = view.TabForTest(0)!;
        var active = view.TabForTest(1)!;

        Assert.NotEqual(Brushes.Transparent, active.BorderBrush);
        Assert.Equal(Brushes.Transparent, inactive.BorderBrush);

        // Same reserved edge on both, so activating a tab recolors it rather than resizing the strip.
        Assert.Equal(new Avalonia.Thickness(0, 0, 0, 2), active.BorderThickness);
        Assert.Equal(active.BorderThickness, inactive.BorderThickness);
    }

    [AvaloniaFact]
    public void The_underline_is_lightened_for_dark_themes_so_it_stays_visible()
    {
        var dark = ((ISolidColorBrush)TwoSheetView(ThemeVariant.Dark).TabForTest(1)!.BorderBrush!).Color;
        var light = ((ISolidColorBrush)TwoSheetView(ThemeVariant.Light).TabForTest(1)!.BorderBrush!).Color;

        Assert.NotEqual(light, dark);
        Assert.True(dark.G > light.G, $"the dark-theme underline should be the brighter green, was {dark} vs {light}");
    }
}
