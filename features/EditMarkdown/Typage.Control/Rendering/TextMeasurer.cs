using System.Globalization;
using Avalonia.Media;

namespace Typage.Control.Rendering;

/// <summary>
/// Owns text measurement and the editor's <see cref="FormattedText"/> / advance-width caches. It depends
/// only on the fonts and sizes carried by a <see cref="RunStyle"/> (plus the current tab width), never on
/// caret, selection, or scroll state — so it can be exercised without a live control. The editor calls
/// <see cref="Invalidate"/> when the theme or fonts change, and supplies the tab width through the
/// constructor delegate so the control's <c>IndentWidth</c> stays the single source of truth.
/// </summary>
internal sealed class TextMeasurer
{
    private readonly Func<int> _tabWidth;
    private readonly Dictionary<FormattedKey, FormattedText> _formattedCache = [];
    private readonly Dictionary<WidthKey, double> _widthCache = [];

    public TextMeasurer(Func<int> tabWidth) => _tabWidth = tabWidth;

    /// <summary>Number of <see cref="FormattedText"/> objects built (cache misses); for perf tests.</summary>
    public int FormattedBuildCount { get; private set; }

    /// <summary>Number of real text-shaping measurements performed (cache misses); for perf tests.</summary>
    public int RawMeasureCount { get; private set; }

    /// <summary>Drop all cached widths and glyph runs — call when the theme or fonts change.</summary>
    public void Invalidate()
    {
        _widthCache.Clear();  // cached widths assume the old fonts/sizes
        _formattedCache.Clear(); // cached glyph runs were built with the old theme's brushes/fonts
    }

    /// <summary>
    /// The advance width of <paramref name="text"/> in <paramref name="style"/>, <b>including any
    /// trailing whitespace</b>. <see cref="FormattedText.Width"/> omits trailing whitespace, which
    /// made a run ending in a space (e.g. a heading marker <c>"## "</c> or a word before emphasis)
    /// advance too little so the next run drew on top of the space — the missing-space bug. Appending
    /// a non-space sentinel makes those spaces interior (and thus measured) and is then subtracted out.
    /// </summary>
    public double MeasureWidth(string text, RunStyle style)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        // Width is fully determined by (text, font family/style/weight, size). Cache it so caret
        // moves, clicks, selection, and per-frame advances stop reconstructing/shaping FormattedText
        // for runs they've already measured (the cache is cleared when the theme/fonts change).
        var tf = style.Typeface;
        var key = new WidthKey(text, tf.FontFamily, tf.Style, tf.Weight, style.FontSize);
        if (_widthCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var width = char.IsWhiteSpace(text[^1])
            ? RawWidth(text + ".", style) - RawWidth(".", style)
            : RawWidth(text, style);

        if (_widthCache.Count >= 16384)
        {
            _widthCache.Clear(); // bound the cache; it rebuilds on demand
        }

        _widthCache[key] = width;
        return width;
    }

    /// <summary>
    /// The drawable <see cref="FormattedText"/> for a run, cached on its full visual identity (text,
    /// font, size, brush, decorations). The draw path builds one <see cref="FormattedText"/> per
    /// visible run per frame; for a static viewport every run is identical frame to frame, so caching
    /// the objects removes the last per-frame allocation/shaping (the cache is invalidated on a theme
    /// change, and a marker run is rebuilt only when it flips between revealed and dimmed).
    /// </summary>
    public FormattedText GetFormattedText(string text, RunStyle style, IBrush brush)
    {
        var key = new FormattedKey(text, style.Typeface, style.FontSize, brush);
        if (_formattedCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        FormattedBuildCount++; // counts FormattedText construction (cache misses); used by perf tests
        // Underline/strikethrough are NOT applied to the FormattedText: Avalonia's text decorations don't
        // render reliably across draw backends (the same reason Cellestra overlays them), so the strike/
        // underline lines are drawn by hand in DrawTextRun instead.
        var ft = new FormattedText(ExpandTabs(text), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            style.Typeface, style.FontSize, brush);

        if (_formattedCache.Count >= 8192)
        {
            _formattedCache.Clear(); // bound the cache; it rebuilds on demand
        }

        _formattedCache[key] = ft;
        return ft;
    }

    private double RawWidth(string text, RunStyle style)
    {
        RawMeasureCount++; // counts actual FormattedText shaping (cache misses) — used by perf tests
        return new FormattedText(ExpandTabs(text), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            style.Typeface, style.FontSize, style.Foreground).Width;
    }

    /// <summary>
    /// A copy of <paramref name="text"/> with each tab expanded to the current tab width in spaces, for
    /// measuring and drawing only — the run still covers the source tab as one column, so a tab indents by a
    /// consistent width while the 1:1 offset↔visual map (which indexes the un-expanded run text) is preserved.
    /// </summary>
    private string ExpandTabs(string text) =>
        text.IndexOf('\t') < 0 ? text : text.Replace("\t", new string(' ', Math.Max(1, _tabWidth())));

    private readonly record struct FormattedKey(string Text, Typeface Typeface, double Size, IBrush Brush);

    private readonly record struct WidthKey(string Text, FontFamily Family, FontStyle Style, FontWeight Weight, double Size);
}
