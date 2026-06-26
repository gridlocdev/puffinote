using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;

namespace BirdBrain.Components.Controls.Icons;

/// <summary>
/// Loads the vendored Lucide SVG glyphs (MIT, under <c>Assets/icons</c>) and parses each into a
/// stroked <see cref="Geometry"/>, cached by key. Golden Rule #6: icons are local SVGs, never
/// fetched at runtime. Lucide icons are uniform — a 24×24 viewBox, <c>fill="none"</c>, stroke
/// width 2, round caps/joins — so a tiny element→path converter (path/line/rect/circle/ellipse/
/// polyline/polygon) covers the whole set, and rendering is a single stroked draw.
/// </summary>
public static class LucideIcons
{
    /// <summary>The Lucide design grid; every glyph is authored in this coordinate space.</summary>
    public const double ViewBox = 24.0;

    private static readonly ConcurrentDictionary<string, Geometry?> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// The combined geometry for <paramref name="key"/> (the SVG file name without extension), or
    /// null when no such glyph is vendored. Parsed once and cached; safe to call on the UI thread.
    /// </summary>
    public static Geometry? Get(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        return _cache.GetOrAdd(key, Load);
    }

    private static Geometry? Load(string key)
    {
        // The icons ship in this shared library (assembly name "BirdBrain.Components"), which is the
        // authority an avares URI resolves against — so every app in the family finds them,
        // regardless of the host assembly's name.
        var uri = new Uri($"avares://BirdBrain.Components/Assets/icons/{key}.svg");
        if (!AssetLoader.Exists(uri))
        {
            return null;
        }

        using var stream = AssetLoader.Open(uri);
        var doc = XDocument.Load(stream);
        if (doc.Root is null)
        {
            return null;
        }

        var group = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var element in doc.Root.Elements())
        {
            string? data = ToPathData(element);
            if (data is null)
            {
                continue;
            }

            try
            {
                group.Children.Add(Geometry.Parse(data));
            }
            catch (FormatException)
            {
                // A malformed element is skipped rather than failing the whole glyph.
            }
        }

        return group.Children.Count > 0 ? group : null;
    }

    /// <summary>Convert one Lucide SVG element to SVG path-data, or null if it isn't a drawable shape.</summary>
    private static string? ToPathData(XElement element) => element.Name.LocalName switch
    {
        "path" => Attr(element, "d"),
        "line" => $"M {N(element, "x1")},{N(element, "y1")} L {N(element, "x2")},{N(element, "y2")}",
        "rect" => Rect(element),
        "circle" => Ellipse(N(element, "cx"), N(element, "cy"), N(element, "r"), N(element, "r")),
        "ellipse" => Ellipse(N(element, "cx"), N(element, "cy"), N(element, "rx"), N(element, "ry")),
        "polyline" => Poly(Attr(element, "points"), close: false),
        "polygon" => Poly(Attr(element, "points"), close: true),
        _ => null,
    };

    private static string Rect(XElement element)
    {
        double x = N(element, "x"), y = N(element, "y");
        double w = N(element, "width"), h = N(element, "height");
        double rx = N(element, "rx"), ry = N(element, "ry");
        if (rx <= 0)
        {
            rx = ry;
        }

        if (ry <= 0)
        {
            ry = rx;
        }

        rx = Math.Min(rx, w / 2);
        ry = Math.Min(ry, h / 2);

        if (rx <= 0 || ry <= 0)
        {
            return $"M {F(x)},{F(y)} h {F(w)} v {F(h)} h {F(-w)} Z";
        }

        var sb = new StringBuilder();
        sb.Append($"M {F(x + rx)},{F(y)} ");
        sb.Append($"h {F(w - 2 * rx)} ");
        sb.Append($"a {F(rx)},{F(ry)} 0 0 1 {F(rx)},{F(ry)} ");
        sb.Append($"v {F(h - 2 * ry)} ");
        sb.Append($"a {F(rx)},{F(ry)} 0 0 1 {F(-rx)},{F(ry)} ");
        sb.Append($"h {F(-(w - 2 * rx))} ");
        sb.Append($"a {F(rx)},{F(ry)} 0 0 1 {F(-rx)},{F(-ry)} ");
        sb.Append($"v {F(-(h - 2 * ry))} ");
        sb.Append($"a {F(rx)},{F(ry)} 0 0 1 {F(rx)},{F(-ry)} Z");
        return sb.ToString();
    }

    /// <summary>A full ellipse (or circle) drawn as two half-arcs.</summary>
    private static string Ellipse(double cx, double cy, double rx, double ry) =>
        $"M {F(cx - rx)},{F(cy)} a {F(rx)},{F(ry)} 0 1 0 {F(2 * rx)},0 a {F(rx)},{F(ry)} 0 1 0 {F(-2 * rx)},0";

    private static string? Poly(string? points, bool close)
    {
        if (string.IsNullOrWhiteSpace(points))
        {
            return null;
        }

        // Lucide separates coordinates with spaces and/or commas; normalise to a token stream.
        var tokens = points.Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 4 || tokens.Length % 2 != 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < tokens.Length; i += 2)
        {
            sb.Append(i == 0 ? "M " : "L ");
            sb.Append(tokens[i]).Append(',').Append(tokens[i + 1]).Append(' ');
        }

        if (close)
        {
            sb.Append('Z');
        }

        return sb.ToString();
    }

    private static string? Attr(XElement element, string name) => element.Attribute(name)?.Value;

    private static double N(XElement element, string name) =>
        double.TryParse(element.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
