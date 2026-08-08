using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Headless.XUnit;
using Puffinote.Components.Input;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Cellestra types into <c>TextBox</c>es — the in-cell editor and the formula bar — rather than painting
/// its own caret, so all it owes the family is the shared blink rate (<see cref="CaretBlink"/>). It is the
/// app where the mismatch was easiest to see: in the Puffinote shell a cell and a Markdown document are one
/// tab apart, and Typage's and Plaintain's surfaces paint their caret at exactly this rate.
/// </summary>
public class CaretBlinkTests
{
    [AvaloniaFact]
    public void The_app_puts_its_TextBoxes_on_the_family_rate()
    {
        var box = new TextBox();
        var window = new Window { Width = 300, Height = 120, Content = box };
        window.Show();
        window.UpdateLayout();

        Assert.Equal(CaretBlink.Interval, box.CaretBlinkInterval);

        // From the app's styles, not from Avalonia's default happening to match today: this is what fails
        // if App.Initialize ever stops registering CaretBlink.TextBoxStyles().
        Assert.Equal(BindingPriority.Style, box.GetDiagnostic(TextBox.CaretBlinkIntervalProperty).Priority);
    }
}
