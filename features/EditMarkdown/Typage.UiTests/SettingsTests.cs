using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Puffinote.Components.Theming;
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
                "Auto-save", "Recovery snapshots", "Completed tasks",
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
        harness.Numbers[4].Value = 0m;    // auto-save off
        harness.Numbers[5].Value = 60m;   // recovery snapshots, every minute
        harness.Toolbar.IsChecked = false;

        Assert.Equal(AppTheme.System, settings.Theme); // nothing written until the dialog is accepted
        harness.Form.Commit();

        Assert.Equal(AppTheme.HighContrast, settings.Theme);
        Assert.Equal(8, settings.TabWidth);
        Assert.Equal(0, settings.EditorMaxContentWidth);
        Assert.Equal(0, settings.AutoSaveSeconds);
        Assert.Equal(60, settings.RecoverySnapshotSeconds);
        Assert.False(settings.ShowToolbar);
    }

    /// <summary>
    /// Unset, the appearance follows the OS — an app nobody has told which appearance to wear should wear
    /// the desktop's. Choosing one puts the whole shell in that variant, the family's chrome tokens
    /// included, so the palette and the modal dialogs match the window they open over. High contrast is
    /// Typage's own, and rides the dark variant: what makes it high contrast is the editor palette.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(AppTheme.Light, "Light")]
    [InlineData(AppTheme.Dark, "Dark")]
    [InlineData(AppTheme.HighContrast, "Dark")]
    public void The_chosen_appearance_puts_the_shell_and_the_family_chrome_in_one_variant(AppTheme theme, string expected)
    {
        Assert.Equal(AppTheme.System, new AppSettings().Theme); // the default: whatever the desktop is wearing

        var settings = TestShell.NewSettings();
        settings.Settings.Theme = theme;

        var window = TestShell.NewWindow(settings);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var variant = expected == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        Assert.Equal(variant, Application.Current!.ActualThemeVariant);
        Assert.Equal(variant, Tokens.Variant);
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
