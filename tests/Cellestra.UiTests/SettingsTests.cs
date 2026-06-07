using System.IO;
using System.Linq;
using Cellestra.Desktop.Services;
using Xunit;

namespace Cellestra.UiTests;

public class SettingsTests
{
    [Fact]
    public void Settings_round_trip_through_disk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cellestra-settings-{System.Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(path);
            var settings = new AppSettings { RibbonVisible = true, Zoom = 1.5, WindowWidth = 800, WindowHeight = 600 }
                .WithRecentFile("/a.ods");

            service.Save(settings);
            var loaded = service.Load();

            Assert.True(loaded.RibbonVisible);
            Assert.Equal(1.5, loaded.Zoom);
            Assert.Equal(800, loaded.WindowWidth);
            Assert.Equal("/a.ods", loaded.RecentFiles.Single());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Recent_files_dedupe_promote_and_cap()
    {
        var settings = new AppSettings();
        for (int i = 0; i < AppSettings.MaxRecentFiles + 5; i++)
        {
            settings = settings.WithRecentFile($"/file{i}.ods");
        }

        Assert.Equal(AppSettings.MaxRecentFiles, settings.RecentFiles.Count);
        Assert.Equal($"/file{AppSettings.MaxRecentFiles + 4}.ods", settings.RecentFiles[0]); // most recent first

        settings = settings.WithRecentFile(settings.RecentFiles[3]); // re-open an existing one
        Assert.Equal(AppSettings.MaxRecentFiles, settings.RecentFiles.Count); // no duplicate added
    }

    [Fact]
    public void Missing_settings_file_loads_defaults()
    {
        var service = new SettingsService(Path.Combine(Path.GetTempPath(), "cellestra-nonexistent.json"));
        var settings = service.Load();

        Assert.Equal(1.0, settings.Zoom);
        Assert.Empty(settings.RecentFiles);
    }
}
