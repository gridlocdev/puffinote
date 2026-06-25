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
    /// <summary>The default dark palette (the bare init values below).</summary>
    public static MarkdownTheme Dark { get; } = new();

    public static MarkdownTheme Default => Dark;

    /// <summary>A light palette for light-mode / high-brightness environments (PRD §7.2).</summary>
    public static MarkdownTheme Light { get; } = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28)),
        MarkerForeground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8E, 0x96)),
        LinkForeground = new SolidColorBrush(Color.FromRgb(0x09, 0x69, 0xDA)),
        CodeForeground = new SolidColorBrush(Color.FromRgb(0xCF, 0x22, 0x2E)),
        CodeBackground = new SolidColorBrush(Color.FromArgb(0x28, 0xAF, 0xB8, 0xC1)),
        HighlightBackground = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0x9A)),
        Caret = new SolidColorBrush(Color.FromRgb(0x09, 0x69, 0xDA)),
        SelectionBackground = new SolidColorBrush(Color.FromArgb(0x40, 0x09, 0x69, 0xDA)),
        SearchMatch = new SolidColorBrush(Color.FromArgb(0x66, 0xF5, 0xC2, 0x11)),
        SearchMatchCurrent = new SolidColorBrush(Color.FromArgb(0xAA, 0xF0, 0xA8, 0x00)),
        Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xFD, 0xFD)),
        PropertiesBackground = new SolidColorBrush(Color.FromArgb(0x0E, 0x00, 0x00, 0x00)),
        BulletForeground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x7B)),
        QuoteBar = new SolidColorBrush(Color.FromRgb(0xCB, 0xD2, 0xDA)),
        RuleForeground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD7, 0xDE)),
        CheckboxForeground = new SolidColorBrush(Color.FromRgb(0x09, 0x69, 0xDA)),
        CodeBlockBackground = new SolidColorBrush(Color.FromArgb(0x14, 0xAF, 0xB8, 0xC1)),
    };

    /// <summary>A high-contrast palette: pure black/white with vivid accents (PRD §7.2, §9.6).</summary>
    public static MarkdownTheme HighContrast { get; } = new()
    {
        Foreground = new SolidColorBrush(Colors.White),
        MarkerForeground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0x00)),
        LinkForeground = new SolidColorBrush(Color.FromRgb(0x58, 0xC0, 0xFF)),
        CodeForeground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9D, 0x5C)),
        CodeBackground = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
        HighlightBackground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xD4, 0x00)),
        Caret = new SolidColorBrush(Colors.White),
        SelectionBackground = new SolidColorBrush(Color.FromArgb(0x88, 0x1E, 0x90, 0xFF)),
        SearchMatch = new SolidColorBrush(Color.FromArgb(0x77, 0xFF, 0xD4, 0x00)),
        SearchMatchCurrent = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xD4, 0x00)),
        Background = new SolidColorBrush(Colors.Black),
        PropertiesBackground = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        BulletForeground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0x00)),
        QuoteBar = new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0x00)),
        RuleForeground = new SolidColorBrush(Colors.White),
        CheckboxForeground = new SolidColorBrush(Color.FromRgb(0x58, 0xC0, 0xFF)),
        CodeBlockBackground = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
    };

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

    public IBrush SearchMatch { get; init; } = new SolidColorBrush(Color.FromArgb(0x55, 0xC2, 0x8A, 0x12));

    public IBrush SearchMatchCurrent { get; init; } = new SolidColorBrush(Color.FromArgb(0x99, 0xE3, 0xA8, 0x2B));

    public IBrush Background { get; init; } = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

    /// <summary>The panel tint behind the leading front-matter "Properties" block (PRD §7.4).</summary>
    public IBrush PropertiesBackground { get; init; } = new SolidColorBrush(Color.FromArgb(0x16, 0x6E, 0x76, 0x81));

    // ----- live-preview decoration treatments (PRD §7.4): the synthetic UI that replaces hidden markers -----

    /// <summary>The bullet glyph drawn in place of a concealed unordered-list marker.</summary>
    public IBrush BulletForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0x9E));

    /// <summary>The vertical bar drawn in the gutter of a concealed blockquote.</summary>
    public IBrush QuoteBar { get; init; } = new SolidColorBrush(Color.FromRgb(0x4A, 0x52, 0x5C));

    /// <summary>The rule drawn in place of a concealed horizontal-rule line.</summary>
    public IBrush RuleForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0x40, 0x47, 0x50));

    /// <summary>The box (and check) of a drawn task checkbox.</summary>
    public IBrush CheckboxForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0x57, 0x9D, 0xF6));

    /// <summary>The panel tint behind a fenced-code block.</summary>
    public IBrush CodeBlockBackground { get; init; } = new SolidColorBrush(Color.FromArgb(0x1F, 0x6E, 0x76, 0x81));

    /// <summary>Thickness of the drawn horizontal rule.</summary>
    public double RuleThickness { get; init; } = 1.5;

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
            TokenType.Strikethrough => new RunStyle(Body(), BaseFontSize, Foreground, null, false, Strike(Foreground)),
            TokenType.Highlight => new RunStyle(Body(), BaseFontSize, Foreground, HighlightBackground, false),
            // Inline code gets a tight "pill" background; code-block text does not — the continuous
            // block panel behind the whole fence/indent provides its background instead (no per-line box).
            TokenType.InlineCode => new RunStyle(Mono(), BaseFontSize, CodeForeground, CodeBackground, false),
            TokenType.CodeText => new RunStyle(Mono(), BaseFontSize, CodeForeground, null, false),
            TokenType.LinkText or TokenType.Autolink or TokenType.WikiLink => new RunStyle(Body(), BaseFontSize, LinkForeground, null, false, LinkUnderline),
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

    /// <summary>
    /// A strikethrough decoration with an <b>explicit</b> stroke pen in the text colour. The stroke is given
    /// rather than left null because relying on a decoration to inherit the run's foreground is unreliable
    /// across draw backends — an explicit pen guarantees the line actually renders through the text.
    /// </summary>
    private TextDecorationCollection? _linkUnderline;

    /// <summary>
    /// The underline decoration for link text — a solid rule in <see cref="LinkForeground"/>, so links read
    /// like HTML hyperlinks (coloured <i>and</i> underlined). Built once per theme instance (themes are
    /// immutable) and shared, so the FormattedText cache keys on a stable decoration object.
    /// </summary>
    public TextDecorationCollection LinkUnderline => _linkUnderline ??= new()
    {
        new TextDecoration
        {
            Location = TextDecorationLocation.Underline,
            Stroke = LinkForeground,
            StrokeThickness = 1,
            StrokeThicknessUnit = TextDecorationUnit.Pixel,
        },
    };

    private static TextDecorationCollection Strike(IBrush foreground) =>
        new()
        {
            new TextDecoration
            {
                Location = TextDecorationLocation.Strikethrough,
                Stroke = foreground,
                StrokeThickness = 1,
                StrokeThicknessUnit = TextDecorationUnit.Pixel,
            },
        };
}
