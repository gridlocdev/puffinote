using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cellestra.Core.Styling;
using Cellestra.Desktop.Views.Dialogs;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The first-party Avalonia ColorPicker resolves its Fluent control theme (so it isn't a blank,
/// templateless control) and the Format Cells dialog builds with it.
/// </summary>
public class FormatCellsColorPickerTests
{
    [AvaloniaFact]
    public void Color_picker_resolves_its_control_theme()
    {
        var picker = new ColorPicker { Color = Colors.Red };
        var window = new Window { Content = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        picker.ApplyTemplate();

        Assert.True(picker.GetVisualChildren().Any(),
            "ColorPicker produced no visual template — its theme is not registered.");
    }

    [AvaloniaFact]
    public void Format_cells_dialog_builds_with_color_pickers()
    {
        var dialog = new FormatCellsDialog(CellStyle.Default);
        Assert.Equal("Format Cells", dialog.Title);
    }
}
