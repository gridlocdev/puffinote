using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Puffinote.Components.Controls;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The shared settings form every app's Preferences dialog is built from. The contract that matters is
/// that nothing reaches the settings object until <see cref="SettingsEditorView.Commit"/> is called — that
/// is what makes Cancel and Esc genuinely free in all five apps.
/// </summary>
public class SettingsEditorViewTests
{
    private enum Mode
    {
        First,
        Second,
        Third,
    }

    private sealed class Prefs
    {
        public bool Flag { get; set; }

        public double Size { get; set; } = 10;

        public Mode Choice { get; set; } = Mode.First;
    }

    private static SettingsEditorView Show(SettingsEditorView view)
    {
        var window = new Window { Width = 500, Height = 400, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    private static SettingsEditorView FormOver(Prefs p) => Show(new SettingsEditorView(
    [
        new SettingsSection("Group", [new ToggleSetting("Flag", () => p.Flag, v => p.Flag = v)]),
        new SettingsSection("Numbers", [new NumberSetting("Size", () => p.Size, v => p.Size = v, 1, 100)]),
        new SettingsSection("Choices",
        [
            ChoiceSetting.ForEnum<Mode>("Choice", () => p.Choice, v => p.Choice = v, ["One", "Two", "Three"]),
        ]),
    ]));

    private static T Editor<T>(SettingsEditorView view)
        where T : Control =>
        view.GetLogicalDescendants().OfType<T>().First();

    [AvaloniaFact]
    public void Every_editor_opens_seeded_from_the_current_value()
    {
        var prefs = new Prefs { Flag = true, Size = 42, Choice = Mode.Third };
        var form = FormOver(prefs);

        Assert.True(Editor<CheckBox>(form).IsChecked);
        Assert.Equal(42m, Editor<NumericUpDown>(form).Value);
        Assert.Equal(2, Editor<ComboBox>(form).SelectedIndex);
    }

    [AvaloniaFact]
    public void Editing_a_field_changes_nothing_until_Commit()
    {
        var prefs = new Prefs();
        var form = FormOver(prefs);

        Editor<CheckBox>(form).IsChecked = true;
        Editor<NumericUpDown>(form).Value = 25m;
        Editor<ComboBox>(form).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        // The dialog hasn't been accepted yet — this is what Cancel and Esc rely on.
        Assert.False(prefs.Flag);
        Assert.Equal(10, prefs.Size);
        Assert.Equal(Mode.First, prefs.Choice);

        form.Commit();

        Assert.True(prefs.Flag);
        Assert.Equal(25, prefs.Size);
        Assert.Equal(Mode.Second, prefs.Choice);
    }

    [AvaloniaFact]
    public void A_number_out_of_range_is_clamped_rather_than_written_through()
    {
        var prefs = new Prefs();
        var form = FormOver(prefs);

        // The spinner itself clamps typed input, but a caller can still set Value directly; the field is
        // the last line of defence, because a setting is often a control property with its own valid range.
        Editor<NumericUpDown>(form).Value = 5000m;
        form.Commit();

        Assert.Equal(100, prefs.Size);
    }

    [AvaloniaFact]
    public void A_cleared_number_leaves_the_setting_alone()
    {
        var prefs = new Prefs { Size = 33 };
        var form = FormOver(prefs);

        Editor<NumericUpDown>(form).Value = null; // an emptied box is "no number entered", not "zero"
        form.Commit();

        Assert.Equal(33, prefs.Size);
    }

    [AvaloniaFact]
    public void A_checkbox_labels_itself_and_a_valued_field_gets_a_label_column()
    {
        var form = FormOver(new Prefs());

        Assert.Equal("Flag", Editor<CheckBox>(form).Content);
        Assert.Contains(form.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Size");

        // Section titles render as headers, so a long form reads as groups rather than a wall of rows.
        Assert.Contains(form.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Numbers");
    }
}
