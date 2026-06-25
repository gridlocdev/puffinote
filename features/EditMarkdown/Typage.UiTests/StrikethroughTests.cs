using System.Linq;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Desktop.Services;
using Typage.Desktop.Views;
using Typage.Engine.Document;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// Strikethrough: ⌘⇧X (Ctrl+Shift+X) toggles it, and the text between the concealed ~~ markers renders
/// with a strike-through decoration.
/// </summary>
public class StrikethroughTests
{
    [AvaloniaFact]
    public void Shortcut_IsBoundAtTheWindow()
    {
        var window = new MainWindow(new SettingsService());
        Assert.Contains(window.KeyBindings, b =>
            b.Gesture is { } g && g.Key == Key.X && g.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift));
    }

    [AvaloniaFact]
    public void StruckText_RendersWithAStrikeDecoration()
    {
        var e = new MarkdownEditor { Text = "say ~~done~~ now" };
        var run = e.BuildVisualLineForTest(0).Runs
            .First(r => r.Kind == VisualRunKind.Text && r.Text.Contains("done"));
        Assert.NotNull(run.Style.Decorations); // the content carries the strikethrough decoration
    }

    [AvaloniaFact]
    public void TheMarkersAreConcealed_LeavingJustTheWord()
    {
        // The ~~ markers collapse to zero width — the visible text runs don't contain a tilde.
        var e = new MarkdownEditor { Text = "~~done~~" };
        var visibleText = string.Concat(e.BuildVisualLineForTest(0).Runs
            .Where(r => r.Kind == VisualRunKind.Text).Select(r => r.Text));
        Assert.DoesNotContain("~", visibleText);
        Assert.Contains("done", visibleText);
    }
}
