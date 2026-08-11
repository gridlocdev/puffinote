using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Desktop.Services;
using Pivotask.Desktop.Views;
using Puffinote.Components.Shell;
using Xunit;

namespace Pivotask.UiTests;

/// <summary>
/// Document auto-save: the open board is written back once the editing stops.
/// <para>
/// Every test drives <c>AutoSaveNowForTest</c> rather than waiting — a <c>DispatcherTimer</c> doesn't tick
/// in a headless suite that isn't pumping real time, and a test that waited out thirty seconds for its
/// assertion is a test nobody runs.
/// </para>
/// </summary>
public class AutoSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pivotask-autosave-{Guid.NewGuid():N}");

    public AutoSaveTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }

        GC.SuppressFinalize(this);
    }

    private MainWindow Open(int? autoSaveSeconds = null)
    {
        var settings = new SettingsService(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.json"));
        if (autoSaveSeconds is { } seconds)
        {
            settings.Settings.AutoSaveSeconds = seconds;
        }

        return new MainWindow(settings);
    }

    /// <summary>A saved board file on disk, and the window with it open.</summary>
    private (MainWindow Window, string Path) OpenSavedBoard(int? autoSaveSeconds = null)
    {
        var path = Path.Combine(_dir, $"board-{Guid.NewGuid():N}.pivotask");
        new BoardDocument().Save(path);

        var window = Open(autoSaveSeconds);
        window.Show();
        window.OpenPath(path);
        Dispatcher.UIThread.RunJobs();
        return (window, path);
    }

    /// <summary>Make a real, undoable edit — the same seam the UI drives.</summary>
    private static void AddGroup(MainWindow window, string name)
    {
        window.BoardView.Document.Execute(BoardCommands.AddGroup(name));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void An_edited_board_is_written_back_without_anyone_pressing_Save()
    {
        var (window, path) = OpenSavedBoard();

        AddGroup(window, "Groceries");
        Assert.True(window.BoardView.Document.IsDirty);

        window.AutoSaveNowForTest().GetAwaiter().GetResult();

        Assert.False(window.BoardView.Document.IsDirty);
        Assert.Contains("Groceries", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Editing_arms_the_countdown_and_saving_disarms_it()
    {
        var (window, _) = OpenSavedBoard();
        Assert.False(window.AutoSavePendingForTest); // opening a board is not an edit

        AddGroup(window, "Later");
        Assert.True(window.AutoSavePendingForTest);

        window.AutoSaveNowForTest().GetAwaiter().GetResult();
        Assert.False(window.AutoSavePendingForTest); // saved: nothing left to write
    }

    [AvaloniaFact]
    public void A_board_that_was_never_saved_is_left_alone_rather_than_asked_where_to_go()
    {
        // There is nowhere to write it, and Save As is a question an auto-save must never ask.
        var window = Open();
        window.Show();
        AddGroup(window, "Untitled work");

        window.AutoSaveNowForTest().GetAwaiter().GetResult();

        Assert.True(window.BoardView.Document.IsDirty); // still unsaved, and no dialog opened
        Assert.Null(window.BoardView.Document.Path);
    }

    [AvaloniaFact]
    public void Turning_the_interval_off_stops_it_on_the_open_board()
    {
        var (window, path) = OpenSavedBoard(autoSaveSeconds: 0);
        var before = File.ReadAllText(path);

        AddGroup(window, "Should not reach disk");
        Assert.False(window.AutoSavePendingForTest);

        window.AutoSaveNowForTest().GetAwaiter().GetResult();
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void The_interval_defaults_to_the_family_default_and_survives_a_round_trip()
    {
        var path = Path.Combine(_dir, "settings.json");
        Assert.Equal(AutoSave.DefaultSeconds, new SettingsService(path).Settings.AutoSaveSeconds);

        var service = new SettingsService(path);
        service.Settings.AutoSaveSeconds = 120;
        service.Save();

        Assert.Equal(120, new SettingsService(path).Settings.AutoSaveSeconds);
    }
}
