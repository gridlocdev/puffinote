using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Pivotask.Control.Views;
using Pivotask.Core.Query;
using Pivotask.Desktop.Services;
using Pivotask.Desktop.Views;
using Puffinote.Components.Theming;
using Xunit;

namespace Pivotask.UiTests;

/// <summary>
/// Preferences: the menu affordance, the form over Pivotask's settings file, and the settings actually
/// reaching the board on the next launch.
/// </summary>
public class SettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pivotask-settings-{Guid.NewGuid():N}.json");

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
        var form = MainWindow.BuildSettingsForm(new AppSettings());
        Assert.Equal(["Theme", "Open in", "Show"], form.Fields.Select(f => f.Label));
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
        theme.SelectedIndex = (int)ThemePreference.Dark;

        Assert.Equal(ThemePreference.System, settings.Theme); // nothing written until the dialog is accepted
        form.Commit();
        Assert.Equal(ThemePreference.Dark, settings.Theme);

        var service = new SettingsService(_path);
        service.Settings.Theme = ThemePreference.Dark;
        service.Save();
        Assert.Equal(ThemePreference.Dark, new SettingsService(_path).Settings.Theme); // i.e. the next launch
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

        var combos = form.GetLogicalDescendants().OfType<ComboBox>().ToList();
        combos[1].SelectedIndex = 1; // List      (combos[0] is Appearance ▸ Theme)
        combos[2].SelectedIndex = 3; // Unfinished

        Assert.Equal(BoardLayout.Board, settings.Layout); // nothing written until the dialog is accepted
        form.Commit();

        Assert.Equal(BoardLayout.List, settings.Layout);
        Assert.Equal(TaskFilter.Unfinished, settings.Filter);
    }

    [AvaloniaFact]
    public void Preferences_that_survive_a_restart_come_back_on_the_board()
    {
        var service = new SettingsService(_path);
        service.Settings.Layout = BoardLayout.List;
        service.Settings.Filter = TaskFilter.Starred;
        service.Save();

        var window = Open(); // a fresh window over the same file — i.e. the next launch
        Assert.Equal(BoardLayout.List, window.BoardView.Layout);
        Assert.Equal(TaskFilter.Starred, window.BoardView.Filter);
    }

    [AvaloniaFact]
    public void A_settings_file_that_isnt_there_is_a_first_run_not_an_error()
    {
        var window = Open(); // _path was never written
        Assert.Equal(BoardLayout.Board, window.BoardView.Layout);
        Assert.Equal(TaskFilter.All, window.BoardView.Filter);
    }
}
