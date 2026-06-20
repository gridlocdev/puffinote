using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Typing a formula shows a positional argument hint: the function signature with the argument the
/// caret is in emphasized, advancing as commas are typed and hiding when the edit ends.
/// </summary>
public class FormulaSignatureHelpTests
{
    private static void Pump() => Dispatcher.UIThread.RunJobs();

    [AvaloniaFact]
    public void Hint_appears_and_tracks_the_active_argument()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        env.Selection.MoveTo(0, 0);
        env.BeginEdit();
        Pump();

        window.KeyTextInput("=ROUND(");
        Pump();
        Assert.True(window.Editor.Grid.IsSignatureHelpVisible);
        Assert.Equal("number", window.Editor.Grid.ActiveSignatureArgument);

        window.KeyTextInput("A1, ");
        Pump();
        Assert.Equal("[digits]", window.Editor.Grid.ActiveSignatureArgument); // second, optional argument

        window.KeyTextInput(")");
        Pump();
        Assert.False(window.Editor.Grid.IsSignatureHelpVisible); // caret past the call — hint gone
    }

    [AvaloniaFact]
    public void Hint_hides_for_a_non_formula_entry()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        env.Selection.MoveTo(0, 0);
        env.BeginEdit();
        Pump();

        window.KeyTextInput("hello");
        Pump();
        Assert.False(window.Editor.Grid.IsSignatureHelpVisible);
    }
}
