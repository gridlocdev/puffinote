using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Typage.Control.Controls;
using Typage.Desktop.Services;
using Typage.Desktop.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Preferences (PRD §7.9): the command affordance, and the form over what Typage keeps in its settings
/// file. Every option in <see cref="AppSettings"/> is reachable from here except the recent-files list,
/// which is a record of what you did rather than a preference — File ▸ Open Recent is its surface.
/// </summary>
public class SettingsTests
{
    private static SettingsEditorViewHarness Harness(AppSettings settings) => new(settings);

    private sealed class SettingsEditorViewHarness
    {
        internal SettingsEditorViewHarness(AppSettings settings)
        {
            Form = MainWindow.BuildSettingsForm(settings);
            var window = new Window { Width = 500, Height = 500, Content = Form };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        internal Puffinote.Components.Controls.SettingsEditorView Form { get; }

        internal List<ComboBox> Combos => [.. Form.GetLogicalDescendants().OfType<ComboBox>()];

        internal List<NumericUpDown> Numbers => [.. Form.GetLogicalDescendants().OfType<NumericUpDown>()];

        internal CheckBox Toolbar => Form.GetLogicalDescendants().OfType<CheckBox>().Single();
    }

    [AvaloniaFact]
    public void Preferences_is_bound_on_the_platform_chord()
    {
        var window = TestShell.NewWindow();
        Assert.Contains(
            window.KeyBindings,
            b => b.Gesture is { } g && g.Key == Key.OemComma && g.KeyModifiers == TestShell.Command);
    }

    [AvaloniaFact]
    public void The_form_covers_every_preference_in_the_settings_file()
    {
        var form = MainWindow.BuildSettingsForm(new AppSettings());

        Assert.Equal(
            [
                "Theme", "Default view", "Show the formatting toolbar",
                "Tab width", "Horizontal padding", "Vertical padding", "Reading width",
                "Autosave", "Completed tasks",
            ],
            form.Fields.Select(f => f.Label));
    }

    [AvaloniaFact]
    public void Every_editor_opens_seeded_from_the_current_settings()
    {
        var settings = new AppSettings
        {
            Theme = AppTheme.Light,
            View = MarkdownEditor.ViewMode.RawText,
            ShowToolbar = false,
            TabWidth = 2,
            EditorMaxContentWidth = 900,
        };
        var harness = Harness(settings);

        Assert.Equal((int)AppTheme.Light, harness.Combos[0].SelectedIndex);
        Assert.Equal((int)MarkdownEditor.ViewMode.RawText, harness.Combos[1].SelectedIndex);
        Assert.False(harness.Toolbar.IsChecked);
        Assert.Equal(2m, harness.Numbers[0].Value);
        Assert.Equal(900m, harness.Numbers[3].Value);
    }

    [AvaloniaFact]
    public void Committing_the_form_writes_the_edited_values_back()
    {
        var settings = new AppSettings();
        var harness = Harness(settings);

        harness.Combos[0].SelectedIndex = (int)AppTheme.HighContrast;
        harness.Numbers[0].Value = 8m;    // tab width
        harness.Numbers[3].Value = 0m;    // reading width: 0 = full width
        harness.Numbers[4].Value = 0m;    // autosave off
        harness.Toolbar.IsChecked = false;

        Assert.Equal(AppTheme.Dark, settings.Theme); // nothing written until the dialog is accepted
        harness.Form.Commit();

        Assert.Equal(AppTheme.HighContrast, settings.Theme);
        Assert.Equal(8, settings.TabWidth);
        Assert.Equal(0, settings.EditorMaxContentWidth);
        Assert.Equal(0, settings.AutosaveSeconds);
        Assert.False(settings.ShowToolbar);
    }

    [AvaloniaFact]
    public void The_reading_width_is_editable_at_all()
    {
        // It was the one AppSettings field the dialog read and applied but never offered — a setting you
        // could only change by editing the JSON by hand.
        var settings = new AppSettings();
        var harness = Harness(settings);

        harness.Numbers[3].Value = 1200m;
        harness.Form.Commit();

        Assert.Equal(1200, settings.EditorMaxContentWidth);
    }
}
