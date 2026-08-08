using System.Globalization;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Cellestra.Core.Editing;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Services;
using Cellestra.Desktop.Views;
using Puffinote.Components.Shell;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Document auto-save: the open workbook is written back once the editing stops. PRD §9 had listed
/// autosave as a v1 non-goal; this is that decision reversed, on the same atomic temp-then-rename write
/// the manual save uses, so the "never corrupt an .ods mid-write" guarantee is untouched.
/// <para>
/// Every test drives <c>AutoSaveNowForTest</c> rather than waiting — a <c>DispatcherTimer</c> doesn't tick
/// in a headless suite that isn't pumping real time.
/// </para>
/// </summary>
public class AutoSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cellestra-autosave-{Guid.NewGuid():N}");

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

    private MainWindow NewWindow(int? autoSaveSeconds = null)
    {
        var path = Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.json");
        var service = new SettingsService(path);
        if (autoSaveSeconds is { } seconds)
        {
            service.Save(new AppSettings { AutoSaveSeconds = seconds });
        }

        return new MainWindow(service);
    }

    /// <summary>A window over a workbook already saved to disk — auto-save's only subject.</summary>
    private (MainWindow Window, string Path) OpenSavedWorkbook(int? autoSaveSeconds = null)
    {
        var window = NewWindow(autoSaveSeconds);
        window.Show();

        var path = Path.Combine(_dir, $"book-{Guid.NewGuid():N}.ods");
        File.WriteAllBytes(path, window.Editor.Session.SerializeToBytes());
        window.Editor.Session.MarkSaved(path);
        Dispatcher.UIThread.RunJobs();
        return (window, path);
    }

    private static void TypeInto(MainWindow window, int row, int column, string text)
    {
        IAppEnvironment env = window.Editor;
        env.Session.Execute(EditCommands.SetInput(
            env.Session.ActiveSheet.Name, row, column, text, CultureInfo.InvariantCulture));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task An_edited_workbook_is_written_back_without_anyone_pressing_Save()
    {
        var (window, path) = OpenSavedWorkbook();
        var before = new FileInfo(path).Length;

        TypeInto(window, 0, 0, "auto-saved");
        Assert.True(window.Editor.Session.IsModified);

        await window.AutoSaveNowForTest();

        Assert.False(window.Editor.Session.IsModified);

        // Re-read it to prove the cell actually reached disk, not just that the dirty flag cleared.
        using var stream = File.OpenRead(path);
        var reloaded = WorkbookSession.LoadAndRecalc(stream).Workbook;
        Assert.Equal("auto-saved", reloaded.Worksheets[0].GetValue(0, 0).ToString());
        Assert.True(before > 0);
    }

    [AvaloniaFact]
    public async Task Editing_arms_the_countdown_and_saving_disarms_it()
    {
        var (window, _) = OpenSavedWorkbook();
        Assert.False(window.AutoSavePendingForTest); // a freshly-saved workbook has nothing pending

        TypeInto(window, 1, 0, "x");
        Assert.True(window.AutoSavePendingForTest);

        await window.AutoSaveNowForTest();
        Assert.False(window.AutoSavePendingForTest); // saved: nothing left to write
    }

    [AvaloniaFact]
    public async Task A_workbook_that_was_never_saved_is_left_alone_rather_than_asked_where_to_go()
    {
        // There is nowhere to write it, and Save As is a question an auto-save must never ask.
        var window = NewWindow();
        window.Show();
        TypeInto(window, 0, 0, "untitled work");

        await window.AutoSaveNowForTest();

        Assert.True(window.Editor.Session.IsModified);
        Assert.Null(window.Editor.Session.FilePath);
    }

    [AvaloniaFact]
    public async Task Turning_the_interval_off_stops_it_on_the_open_workbook()
    {
        var (window, path) = OpenSavedWorkbook(autoSaveSeconds: 0);
        var before = File.ReadAllBytes(path);

        TypeInto(window, 0, 0, "should not reach disk");
        Assert.False(window.AutoSavePendingForTest);

        await window.AutoSaveNowForTest();
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.True(window.Editor.Session.IsModified);
    }

    [Fact]
    public void The_interval_defaults_to_the_family_default_and_survives_a_round_trip()
    {
        var path = Path.Combine(_dir, "settings.json");
        var service = new SettingsService(path);
        Assert.Equal(AutoSave.DefaultSeconds, service.Load().AutoSaveSeconds);

        service.Save(new AppSettings { AutoSaveSeconds = 120 });
        Assert.Equal(120, new SettingsService(path).Load().AutoSaveSeconds);
    }
}
