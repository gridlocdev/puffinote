using Avalonia.Media;
using Typage.Engine.Markdown;

namespace Typage.Control.Rendering;

/// <summary>
/// The resolved visual treatment for one token: typeface, size, colors, and whether it is concealable syntax.
///
/// <para><paramref name="BackgroundPad"/> is the horizontal padding a <paramref name="Background"/> chip extends
/// past its text on each side — an inline-code chip needs room to breathe around the code. The layout
/// <i>reserves</i> it where the construct's markers concealed (a zero-glyph spacer run), and the draw path bleeds
/// the chip by exactly that much, so the two agree: without the reservation the chip is drawn into its
/// neighbours' space and paints over the tail of the word before it.</para>
/// </summary>
public readonly record struct RunStyle(
    Typeface Typeface, double FontSize, IBrush Foreground, IBrush? Background,
    bool IsMarker, TextDecorationCollection? Decorations = null, double BackgroundPad = 0);

/// <summary>
/// What a laid-out block <i>is</i>, for vertical rhythm. The engine emits tokens, not blocks, so the layout
/// classifies each block from its first line's leading token; the theme then answers the block's metrics
/// (<see cref="BlockMetrics"/>) from this. Purely presentational — the engine knows nothing about it.
/// </summary>
public enum MarkdownBlockKind
{
    Paragraph,
    Heading,
    ListItem,
    Blockquote,
    Code,
    Rule,
    Table,
    FrontMatter,
}

/// <summary>
/// One block's vertical metrics, in device-independent pixels.
///
/// <para><paramref name="RowHeight"/> is the height of each wrapped row inside the block (a heading's rows are
/// taller than a paragraph's). <paramref name="SpaceBefore"/>/<paramref name="SpaceAfter"/> are margins
/// <i>outside</i> the block — the layout collapses them against the neighbour's (see
/// <see cref="MarkdownTheme.GapBetween"/>). <paramref name="PadTop"/>/<paramref name="PadBottom"/> are padding
/// <i>inside</i> it, included in its height and covered by its background — this is what gives a fenced-code
/// panel or a blockquote its inner breathing room, since each source line of those is its own block and only
/// the first/last of a run carries the padding.</para>
/// </summary>
public readonly record struct BlockMetrics(
    double RowHeight, double SpaceBefore, double SpaceAfter, double PadTop = 0, double PadBottom = 0);

/// <summary>
/// Maps semantic token types to visual treatments (PRD §10.1 — the presentation-side seam that
/// lets the notes app re-theme without touching the engine). The engine stays styling-free; all
/// fonts and colors live here. This is the default dark theme (tuned to sit alongside the
/// Cellestra-style chrome); light and high-contrast variants would parameterize these init
/// properties (PRD §7.2).
///
/// <para>A <b>record</b>, so a caller can say <c>theme with { BaseFontSize = … }</c> and get every one of
/// the forty-odd brushes carried across. Zoom needs exactly that (<see cref="Scaled"/>): it must not mutate
/// <see cref="Dark"/>/<see cref="Light"/>/<see cref="HighContrast"/>, which are shared singletons — in
/// Puffinote two open notes would zoom each other. A hand-written copy constructor would do the same job
/// and silently drop whichever colour a future edit forgets to add to it.</para>
/// </summary>
public record MarkdownTheme
{
    /// <summary>The default dark palette (the bare init values below).</summary>
    public static MarkdownTheme Dark { get; } = new();

    public static MarkdownTheme Default => Dark;

