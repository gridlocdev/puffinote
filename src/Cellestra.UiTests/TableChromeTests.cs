using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class TableChromeTests
{
    [AvaloniaFact]
    public void Table_filter_and_validation_chrome_render_without_throwing()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        // A table (banding + header band + filter buttons).
        sheet.SetValue(0, 0, CellValue.Text("Name"));
        sheet.SetValue(0, 1, CellValue.Text("Score"));
        sheet.SetValue(1, 0, CellValue.Text("Ada")); sheet.SetValue(1, 1, CellValue.Number(1));
        sheet.SetValue(2, 0, CellValue.Text("Bev")); sheet.SetValue(2, 1, CellValue.Number(2));
        env.Selection.MoveTo(0, 0);
        AppCommands.ById("data.createTable")!.Execute(env);

        // A list-validated cell (validation dropdown on the active cell).
        sheet.AddValidation(new DataValidation(ValidationKind.List, CellRange.Parse("D1")) { ListValues = new[] { "x", "y" } });
        env.Selection.MoveTo(0, 3);

        // An AutoFilter range (filter buttons).
        sheet.ActiveFilter = CellRange.Parse("A1:B3");
        env.Refresh();

        // Forces a render pass; throws if any chrome drawing is broken.
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }
}
