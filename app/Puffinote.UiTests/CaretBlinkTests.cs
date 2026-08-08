using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Puffinote.Components.Input;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The other half of <see cref="CaretBlink"/>: the shell types into <c>TextBox</c>es (search, rename, the
/// embedded spreadsheet's cell editor and formula bar) as well as into the two custom surfaces that paint
/// their own caret. Both halves have to blink at the same rate, because in Puffinote they are one window —
/// a spreadsheet in one tab and a Markdown document in the next, a search box above both.
/// </summary>
public class CaretBlinkTests
{
    private static TextBox ShownTextBox()
    {
        var box = new TextBox();
        var window = new Window { Width = 300, Height = 120, Content = box };
        window.Show();
        window.UpdateLayout();
        return box;
    }

    [AvaloniaFact]
    public void A_TextBox_in_the_shell_blinks_at_the_family_rate()
    {
        var box = ShownTextBox();

        Assert.Equal(CaretBlink.Interval, box.CaretBlinkInterval);

        // …and it got there from the app's styles, not by coincidentally matching whatever default the
        // control shipped with. Without this the assertion above would keep passing after the app stopped
        // registering CaretBlink.TextBoxStyles() — right up until an Avalonia upgrade changed the default
        // and every TextBox in the shell silently drifted off the rate the custom surfaces blink at.
        Assert.Equal(BindingPriority.Style, box.GetDiagnostic(TextBox.CaretBlinkIntervalProperty).Priority);
    }

    [AvaloniaFact]
    public void The_rate_reaches_the_presenter_that_actually_blinks()
    {
        // Setting the property on the TextBox is only worth anything if its template carries the value down
        // to the TextPresenter, which owns the blink timer. Assert the delivery, not just the intent.
        var presenter = ShownTextBox().GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();

        Assert.NotNull(presenter);
        Assert.Equal(CaretBlink.Interval, presenter.CaretBlinkInterval);
    }
}
