using Avalonia;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Hyperlink rendering and activation in the grid (PRD §4 Data: Hyperlinks): a linked cell draws
/// through the real Skia path without throwing, the hover hint and the URI-safety guard behave, and
/// the guard accepts only well-formed http/https/mailto/file URIs.
/// </summary>
public class GridHyperlinkTests
{
    private static (MainWindow Window, Worksheet Sheet, SpreadsheetGrid Grid) Open()
    {
        var window = new MainWindow();
        window.Show();
        return (window, window.Editor.Session.ActiveSheet, window.Editor.Grid);
    }

    [AvaloniaFact]
    public void A_hyperlinked_cell_renders_without_throwing()
    {
        var (window, sheet, grid) = Open();

        // A link with visible text, and an empty cell that still carries a link (target stands in).
        sheet.SetValue(0, 0, CellValue.Text("Anthropic"));
        sheet.GetOrCreate(0, 0).Hyperlink = new Hyperlink("https://www.anthropic.com");
        sheet.GetOrCreate(1, 0).Hyperlink = new Hyperlink("mailto:hi@example.com");

        grid.Measure(new Size(800, 600));
        grid.Arrange(new Rect(0, 0, 800, 600));
        window.AssertRenders();
    }

    [AvaloniaFact]
    public void Hover_hint_appears_only_on_a_hyperlinked_cell()
    {
        var (_, sheet, grid) = Open();
        sheet.SetValue(0, 0, CellValue.Text("docs"));
        sheet.GetOrCreate(0, 0).Hyperlink = new Hyperlink("https://example.com/docs");

        Assert.Equal("⌘-click to open https://example.com/docs", grid.HyperlinkTipAt(0, 0));
        Assert.Null(grid.HyperlinkTipAt(1, 1)); // a plain cell has no hint
    }

    [AvaloniaFact]
    public void Hover_hint_for_a_link_inside_a_merge_reports_via_the_anchor()
    {
        var (_, sheet, grid) = Open();
        sheet.AddMerge(new MergedRegion(CellRange.Parse("B2:C3")));
        sheet.GetOrCreate(1, 1).Hyperlink = new Hyperlink("https://example.com");

        // Hovering a covered (non-anchor) cell still surfaces the anchor's link hint.
        Assert.Equal("⌘-click to open https://example.com", grid.HyperlinkTipAt(2, 2));
    }

    [AvaloniaTheory]
    [InlineData("https://www.anthropic.com")]
    [InlineData("http://example.com/path?q=1#frag")]
    [InlineData("HTTPS://EXAMPLE.COM")] // scheme match is case-insensitive
    [InlineData("mailto:someone@example.com")]
    [InlineData("file:///Users/me/report.ods")]
    public void Guard_accepts_well_formed_safe_uris(string target)
    {
        Assert.True(SpreadsheetGrid.TryGetLaunchableUri(target, out var uri));
        Assert.NotNull(uri);
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a uri")]
    [InlineData("/relative/path")]          // relative — rejected
    [InlineData("example.com")]             // no scheme
    [InlineData("javascript:alert(1)")]     // disallowed scheme
    [InlineData("ftp://example.com/file")]  // disallowed scheme
    public void Guard_rejects_unsafe_or_malformed_targets(string? target)
    {
        Assert.False(SpreadsheetGrid.TryGetLaunchableUri(target, out _));
    }
}