    /// <summary>A light palette for light-mode / high-brightness environments (PRD §7.2).</summary>
    public static MarkdownTheme Light { get; } = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x2F)),
        HeadingForeground = new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17)),
        HeadingForegroundH3 = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22)),
        HeadingForegroundH4 = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28)),
        HeadingForegroundMuted = new SolidColorBrush(Color.FromRgb(0x57, 0x60, 0x6A)),
        HeadingForegroundFaint = new SolidColorBrush(Color.FromRgb(0x6E, 0x77, 0x81)),
        MarkerForeground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8E, 0x96)),
        LinkForeground = new SolidColorBrush(Color.FromRgb(0x09, 0x69, 0xDA)),
        CodeForeground = new SolidColorBrush(Color.FromRgb(0x95, 0x38, 0x00)),
        CodeBlockForeground = new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x2F)),
        CodeBackground = new SolidColorBrush(Color.FromRgb(0xEF, 0xF1, 0xF3)),
        HighlightBackground = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0x9A)),
        Caret = new SolidColorBrush(Color.FromRgb(0x09, 0x69, 0xDA)),
        SelectionBackground = new SolidColorBrush(Color.FromArgb(0x40, 0x09, 0x69, 0xDA)),
        SearchMatch = new SolidColorBrush(Color.FromArgb(0x66, 0xF5, 0xC2, 0x11)),
        SearchMatchCurrent = new SolidColorBrush(Color.FromArgb(0xAA, 0xF0, 0xA8, 0x00)),
        Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xFD, 0xFD)),
        CurrentLineBackground = new SolidColorBrush(Color.FromArgb(0x0F, 0x00, 0x00, 0x00)),
        PropertiesBackground = new SolidColorBrush(Color.FromArgb(0x0E, 0x00, 0x00, 0x00)),
        BulletForeground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x7B)),
        QuoteBar = new SolidColorBrush(Color.FromRgb(0xD0, 0xD7, 0xDE)),
        QuoteBackground = new SolidColorBrush(Color.FromRgb(0xF6, 0xF8, 0xFA)),
        QuoteForeground = new SolidColorBrush(Color.FromRgb(0x57, 0x60, 0x6A)),
        RuleForeground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD7, 0xDE)),
        TableHeaderBorder = new SolidColorBrush(Color.FromRgb(0xAF, 0xB8, 0xC1)),
        TableRowBorder = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xEF)),
        TableHeaderForeground = new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17)),
        CodeBlockBackground = new SolidColorBrush(Color.FromRgb(0xF6, 0xF8, 0xFA)),
        CodeBlockBorder = new SolidColorBrush(Color.FromRgb(0xE1, 0xE4, 0xE8)),

        // The same one accent, re-mixed for a white page: the blue drops in lightness and gains chroma, because a
        // hue that reads as vivid on a dark surface reads as washed out on a light one. The check stays white —
        // it is read against the accent, not against the page.
        TaskBorder = new SolidColorBrush(Color.FromRgb(0xB1, 0xB8, 0xC2)),
        TaskAccent = new SolidColorBrush(Color.FromRgb(0x21, 0x6B, 0xE0)),
        TaskCheck = new SolidColorBrush(Colors.White),
        TaskDoneForeground = new SolidColorBrush(Color.FromRgb(0x6E, 0x77, 0x81)),
    };

    /// <summary>
    /// A high-contrast palette: pure black/white with vivid accents (PRD §7.2, §9.6). Note the headings do
    /// <i>not</i> desaturate down the scale the way the other palettes do — dimming H5/H6 is exactly what
    /// high-contrast users need us not to do — so here the type scale rests on size and weight alone.
    /// </summary>
    public static MarkdownTheme HighContrast { get; } = new()
    {
        Foreground = new SolidColorBrush(Colors.White),
        HeadingForeground = new SolidColorBrush(Colors.White),
        HeadingForegroundH3 = new SolidColorBrush(Colors.White),
        HeadingForegroundH4 = new SolidColorBrush(Colors.White),
        HeadingForegroundMuted = new SolidColorBrush(Colors.White),
        HeadingForegroundFaint = new SolidColorBrush(Colors.White),
        MarkerForeground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0x00)),
        LinkForeground = new SolidColorBrush(Color.FromRgb(0x58, 0xC0, 0xFF)),
        CodeForeground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9D, 0x5C)),
        CodeBlockForeground = new SolidColorBrush(Colors.White),
        CodeBackground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
        HighlightBackground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xD4, 0x00)),
        Caret = new SolidColorBrush(Colors.White),
        SelectionBackground = new SolidColorBrush(Color.FromArgb(0x88, 0x1E, 0x90, 0xFF)),
        SearchMatch = new SolidColorBrush(Color.FromArgb(0x77, 0xFF, 0xD4, 0x00)),
        SearchMatchCurrent = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xD4, 0x00)),
        Background = new SolidColorBrush(Colors.Black),

        // Stronger than the other two palettes': a tint a high-contrast reader cannot see is a feature they
        // switched on and did not get. Still well below the selection, which has to stay the louder of the two.
        CurrentLineBackground = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
        PropertiesBackground = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        BulletForeground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0x00)),
        QuoteBar = new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0x00)),
        QuoteBackground = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
        QuoteForeground = new SolidColorBrush(Colors.White),
        RuleForeground = new SolidColorBrush(Colors.White),
        TableHeaderBorder = new SolidColorBrush(Colors.White),
        TableRowBorder = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
        TableHeaderForeground = new SolidColorBrush(Colors.White),
        CodeBlockBackground = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)),
        CodeBlockBorder = new SolidColorBrush(Colors.White),

        // The accent at full strength on black, and the resting box in plain *white*, not grey: dimming the state a
        // reader is trying to see is exactly what high contrast must not do, so an empty box is spelled by the
        // absence of the accent, never by a faded outline. Done text likewise stays white — the strike through it
        // says "closed" on its own. The check flips to black, because here the accent is a *light* blue: a white
        // check on it would be the one unreadable thing on the page.
        TaskBorder = new SolidColorBrush(Colors.White),
        TaskAccent = new SolidColorBrush(Color.FromRgb(0x58, 0xC0, 0xFF)),
        TaskCheck = new SolidColorBrush(Colors.Black),
        TaskDoneForeground = new SolidColorBrush(Colors.White),
    };

    /// <summary>
    /// This theme at <paramref name="zoom"/> — the body size and the row height scaled together, which is
    /// all it takes: every other metric on this type is expressed as a multiple of one or the other, so the
    /// heading scale, the bullet gutter, the checkbox and the block margins all follow (the "rem" behaviour
    /// a stylesheet gets for free). Colours, paddings and the reading-column inset are untouched — zoom
    /// magnifies the text, it doesn't re-theme the page.
    /// </summary>
    public MarkdownTheme Scaled(double zoom) => zoom is 1.0
        ? this
        : this with { BaseFontSize = BaseFontSize * zoom, LineHeight = LineHeight * zoom };

    public double BaseFontSize { get; init; } = 16;

    /// <summary>The height of one body row — the baseline of the vertical rhythm (16/26). Other block kinds take
    /// their own row height from <see cref="MetricsFor"/>; this is the default and what the scroll bar steps by.</summary>
    public double LineHeight { get; init; } = 26;

    /// <summary>The row height of a body/paragraph line — an alias of <see cref="LineHeight"/>, named for the metrics table.</summary>
    public double BodyRowHeight => LineHeight;

    /// <summary>
    /// The size of monospace runs (inline code and code blocks). Deliberately below <see cref="BaseFontSize"/>:
    /// a monospace face has a larger apparent x-height, so code set at the body size reads oversized next to prose.
    /// </summary>
    public double CodeFontSize => BaseFontSize * 0.875;

    /// <summary>The row height inside a code block — tighter than prose, since code is scanned, not read in lines.</summary>
    public double CodeRowHeight => 21 * Scale;

    /// <summary>Left inset of the text column from the editor edge (PRD §7.5 — breathing room).</summary>
    public double PaddingLeft { get; init; } = 20;

    /// <summary>Top inset of the first line from the editor edge.</summary>
    public double PaddingTop { get; init; } = 14;

    // Dark palette, tuned to sit alongside the Cellestra chrome.
    public IBrush Foreground { get; init; } = new SolidColorBrush(Color.FromRgb(0xC9, 0xCC, 0xCF));

    // ----- heading foregrounds: the type scale desaturates as it descends, so that below H4 — where the sizes
    // converge — colour and weight carry the hierarchy that size no longer can.

    /// <summary>H1/H2 — the brightest text on the surface.</summary>
    public IBrush HeadingForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xED));

    public IBrush HeadingForegroundH3 { get; init; } = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4));

    public IBrush HeadingForegroundH4 { get; init; } = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));

    /// <summary>H5 — steps off the prose ramp onto a cooler grey.</summary>
    public IBrush HeadingForegroundMuted { get; init; } = new SolidColorBrush(Color.FromRgb(0xB8, 0xBC, 0xC0));

    /// <summary>H6 — faint and small; the last rung, distinguished from body text by size, weight, and colour.</summary>
    public IBrush HeadingForegroundFaint { get; init; } = new SolidColorBrush(Color.FromRgb(0x8B, 0x90, 0x96));

    public IBrush MarkerForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x81));

    /// <summary>Link text. Links are the one place a hue distinct from the app accent is defensible — "clickable
    /// text" is a different affordance from "selected item" — so this stays blue against the shell's orange chrome.</summary>
    public IBrush LinkForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0x5B, 0x9D, 0xD9));

    /// <summary>Inline code — a warm amber that reads as code on a dark surface, against its own chip background.</summary>
    public IBrush CodeForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0xD7, 0xBA, 0x7D));

    /// <summary>Code-block text. Neutral rather than amber: inside the panel it is the content, not an inline accent.</summary>
    public IBrush CodeBlockForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));

    /// <summary>The chip behind an inline-code run.</summary>
    public IBrush CodeBackground { get; init; } = new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x34));

    public IBrush HighlightBackground { get; init; } = new SolidColorBrush(Color.FromArgb(0x55, 0xD2, 0xA8, 0x06));

    public IBrush Caret { get; init; } = new SolidColorBrush(Color.FromRgb(0x57, 0x9D, 0xF6));

    public IBrush SelectionBackground { get; init; } = new SolidColorBrush(Color.FromArgb(0x55, 0x2F, 0x68, 0xC4));

    public IBrush SearchMatch { get; init; } = new SolidColorBrush(Color.FromArgb(0x55, 0xC2, 0x8A, 0x12));

    public IBrush SearchMatchCurrent { get; init; } = new SolidColorBrush(Color.FromArgb(0x99, 0xE3, 0xA8, 0x2B));

    public IBrush Background { get; init; } = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

    /// <summary>
    /// The wash across the row the caret is on, when that highlight is switched on (it is off by default).
    /// Deliberately the faintest tint in the palette: it is drawn under every glyph on the line, so anything
    /// stronger would change how the text itself reads — and it has to stay clearly weaker than
    /// <see cref="SelectionBackground"/>, which means something you did rather than somewhere you are.
    /// </summary>
    public IBrush CurrentLineBackground { get; init; } = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));

    /// <summary>The panel tint behind the leading front-matter "Properties" block (PRD §7.4).</summary>
    public IBrush PropertiesBackground { get; init; } = new SolidColorBrush(Color.FromArgb(0x16, 0x6E, 0x76, 0x81));

    // ----- live-preview decoration treatments (PRD §7.4): the synthetic UI that replaces hidden markers -----

    /// <summary>The bullet glyph (and computed ordinal) drawn in place of a concealed list marker. Muted, because
    /// a marker is structure, not content — it should feel attached to its item, not compete with it.</summary>
    public IBrush BulletForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0x8B, 0x90, 0x96));

    /// <summary>The vertical bar drawn in the gutter of a concealed blockquote.</summary>
    public IBrush QuoteBar { get; init; } = new SolidColorBrush(Color.FromRgb(0x4A, 0x4F, 0x55));

    /// <summary>The panel tint behind a blockquote — one step off the editor background, so nested quotes stack legibly.</summary>
    public IBrush QuoteBackground { get; init; } = new SolidColorBrush(Color.FromRgb(0x24, 0x27, 0x2B));

    /// <summary>Blockquote body text — dimmer than prose, so a quote reads as secondary.</summary>
    public IBrush QuoteForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0xB4, 0xB8, 0xBC));

    /// <summary>The rule drawn in place of a concealed horizontal-rule line.</summary>
    public IBrush RuleForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0x34, 0x38, 0x3D));

    /// <summary>The hairline under a table's header row — the strongest line in the table.</summary>
    public IBrush TableHeaderBorder { get; init; } = new SolidColorBrush(Color.FromRgb(0x4A, 0x4F, 0x55));

    /// <summary>The separator between table body rows — kept very faint, so the data reads as rows, not a spreadsheet.</summary>
    public IBrush TableRowBorder { get; init; } = new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x34));

    /// <summary>A table header cell's text.</summary>
    public IBrush TableHeaderForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xED));

    /// <summary>The border around a fenced-code panel.</summary>
    public IBrush CodeBlockBorder { get; init; } = new SolidColorBrush(Color.FromRgb(0x34, 0x38, 0x3D));

    // ----- the task checkbox (PRD §8.3.2): one accent, single-hue -----
    //
    // Three states need three *shapes*, not three hues. The box is drawn once — a rounded square — and the state
    // is spelled by what is in it: nothing, a dash, a check. Only one colour is ever spent: the accent, shared by
    // the dash and the done box's fill. A resting box is a bare outline in the quiet border colour, so a column of
    // tasks reads as one calm gutter with the finished and the moving items picked out of it, rather than as five
    // competing hues.
    //
    // Colour is never the only channel: the dash and the check tell the states apart on their own, so they survive
    // colour-blindness and a monochrome print (PRD §9.6). That was true of six hues too — it is simply cheaper to
    // hold with one.

    /// <summary>The outline of a box that isn't done — <c>[ ]</c> and <c>[/]</c> alike. Quiet: a task not yet
    /// finished is the resting state of a list, and the gutter shouldn't shout it.</summary>
    public IBrush TaskBorder { get; init; } = new SolidColorBrush(Color.FromRgb(0x5A, 0x62, 0x72));

    /// <summary>The one hue the checkbox spends: the <c>[/]</c> dash, and the fill of a <c>[x]</c> box. Sharing it
    /// is what makes "under way" and "done" read as two points on one path rather than two unrelated signals.</summary>
    public IBrush TaskAccent { get; init; } = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));

    /// <summary>The check drawn <i>on</i> the filled done box — so it must contrast with
    /// <see cref="TaskAccent"/>, not with the page.</summary>
    public IBrush TaskCheck { get; init; } = new SolidColorBrush(Colors.White);

    /// <summary>The text of a done item — dimmed, because it is no longer work to do.</summary>
    public IBrush TaskDoneForeground { get; init; } = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xB5));

    /// <summary>The panel behind a fenced-code block — <i>darker</i> than the editor background, so the block recedes.</summary>
    public IBrush CodeBlockBackground { get; init; } = new SolidColorBrush(Color.FromRgb(0x18, 0x1A, 0x1D));

    // ----- inline-code / highlight chips -----

    /// <summary>
    /// The padding inside an inline-code chip, on each side of the code. Sized so the chip reads as a chip: a
    /// backtick's worth of space is not enough, and with none reserved in the layout the chip is drawn over the
    /// tail of the word before it. <see cref="ChipHeightRatio"/> is the vertical counterpart.
    /// </summary>
    public double InlineCodePadding { get; init; } = 5;

    /// <summary>The padding inside a <c>==highlight==</c> band. Tighter than a code chip — a highlight is a mark
    /// on prose, so it should hug the words it marks rather than stand off them.</summary>
    public double HighlightPadding { get; init; } = 2;

    /// <summary>The height of a chip/band as a multiple of its run's font size (capped to the row it sits in, so a
    /// code span on a heading line can't outgrow the heading's row).</summary>
    public double ChipHeightRatio { get; init; } = 1.6;

    // ----- list bullet: the gutter width and the drawn glyph read the same numbers, so they stay in sync -----

    /// <summary>The diameter of the drawn bullet.</summary>
    public double BulletSize => Math.Max(4, BaseFontSize * 0.28);

    /// <summary>The inset before the bullet — the space between the item's indent and the dot.</summary>
    public double BulletLeftPad => BaseFontSize * 0.25;

    /// <summary>The space between the bullet and the item's text.</summary>
    public double BulletGap => BaseFontSize * 0.5;

    /// <summary>
    /// The width a concealed bullet marker reserves — inset + dot + gap, and the hanging indent an item's wrapped
    /// rows sit at. Deliberately independent of the source <c>"- "</c>/<c>"* "</c> punctuation's measured width,
    /// which is a couple of pixels wider than the dot itself and left it crowded against its own text.
    /// </summary>
    public double BulletGutter => BulletLeftPad + BulletSize + BulletGap;

    // ----- the task checkbox: geometry, colour, and the treatment of a done item's text -----

    /// <summary>The side of the drawn task box — a shade under the cap height of the text beside it, so the box
    /// sits <i>in</i> the line rather than standing over it.</summary>
    public double CheckboxSize => BaseFontSize * 0.95;

    /// <summary>The corner radius of the box, as a fraction of its side: rounded enough to read as a soft square,
    /// not so much that it becomes a circle. A square is the checkbox's own shape — it says "tick me".</summary>
    public double CheckboxCornerRatio { get; init; } = 0.28;

    /// <summary>The thickness of the box's outline, as a fraction of its side (with a floor, so the line stays
    /// crisp at small font sizes rather than thinning to a hair).</summary>
    public double CheckboxBorderRatio { get; init; } = 0.085;

    /// <summary>The inset before the box — the space between the item's indent and the outline.</summary>
    public double CheckboxLeftPad => BaseFontSize * 0.0625;

    /// <summary>The space between the box and the item's text.</summary>
    public double CheckboxGap => BaseFontSize * 0.45;

    /// <summary>The width a concealed task marker reserves — inset + box + gap — and the hanging indent an item's
    /// wrapped rows sit at. Fixed across the three states, so changing an item's state never shifts its text
    /// sideways.</summary>
    public double CheckboxGutter => CheckboxLeftPad + CheckboxSize + CheckboxGap;

    /// <summary>
    /// The dimmed, struck-through treatment of a <b>done</b> item's text — it is no longer work to do. The rule is
    /// drawn in the same dimmed colour as the text, so it marks the line without becoming a second signal.
    /// </summary>
    public RunStyle OnDoneTask(RunStyle style) =>
        style with { Foreground = TaskDoneForeground, Decorations = DoneStrike };

    /// <summary>The alpha of the strike-through rule, so it marks the text without shouting over it.</summary>
    private const byte TaskStrikeAlpha = 0xB0;

    // Built once per theme (themes are immutable) and shared, so the draw path allocates nothing per box and the
    // FormattedText cache keys on a stable decoration object.
    private TextDecorationCollection? DoneStrike => field ??= TaskDoneForeground is ISolidColorBrush solid
        ? Strike(new SolidColorBrush(Color.FromArgb(TaskStrikeAlpha, solid.Color.R, solid.Color.G, solid.Color.B)))
        : Strike(TaskDoneForeground);

    /// <summary>Thickness of the drawn horizontal rule.</summary>
    public double RuleThickness { get; init; } = 1;

    /// <summary>Corner radius of the fenced-code panel.</summary>
    public double CodeBlockCornerRadius { get; init; } = 8;

    /// <summary>Width of the blockquote's left accent bar.</summary>
    public double QuoteBarWidth { get; init; } = 3;

    /// <summary>The space between a quote's bar and the text that hangs off it.</summary>
    public double QuoteBarGap { get; init; } = 14;

    /// <summary>The step from one nesting level's quote bar to the next — the document's one
    /// <see cref="IndentStep"/>, so a nested quote and a nested list item indent alike.</summary>
    public double QuoteBarStep => IndentStep;

    /// <summary>
    /// The gutter a blockquote's text hangs at: its own bar — one <see cref="IndentStep"/> in per level of
    /// <paramref name="depth"/> — plus the gap after it.
    ///
    /// <para>Derived from the nesting depth rather than measured from the source <c>"&gt;&gt; "</c>, which is the
    /// same handful of pixels however deep the quote is. That made nesting barely register as indentation, and
    /// left a <c>&gt;&gt;</c> quote's prose sitting on top of its own second bar. The bars are drawn on the same
    /// step, so text and bars can't drift apart.</para>
    /// </summary>
    public double QuoteGutter(int depth) =>
        ((Math.Max(1, depth) - 1) * QuoteBarStep) + QuoteBarWidth + QuoteBarGap;

    /// <summary>
    /// The horizontal step <b>one level of nesting</b> indents by — shared by lists and blockquotes, so a document
    /// has a single indent rhythm however its structures are mixed.
    ///
    /// <para>It is the <i>level</i> that steps, not the source whitespace. Markdown spells one level of nesting as
    /// two leading spaces on a bullet, three on a number, two or six on a task, and one more <c>&gt;</c> on a
    /// quote — so rendering that literal markup indented the same nesting depth by a different amount for each
    /// construct, bullets least of all. The level itself comes from the document's structure
    /// (<c>MarkdownEditor.ListLevel</c>, and the depth a blockquote marker carries); this is what a level is
    /// worth in pixels.</para>
    /// </summary>
    public double IndentStep => BaseFontSize * 1.5;

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
            TokenType.Highlight => new RunStyle(Body(), BaseFontSize, Foreground, HighlightBackground, false, null, HighlightPadding),
            // Inline code gets a "chip" background, padded on both sides; code-block text does not — the
            // continuous block panel behind the whole fence/indent is its background instead (no per-line box).
            TokenType.InlineCode => new RunStyle(Mono(), CodeFontSize, CodeForeground, CodeBackground, false, null, InlineCodePadding),
            TokenType.CodeText => new RunStyle(Mono(), CodeFontSize, CodeBlockForeground, null, false),
            TokenType.LinkText or TokenType.Autolink or TokenType.WikiLink => new RunStyle(Body(), BaseFontSize, LinkForeground, null, false, LinkUnderline),
            TokenType.LinkUrl or TokenType.ImageUrl or TokenType.Embed => Marker(),
            TokenType.BlockquoteMarker or TokenType.ListMarker or TokenType.TaskMarker
                or TokenType.HorizontalRule or TokenType.CodeFence or TokenType.FrontMatterFence
                or TokenType.TableDelimiter or TokenType.Marker or TokenType.Escape
                or TokenType.SetextUnderline => Marker(),
            TokenType.FrontMatterText => new RunStyle(Mono(), CodeFontSize, MarkerForeground, null, false),
            _ => new RunStyle(Body(), BaseFontSize, Foreground, null, false),
        };
    }

    // ----- the heading type scale -----
    //
    // The scale is authored against a 16px body and expressed here as multiples of BaseFontSize, so changing
    // the base font size rescales every level together (the "rem" behaviour a stylesheet would get for free).
    // Ratio ~1.25 between adjacent levels down to H4; below that, differentiation shifts from size to weight
    // and colour, because shrinking further would put H5/H6 under the body size and make them unreadable.
    // Headings bond *downward* — a large SpaceBefore and a small SpaceAfter — so each one groups with the
    // content it introduces rather than floating between two blocks.
    //
    //  level   size   row    before  after   weight     colour
    //   H1      30     38      48      12    Bold       HeadingForeground
    //   H2      24     31      38      10    Bold       HeadingForeground
    //   H3      20     27      32       8    SemiBold   HeadingForegroundH3
    //   H4      17     24      27       7    SemiBold   HeadingForegroundH4
    //   H5      15     21      24       6    Bold       HeadingForegroundMuted
    //   H6      13     18      21       5    Bold       HeadingForegroundFaint
    //   body    16     26       0      12    Normal     Foreground
    private static readonly (double Size, double Row, double Before, double After)[] HeadingScale =
    [
        (30, 38, 48, 12), // H1
        (24, 31, 38, 10), // H2
        (20, 27, 32, 8),  // H3
        (17, 24, 27, 7),  // H4
        (15, 21, 24, 6),  // H5
        (13, 18, 21, 5),  // H6
    ];

    /// <summary>The scale factor from the authored 16px baseline to the configured <see cref="BaseFontSize"/>.</summary>
    private double Scale => BaseFontSize / 16.0;

    private static (double Size, double Row, double Before, double After) HeadingRow(int level) =>
        HeadingScale[Math.Clamp(level, 1, 6) - 1];

    /// <summary>The size of a line that begins with a heading marker of <paramref name="level"/> (1–6).</summary>
    public double HeadingSize(int level) => HeadingRow(level).Size * Scale;

    /// <summary>The weight of a heading of <paramref name="level"/>. H3/H4 step down to SemiBold; H5/H6 go back
    /// to Bold because at their size weight is the only thing left carrying the hierarchy.</summary>
    public static FontWeight HeadingWeight(int level) => level is 3 or 4 ? FontWeight.SemiBold : FontWeight.Bold;

    /// <summary>The foreground of a heading of <paramref name="level"/> — desaturating as the level descends.</summary>
    public IBrush HeadingForegroundFor(int level) => level switch
    {
        1 or 2 => HeadingForeground,
        3 => HeadingForegroundH3,
        4 => HeadingForegroundH4,
        5 => HeadingForegroundMuted,
        _ => HeadingForegroundFaint,
    };

    /// <summary>
    /// Scale, weight, and colour <paramref name="style"/> for a run sitting on a heading line of
    /// <paramref name="level"/>. Every run on the line takes the heading's size (so a concealed <c>#</c>, a code
    /// span, or a link all sit on the same baseline as the heading text) and at least the heading's weight, but
    /// only prose runs take the heading's colour — a marker, link, or code run keeps the colour that identifies it.
    /// </summary>
    public RunStyle OnHeading(RunStyle style, int level, TokenType type)
    {
        var tf = style.Typeface;
        var weight = (FontWeight)Math.Max((int)tf.Weight, (int)HeadingWeight(level));
        style = style with
        {
            FontSize = HeadingSize(level),
            Typeface = new Typeface(tf.FontFamily, tf.Style, weight),
        };

        return type is TokenType.HeadingText or TokenType.Text or TokenType.Strong or TokenType.Emphasis
            or TokenType.StrongEmphasis or TokenType.Strikethrough or TokenType.Highlight
            ? style with { Foreground = HeadingForegroundFor(level) }
            : style;
    }

    /// <summary>
    /// The vertical metrics for a block of <paramref name="kind"/> (and, for a heading, <paramref name="level"/>).
    /// <paramref name="firstOfRun"/>/<paramref name="lastOfRun"/> say whether this block opens/closes a run of
    /// same-kind neighbours — a fenced-code panel and a blockquote are each spelled as one source line per block,
    /// so only the run's outer edges carry the inner padding that makes them read as one panel.
    /// </summary>
    public BlockMetrics MetricsFor(MarkdownBlockKind kind, int level = 0, bool firstOfRun = true, bool lastOfRun = true)
    {
        var s = Scale;
        switch (kind)
        {
            case MarkdownBlockKind.Heading:
                var h = HeadingRow(level);
                return new BlockMetrics(h.Row * s, h.Before * s, h.After * s);

            // A list is tight: 4px between items, with the list block's own margin coming from the paragraph
            // above/below it (whose SpaceAfter/Before collapses against the item's).
            case MarkdownBlockKind.ListItem:
                return new BlockMetrics(BodyRowHeight, 0, 4 * s);

            case MarkdownBlockKind.Blockquote:
                return new BlockMetrics(BodyRowHeight, 16 * s, 16 * s, firstOfRun ? 8 * s : 0, lastOfRun ? 8 * s : 0);

            case MarkdownBlockKind.Code:
                return new BlockMetrics(CodeRowHeight, 12 * s, 16 * s, firstOfRun ? 12 * s : 0, lastOfRun ? 12 * s : 0);

            // The rule *is* the section break, so its margin is the whole gap; it needs no row height beyond
            // enough to place a caret on.
            case MarkdownBlockKind.Rule:
                return new BlockMetrics(BodyRowHeight, 24 * s, 24 * s);

            case MarkdownBlockKind.Table:
                return new BlockMetrics(BodyRowHeight, firstOfRun ? 12 * s : 0, lastOfRun ? 16 * s : 0);

            case MarkdownBlockKind.FrontMatter:
                return new BlockMetrics(BodyRowHeight, firstOfRun ? 4 * s : 0, lastOfRun ? 16 * s : 0);

            default:
                return new BlockMetrics(BodyRowHeight, 0, 12 * s);
        }
    }

    /// <summary>
    /// The gap the layout puts between two adjacent blocks. Margins <b>collapse</b> — the gap is the larger of
    /// the two, not their sum — which is the one place this deliberately departs from Avalonia's own box model
    /// (Avalonia adds adjacent margins). Summing them here would put a paragraph's 12px under an H1's 48px and
    /// yield a 60px canyon mid-document; collapsing keeps the heading's own space-before as the single thing
    /// that sets the section gap, which is what the scale was designed around.
    ///
    /// <para>A run of same-kind blocks that reads as one object — a fenced-code panel, a blockquote, a table —
    /// closes up entirely, because each of its source lines is a separate block.</para>
    /// </summary>
    public static double GapBetween(BlockMetrics before, MarkdownBlockKind beforeKind, BlockMetrics after, MarkdownBlockKind afterKind)
    {
        if (beforeKind == afterKind && beforeKind is MarkdownBlockKind.Blockquote or MarkdownBlockKind.Code
            or MarkdownBlockKind.Table or MarkdownBlockKind.FrontMatter)
        {
            return 0;
        }

        return Math.Max(before.SpaceAfter, after.SpaceBefore);
    }

    /// <summary>
    /// The plain treatment for the raw-text view: monospace body text in the foreground colour, with no
    /// background, decorations, or marker flag — every token, marker and content alike, renders identically.
    /// </summary>
    public RunStyle RawTextStyle() => new(Mono(), BaseFontSize, Foreground, null, false);

    private RunStyle Marker() => new(Body(), BaseFontSize, MarkerForeground, null, true);

    private Typeface Body() => new(_body);
    private Typeface Bold() => new(_body, FontStyle.Normal, FontWeight.Bold);
    private Typeface Italic() => new(_body, FontStyle.Italic);
    private Typeface BoldItalic() => new(_body, FontStyle.Italic, FontWeight.Bold);
    private Typeface Mono() => new(_mono);

    /// <summary>
    /// The underline decoration for link text — a solid rule in <see cref="LinkForeground"/>, so links read
    /// like HTML hyperlinks (coloured <i>and</i> underlined). Built once per theme instance (themes are
    /// immutable) and shared, so the FormattedText cache keys on a stable decoration object.
    /// </summary>
    public TextDecorationCollection LinkUnderline => field ??=
    [
        new TextDecoration
        {
            Location = TextDecorationLocation.Underline,
            Stroke = LinkForeground,
            StrokeThickness = 1,
            StrokeThicknessUnit = TextDecorationUnit.Pixel,
        },
    ];

    private static TextDecorationCollection Strike(IBrush foreground) =>
        [
            new TextDecoration
            {
                Location = TextDecorationLocation.Strikethrough,
                Stroke = foreground,
                StrokeThickness = 1,
                StrokeThicknessUnit = TextDecorationUnit.Pixel,
            },
        ];
}
