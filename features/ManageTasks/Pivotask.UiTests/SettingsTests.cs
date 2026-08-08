using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Pivotask.Control.Views;
using Pivotask.Core.Query;
using Pivotask.Desktop.Services;
using Pivotask.Desktop.Views;
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
        Assert.Equal(["Open in", "Show"], form.Fields.Select(f => f.Label));
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
        combos[0].SelectedIndex = 1; // List
        combos[1].SelectedIndex = 3; // Unfinished

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
