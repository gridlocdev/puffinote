using System.Globalization;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Editing;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

public class MarqueeTests
{
    [AvaloniaFact]
    public void Copy_renders_the_marquee_and_editing_clears_it()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;

        env.Session.Execute(EditCommands.SetValue(sheet.Name, 0, 0, CellValue.Number(5)));
        env.Selection.MoveTo(0, 0);

        // Copy sets the marquee; rendering exercises DrawMarquee through real Skia.
        window.Editor.Copy();
        window.AssertRenders();

        // Starting an edit must clear the marquee/clipboard without error.
        window.Editor.BeginEdit();
        window.AssertRenders();
    }
}
