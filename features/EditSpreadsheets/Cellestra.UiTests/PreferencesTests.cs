using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cellestra.Desktop.Views;
using Cellestra.Editor;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Theming;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Preferences: the catalog command, and the dialog over the options the host keeps in Cellestra's
/// settings file. The recent-files list and the remembered window size are deliberately absent — they're
/// session state, and the window size in particular is rewritten from the real window on close, so a
/// value typed into a form could only ever be discarded.
/// </summary>
public class PreferencesTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cellestra-prefs-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    /// <summary>A window on a scratch settings file — accepting the dialog really does persist, and it
    /// must not be the developer's own profile it persists into.</summary>
    private MainWindow ScratchWindow() => new(new Cellestra.Desktop.Services.SettingsService(_path));

    [Fact]
    public void Preferences_is_a_catalog_command_on_the_platform_chord()
    {
        // Metadata only — build the catalog against a no-op environment (no MainWindow needed).
        var command = FakeAppEnvironment.Catalog.ById("app.preferences");
        Assert.NotNull(command);
        Assert.Equal("Preferences…", command!.Title);

        // ⌘, — the platform's Preferences chord. Cellestra is macOS-only, so assert the macOS rendering
        // explicitly (the test host is Linux).
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Meta), command.Shortcut!.Value.ToGesture(mac: true));
        Assert.Equal("⌘,", ShortcutFormatter.Format(command.Shortcut!.Value, mac: true));
    }

    /// <summary>
    /// ⌘K's list is where someone looks for a command they haven't memorised, so Preferences has to be
    /// <em>in</em> it — and to carry its chord, which is how the palette teaches ⌘, rather than only
    /// answering to it. It is filed under "App": as the one <c>app.</c> command it used to fall through
    /// to the "Other" bin at the end of the list.
    /// </summary>
    [AvaloniaFact]
    public void Preferences_is_offered_by_the_command_menu_with_its_chord()
    {
        var window = ScratchWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.Editor.ShowCommandPalette(); // what ⌘K (view.commandPalette) runs
        Dispatcher.UIThread.RunJobs();

        var palette = window.Editor.OverlayHost.GetVisualDescendants().OfType<PaletteListView>().Single();
        var entry = Assert.Single(palette.ListedEntries, e => e.Title == "Preferences…");

        // Cellestra is macOS-only, so the palette renders ⌘ regardless of the host running the tests.
        Assert.Equal("⌘,", entry.Shortcut);
        Assert.Contains("App", palette.ListedTitles);
    }

    [AvaloniaFact]
    public void Accepting_the_dialog_applies_the_edits_and_asks_the_host_to_persist()
    {
        var window = ScratchWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var persisted = 0;
        window.Editor.PreferencesChanged += (_, _) => persisted++;

        window.Editor.ShowPreferences();
        Dispatcher.UIThread.RunJobs();

        var overlay = window.Editor.OverlayHost;
        overlay.GetLogicalDescendants().OfType<NumericUpDown>().First().Value = 150m; // zoom, as a percent
        // Two drop-downs now: the host's appearance row on top, then the grid's own command-bar row.
        overlay.GetLogicalDescendants().OfType<ComboBox>().ElementAt(1).SelectedIndex = (int)CommandBarVisibility.Hidden;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1.0, window.Editor.Zoom); // nothing applied while the dialog is still open

        // The dialog's default button is OK; clicking it resolves the awaited FormDialog.
        overlay.GetLogicalDescendants().OfType<Button>().First(b => b.IsDefault).RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1.5, window.Editor.Zoom, precision: 3);
        Assert.Equal(CommandBarVisibility.Hidden, window.Editor.CommandBarVisibility);
        Assert.Equal(1, persisted);
    }

    /// <summary>
    /// Appearance belongs to the app around the grid, so the standalone window contributes it through the
    /// editor's host seam rather than the grid carrying a theme row wherever it is embedded. Accepting the
    /// dialog applies the choice — the app really is in the light variant afterwards — and persists it, so
    /// the next launch opens the way this one was left.
    /// </summary>
    [AvaloniaFact]
    public void The_theme_is_offered_by_the_window_applied_at_once_and_remembered()
    {
        var window = ScratchWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.Editor.ShowPreferences();
        Dispatcher.UIThread.RunJobs();

        var overlay = window.Editor.OverlayHost;
        var theme = overlay.GetLogicalDescendants().OfType<ComboBox>().First();
        Assert.Equal(ThemePreferences.Labels, theme.ItemsSource); // the host's section, at the top
        theme.SelectedIndex = (int)ThemePreference.Light;
        Dispatcher.UIThread.RunJobs();

        overlay.GetLogicalDescendants().OfType<Button>().First(b => b.IsDefault).RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(ThemeVariant.Light, Application.Current!.ActualThemeVariant);
        Assert.Equal(ThemeVariant.Light, Tokens.Variant); // and the family's chrome tokens went with it
        Assert.Equal(ThemePreference.Light, new Cellestra.Desktop.Services.SettingsService(_path).Load().Theme);
    }

    /// <summary>
    /// The seam is opt-in: an embedding shell that contributes nothing — as Puffinote does, settling the
    /// theme once for itself — gets the grid's own options and no appearance row at all.
    /// </summary>
    [AvaloniaFact]
    public void An_embedding_host_that_contributes_nothing_gets_no_appearance_row()
    {
        var editor = new SpreadsheetEditorView();
        var window = new Window { Width = 900, Height = 600, Content = editor };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        editor.ShowPreferences();
        Dispatcher.UIThread.RunJobs();

        // The grid does carry one drop-down of its own — the command bar's three states — so the check is
        // that it is the *only* one: no appearance row arrived, because no host contributed it.
        var combos = editor.OverlayHost.GetLogicalDescendants().OfType<ComboBox>().ToList();
        var only = Assert.Single(combos);
        Assert.Equal(["Visible", "Collapsed", "Hidden"], only.ItemsSource!.Cast<string>());
    }

    [AvaloniaFact]
    public void Cancelling_the_dialog_leaves_every_setting_alone()
    {
        var window = ScratchWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var persisted = 0;
        window.Editor.PreferencesChanged += (_, _) => persisted++;

        window.Editor.ShowPreferences();
        Dispatcher.UIThread.RunJobs();

        var overlay = window.Editor.OverlayHost;
        var zoomBefore = window.Editor.Zoom;
        overlay.GetLogicalDescendants().OfType<NumericUpDown>().First().Value = 300m;
        overlay.TryCancel(); // Esc / backdrop
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(zoomBefore, window.Editor.Zoom);
        Assert.Equal(0, persisted);
    }
}
