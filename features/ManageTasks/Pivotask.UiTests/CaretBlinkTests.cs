using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Headless.XUnit;
using Puffinote.Components.Input;
using Xunit;

namespace Pivotask.UiTests;

/// <summary>
/// Pivotask has no custom-drawn caret — every field a task is typed into is an Avalonia
/// <c>TextBox</c> — so all it owes the family is the shared blink rate (<see cref="CaretBlink"/>).
/// That matters here because these same cards are edited inside the Puffinote shell, a tab away from
/// Typage's and Plaintain's surfaces, which paint their own caret at exactly this rate.
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
