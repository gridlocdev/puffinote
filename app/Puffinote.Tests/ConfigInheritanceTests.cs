using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

/// <summary>
/// Config precedence (PRD §7.5): sub-app defaults &lt; the sub-app's own config file &lt; Puffinote overrides,
/// with the per-notebook override winning over the per-user one.
/// </summary>
public sealed class ConfigInheritanceTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("puffinote-config");

    public void Dispose() => _dir.Delete(recursive: true);

    private string At(string relative) => System.IO.Path.Combine(_dir.FullName, relative);

    // ---- NoteConfig: inherit Typage's own file, then layer Puffinote overrides -------------------

    [Fact]
    public void NoteConfig_load_reads_typages_own_settings_file()
    {
        var path = At("typage.json");
        // Shaped like Typage's AppSettings — extra fields Puffinote doesn't apply are ignored.
        File.WriteAllText(path, """
            { "TabWidth": 2, "EditorPadding": 40, "EditorPaddingVertical": 8, "View": "Source", "ShowToolbar": false }
            """);

        var cfg = NoteConfig.Load(path);

        Assert.Equal(2, cfg.TabWidth);
        Assert.Equal(40, cfg.EditorPadding);
        Assert.Equal(8, cfg.EditorPaddingVertical);
    }

    [Fact]
    public void NoteConfig_load_falls_back_to_typage_defaults_when_the_file_is_absent()
    {
        var cfg = NoteConfig.Load(At("does-not-exist.json"));

        Assert.Equal(4, cfg.TabWidth); // standalone Typage's defaults
        Assert.Equal(20, cfg.EditorPadding);
        Assert.Equal(14, cfg.EditorPaddingVertical);
    }

    [Fact]
    public void A_puffinote_override_wins_over_the_inherited_tab_width_but_a_null_override_inherits_it()
    {
        var path = At("typage.json");
        File.WriteAllText(path, """{ "TabWidth": 2 }""");

        // Override set → Puffinote wins; the inherited padding is untouched.
        var overridden = NoteConfig.Resolve(new PuffinoteSettings { TabWidth = 8 }, path);
        Assert.Equal(8, overridden.TabWidth);
        Assert.Equal(20, overridden.EditorPadding);

        // Override null → the sub-app's own value is inherited.
        Assert.Equal(2, NoteConfig.Resolve(new PuffinoteSettings(), path).TabWidth);
    }

    // ---- PuffinoteSettings: per-user vs per-notebook layering --------------------------------------

    [Fact]
    public void Layer_lets_the_over_value_win_but_keeps_the_base_where_the_over_is_unset()
    {
        var user = new PuffinoteSettings { TabWidth = 4 };

        Assert.Equal(8, user.Layer(new PuffinoteSettings { TabWidth = 8 }).TabWidth);
        Assert.Equal(4, user.Layer(new PuffinoteSettings()).TabWidth); // null over → keep the base
    }

    [Fact]
    public void Layering_a_bool_distinguishes_a_deliberate_false_from_an_unset_one()
    {
        // The trap in a nullable bool: "hide the tab bar" is `false`, which is falsy but *set*, so it has to
        // win over a user who shows it rather than being swallowed as "said nothing".
        var user = new PuffinoteSettings { ShowTabBar = true };

        Assert.False(user.Layer(new PuffinoteSettings { ShowTabBar = false }).ShowTabBar);
        Assert.True(user.Layer(new PuffinoteSettings()).ShowTabBar); // unset over → keep the base
        Assert.Null(new PuffinoteSettings().Layer(new PuffinoteSettings()).ShowTabBar); // neither set
    }

    [Fact]
    public void A_notebook_can_hide_the_tab_bar_for_a_user_who_shows_it()
    {
        var stateDir = Directory.CreateDirectory(At(NotebookNode.StateDirectoryName));
        File.WriteAllText(System.IO.Path.Combine(stateDir.FullName, "settings.json"), """{ "ShowTabBar": false }""");

        var resolved = PuffinoteSettings.Resolve(new PuffinoteSettings { ShowTabBar = true }, _dir.FullName);

        Assert.False(resolved.ShowTabBar); // the committed, travels-with-the-repo choice wins
    }

    [Fact]
    public void Resolve_layers_the_per_notebook_settings_file_over_the_user_settings()
    {
        var stateDir = Directory.CreateDirectory(At(NotebookNode.StateDirectoryName));
        File.WriteAllText(System.IO.Path.Combine(stateDir.FullName, "settings.json"), """{ "TabWidth": 8 }""");

        var resolved = PuffinoteSettings.Resolve(new PuffinoteSettings { TabWidth = 4 }, _dir.FullName);

        Assert.Equal(8, resolved.TabWidth); // the notebook's committed override wins
    }

    [Fact]
    public void Resolve_returns_the_user_settings_when_the_notebook_has_no_settings_file()
    {
        var user = new PuffinoteSettings { TabWidth = 3 };

        Assert.Equal(3, PuffinoteSettings.Resolve(user, _dir.FullName).TabWidth); // no .puffinote/settings.json
        Assert.Equal(3, PuffinoteSettings.Resolve(user, null).TabWidth);
    }
}
