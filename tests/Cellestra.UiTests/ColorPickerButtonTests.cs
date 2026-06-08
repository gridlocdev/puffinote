using Avalonia.Headless.XUnit;
using Cellestra.Core.Styling;
using Cellestra.Desktop.Controls;
using Cellestra.Desktop.Views.Dialogs;
using Xunit;

namespace Cellestra.UiTests;

public class ColorPickerButtonTests
{
    [AvaloniaFact]
    public void Reports_its_initial_color_and_raises_changes()
    {
        var picker = new ColorPickerButton(ArgbColor.FromHex("#1E88E5"), allowNone: false);
        Assert.Equal(ArgbColor.FromHex("#1E88E5"), picker.Color);

        int changes = 0;
        picker.ColorChanged += (_, _) => changes++;
        picker.Color = ArgbColor.FromHex("#E53935");

        Assert.Equal(ArgbColor.FromHex("#E53935"), picker.Color);
        Assert.Equal(1, changes);
    }

    [AvaloniaFact]
    public void Supports_no_color_when_allowed()
    {
        var picker = new ColorPickerButton(initial: null, allowNone: true);
        Assert.Null(picker.Color);

        picker.Color = ArgbColor.White;
        Assert.Equal(ArgbColor.White, picker.Color);

        picker.Color = null; // back to "no fill"
        Assert.Null(picker.Color);
    }

    [AvaloniaFact]
    public void Format_cells_dialog_constructs_with_the_pickers()
    {
        var dialog = new FormatCellsDialog(CellStyle.Default);
        Assert.Equal("Format Cells", dialog.Title);
    }
}
