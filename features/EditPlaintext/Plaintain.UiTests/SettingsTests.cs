using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Plaintain.Desktop.Services;
using Plaintain.Desktop.Views;
using Puffinote.Components.Theming;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// Preferences: the menu affordance, and the form over what Plaintain actually keeps in its settings file.
/// </summary>
public class SettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"plaintain-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    private MainWindow Open()
    {
        var window = new MainWindow(new SettingsService(_path));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void The_File_menu_offers_Preferences_on_the_platform_chord()
    {
        var window = Open();
        var file = window.GetLogicalDescendants().OfType<MenuItem>().First(m => (string?)m.Header == "_File");
        var preferences = file.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "_Preferences…");

        Assert.Equal(Key.OemComma, preferences.InputGesture!.Key);
        Assert.Contains(window.KeyBindings, b => b.Gesture.Key == Key.OemComma);
    }

    [AvaloniaFact]
    public void The_form_covers_every_preference_in_the_settings_file()
    {
        var settings = new AppSettings { WordWrap = true, ShowLineNumbers = false, FontSize = 18 };
        var form = MainWindow.BuildSettingsForm(settings);

        Assert.Equal(
            ["Theme", "Wrap long lines", "Show line numbers", "Font size"],
            form.Fields.Select(f => f.Label));
    }

    /// <summary>
    /// The appearance is a preference like any other: unset it follows the OS, and choosing one is written
    /// to the file so the next launch opens in it. The drop-down's options are the family's three, in the
    /// order the shared enum declares them — which is the order an index maps back to a value in.
    /// </summary>
    [AvaloniaFact]
    public void The_theme_can_be_chosen_and_is_remembered()
    {
        var settings = new AppSettings();
        Assert.Equal(ThemePreference.System, settings.Theme); // nobody has said otherwise yet

        var form = MainWindow.BuildSettingsForm(settings);
        var window = new Window { Width = 400, Height = 300, Content = form };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var theme = form.GetLogicalDescendants().OfType<ComboBox>().First();
        Assert.Equal(ThemePreferences.Labels, theme.ItemsSource);
        theme.SelectedIndex = (int)ThemePreference.Light;

        Assert.Equal(ThemePreference.System, settings.Theme); // nothing written until the dialog is accepted
        form.Commit();
        Assert.Equal(ThemePreference.Light, settings.Theme);

        var service = new SettingsService(_path);
        service.Settings.Theme = ThemePreference.Light;
        service.Save();
        Assert.Equal(ThemePreference.Light, new SettingsService(_path).Settings.Theme); // i.e. the next launch
    }

    [AvaloniaFact]
    public void Committing_the_form_writes_the_edited_values_back()
    {
        var settings = new AppSettings();
        var form = MainWindow.BuildSettingsForm(settings);
        var window = new Window { Width = 400, Height = 300, Content = form };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var boxes = form.GetLogicalDescendants().OfType<CheckBox>().ToList();
        boxes[0].IsChecked = true;   // wrap
        boxes[1].IsChecked = false;  // line numbers
        form.GetLogicalDescendants().OfType<NumericUpDown>().Single().Value = 20m;

        Assert.False(settings.WordWrap); // nothing written until the dialog is accepted
        form.Commit();

        Assert.True(settings.WordWrap);
        Assert.False(settings.ShowLineNumbers);
        Assert.Equal(20, settings.FontSize);
    }

    [AvaloniaFact]
    public void Preferences_that_survive_a_restart_come_back_on_the_editor()
    {
        var service = new SettingsService(_path);
        service.Settings.WordWrap = true;
        service.Settings.FontSize = 22;
        service.Save();

        var window = Open(); // a fresh window over the same file — i.e. the next launch
        Assert.True(window.EditorView.Editor.WordWrap);
        Assert.Equal(22, window.EditorView.Editor.FontSize);
    }
}
