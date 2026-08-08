using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Services;
using Puffinote.Components.Controls.CommandBar;
using Xunit;

namespace Cellestra.UiTests;

public class SettingsTests
{
    [Fact]
    public void The_command_bar_state_is_written_by_name_and_read_back()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cellestra-settings-{System.Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(path);
            service.Save(new AppSettings { CommandBar = CommandBarVisibility.Hidden });

            // A settings file is meant to be readable and hand-editable — names, not ordinals.
            Assert.Contains("\"Hidden\"", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Equal(CommandBarVisibility.Hidden, service.Load().CommandBar);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_collapsed_bar_from_the_two_state_build_survives_the_upgrade()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cellestra-settings-{System.Guid.NewGuid():N}.json");
        try
        {
            // What the pre-three-state build wrote. The bar must not spring back open on first launch.
            File.WriteAllText(path, "{ \"CommandBarCollapsed\": true, \"Zoom\": 1.5 }");
            var loaded = new SettingsService(path).Load();

            Assert.Equal(CommandBarVisibility.Collapsed, loaded.CommandBar);
            Assert.Equal(1.5, loaded.Zoom);

            // An expanded bar in that build is the new default, and the new key always wins over the old one.
            File.WriteAllText(path, "{ \"CommandBarCollapsed\": false }");
            Assert.Equal(CommandBarVisibility.Visible, new SettingsService(path).Load().CommandBar);

            File.WriteAllText(path, "{ \"CommandBar\": \"Hidden\", \"CommandBarCollapsed\": true }");
            Assert.Equal(CommandBarVisibility.Hidden, new SettingsService(path).Load().CommandBar);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Radial_menu_config_round_trips_through_disk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cellestra-settings-{System.Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(path);
            var radial = new RadialMenuConfig
            {
                SegmentCount = 4,
                CommandIds = ["edit.cut", null, "edit.paste", "edit.undo"],
            };
            service.Save(new AppSettings { RadialMenu = radial });

            var loaded = service.Load().RadialMenu;
            Assert.Equal(4, loaded.SegmentCount);
            Assert.Equal("edit.cut", loaded.CommandIdAt(0));
            Assert.Null(loaded.CommandIdAt(1));
            Assert.Equal("edit.undo", loaded.CommandIdAt(3));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Settings_without_a_radial_block_fall_back_to_the_default()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cellestra-settings-{System.Guid.NewGuid():N}.json");
        try
        {
            // An older settings file (written before the radial feature) has no RadialMenu key.
            File.WriteAllText(path, "{ \"Zoom\": 1.0 }");
            var loaded = new SettingsService(path).Load();
            Assert.Equal(RadialMenuConfig.DefaultSegmentCount, loaded.RadialMenu.SegmentCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Settings_round_trip_through_disk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cellestra-settings-{System.Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(path);
            var settings = new AppSettings { CommandBar = CommandBarVisibility.Collapsed, Zoom = 1.5, WindowWidth = 800, WindowHeight = 600 }
                .WithRecentFile("/a.ods");

            service.Save(settings);
            var loaded = service.Load();

            Assert.Equal(CommandBarVisibility.Collapsed, loaded.CommandBar);
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
