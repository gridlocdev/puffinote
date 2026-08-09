using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Control.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The optional tint on the line being edited: off unless asked for, settable as the default a document
/// opens with, and flippable for the session from the command menu or the View menu.
/// </summary>
public class CurrentLineHighlightTests
{
    [AvaloniaFact]
    public void It_is_off_until_someone_asks_for_it()
    {
        // The surface is a page of prose; a band under someone's writing is not something to draw uninvited.
        Assert.False(new MarkdownEditor().HighlightCurrentLine);
        Assert.False(new MarkdownEditorView().HighlightCurrentLine);
        Assert.False(new Typage.Desktop.Services.AppSettings().HighlightCurrentLine);
    }

    /// <summary>
    /// It is a property of the surface, not of a view mode, so it is in force in all three presentations —
    /// rich, formatted, and raw source — rather than only in the one it happened to be switched on in.
    /// </summary>
    [AvaloniaFact]
    public void It_survives_every_view_mode()
    {
        var view = new MarkdownEditorView { HighlightCurrentLine = true };
        foreach (var mode in Enum.GetValues<MarkdownEditor.ViewMode>())
        {
            view.ViewMode = mode;
            Assert.True(view.HighlightCurrentLine);
            Assert.True(view.Editor.HighlightCurrentLine);
        }
    }

    /// <summary>
    /// Every palette answers for the tint, and answers faintly: it is drawn under every glyph on the line,
    /// and it must stay clearly quieter than the selection, which means something you did rather than
    /// somewhere you are.
    /// </summary>
    [AvaloniaFact]
    public void Every_palette_tints_more_faintly_than_it_selects()
    {
        foreach (var theme in new[] { MarkdownTheme.Dark, MarkdownTheme.Light, MarkdownTheme.HighContrast })
        {
            var line = Assert.IsType<Avalonia.Media.SolidColorBrush>(theme.CurrentLineBackground);
            var selection = Assert.IsType<Avalonia.Media.SolidColorBrush>(theme.SelectionBackground);
            Assert.True(line.Color.A > 0, "a tint nobody can see is a feature that didn't ship");
            Assert.True(line.Color.A < selection.Color.A);
        }
    }

    [AvaloniaFact]
    public void The_command_toggles_it_for_the_session_and_lights_when_it_is_on()
    {
        var view = new MarkdownEditorView();
        var command = view.EditingCommands.Single(c => c.Id == "view.highlightCurrentLine");

        Assert.False(command.IsActive?.Invoke());
        command.Execute();
        Assert.True(view.HighlightCurrentLine);
        Assert.True(command.IsActive?.Invoke());

        command.Execute();
        Assert.False(view.HighlightCurrentLine);
    }

    [AvaloniaFact]
    public void The_window_opens_a_document_with_whatever_the_setting_says()
    {
        var settings = TestShell.NewSettings();
        settings.Settings.HighlightCurrentLine = true;

        var window = TestShell.NewWindow(settings);
        Assert.True(window.EditorView.HighlightCurrentLine);
    }

    /// <summary>
    /// The session toggle changes the document in front of you and nothing else — Preferences is where the
    /// answer for the next document is set, so a flip must not quietly rewrite it.
    /// </summary>
    [AvaloniaFact]
    public void Toggling_it_for_the_session_does_not_rewrite_the_default()
    {
        var settings = TestShell.NewSettings();
        var window = TestShell.NewWindow(settings);

        window.EditorView.HighlightCurrentLine = true;

        Assert.True(window.EditorView.HighlightCurrentLine);
        Assert.False(settings.Settings.HighlightCurrentLine);
    }
}
