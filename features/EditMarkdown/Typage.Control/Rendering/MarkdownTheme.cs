using Avalonia.Media;
using Typage.Engine.Markdown;

namespace Typage.Control.Rendering;

/// <summary>The resolved visual treatment for one token: typeface, size, colors, and whether it is concealable syntax.</summary>
public readonly record struct RunStyle(
    Typeface Typeface, double FontSize, IBrush Foreground, IBrush? Background,
    bool IsMarker, TextDecorationCollection? Decorations = null);

/// <summary>
/// Maps semantic token types to visual treatments (PRD §10.1 — the presentation-side seam that
/// lets the notes app re-theme without touching the engine). The engine stays styling-free; all
/// fonts and colors live here. This is the default dark theme (tuned to sit alongside the
/// Cellestra-style chrome); light and high-contrast variants would parameterize these init
/// properties (PRD §7.2).
/// </summary>
public class MarkdownTheme
{
    public static readonly MarkdownTheme Default = new();

    public double BaseFontSize { get; init; } = 15;

    public double LineHeight { get; init; } = 30;

    /// <summary>Left inset of the text column from the editor edge (PRD §7.5 — breathing room).</summary>
    public double PaddingLeft { get; init; } = 20;

    /// <summary>Top inset of the first line from the editor edge.</summary>
    public double PaddingTop { get; init; } = 14;

    // Dark palette, tuned to sit alongside the Cellestra chrome.
    public IBrush Foreground { get; init; } = new SolidColorBrush(Color.FromRgb(0xD4, 0xD7, 0xDC));

    public IBrush MarkerForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x81));

    public IBrush LinkForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0x57, 0x9D, 0xF6));

    public IBrush CodeForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x78));

    public IBrush CodeBackground { get; init; } = new SolidColorBrush(Color.FromArgb(0x33, 0x6E, 0x76, 0x81));

    public IBrush HighlightBackground { get; init; } = new SolidColorBrush(Color.FromArgb(0x55, 0xD2, 0xA8, 0x06));

    public IBrush Caret { get; init; } = new SolidColorBrush(Color.FromRgb(0x57, 0x9D, 0xF6));

    public IBrush SelectionBackground { get; init; } = new SolidColorBrush(Color.FromArgb(0x55, 0x2F, 0x68, 0xC4));

    public IBrush Background { get; init; } = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

    private readonly FontFamily _body = FontFamily.Default;
    private readonly FontFamily _mono = new("Cascadia Code,Consolas,Menlo,monospace");

    /// <summary>The visual treatment for <paramref name="type"/>. <paramref name="headingLevel"/> sizes headings.</summary>
    public RunStyle Resolve(TokenType type, int headingLevel = 0)
    {
        return type switch
        {
            TokenType.HeadingMarker => Marker(),
            TokenType.HeadingText => new RunStyle(Bold(), HeadingSize(headingLevel), Foreground, null, false),
            TokenType.Strong => new RunStyle(Bold(), BaseFontSize, Foreground, null, false),
            TokenType.Emphasis => new RunStyle(Italic(), BaseFontSize, Foreground, null, false),
            TokenType.StrongEmphasis => new RunStyle(BoldItalic(), BaseFontSize, Foreground, null, false),
            TokenType.Strikethrough => new RunStyle(Body(), BaseFontSize, Foreground, null, false, Strike()),
            TokenType.Highlight => new RunStyle(Body(), BaseFontSize, Foreground, HighlightBackground, false),
            TokenType.InlineCode or TokenType.CodeText => new RunStyle(Mono(), BaseFontSize, CodeForeground, CodeBackground, false),
            TokenType.LinkText or TokenType.Autolink or TokenType.WikiLink => new RunStyle(Body(), BaseFontSize, LinkForeground, null, false),
            TokenType.LinkUrl or TokenType.ImageUrl or TokenType.Embed => Marker(),
            TokenType.BlockquoteMarker or TokenType.ListMarker or TokenType.TaskMarker
                or TokenType.HorizontalRule or TokenType.CodeFence or TokenType.FrontMatterFence
                or TokenType.TableDelimiter or TokenType.Marker or TokenType.Escape => Marker(),
            TokenType.FrontMatterText => new RunStyle(Mono(), BaseFontSize - 1, MarkerForeground, null, false),
            _ => new RunStyle(Body(), BaseFontSize, Foreground, null, false),
        };
    }

    /// <summary>The size of a line that begins with a heading marker of <paramref name="level"/> (1–6).</summary>
    public double HeadingSize(int level) => level switch
    {
        1 => BaseFontSize + 9,
        2 => BaseFontSize + 6,
        3 => BaseFontSize + 4,
        4 => BaseFontSize + 2,
        5 => BaseFontSize + 1,
        _ => BaseFontSize,
    };

    private RunStyle Marker() => new(Body(), BaseFontSize, MarkerForeground, null, true);

    private Typeface Body() => new(_body);
    private Typeface Bold() => new(_body, FontStyle.Normal, FontWeight.Bold);
    private Typeface Italic() => new(_body, FontStyle.Italic);
    private Typeface BoldItalic() => new(_body, FontStyle.Italic, FontWeight.Bold);
    private Typeface Mono() => new(_mono);

    private static TextDecorationCollection Strike() =>
        new() { new TextDecoration { Location = TextDecorationLocation.Strikethrough } };
}
