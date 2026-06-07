using System.Globalization;
using System.Xml.Linq;
using Cellestra.Core.Styling;
using static Cellestra.Core.Serialization.Ods.OdsXmlNames;

namespace Cellestra.Core.Serialization.Ods.Mapping;

/// <summary>Maps a <see cref="CellStyle"/> to an ODF table-cell automatic style and back.</summary>
public static class OdsCellStyles
{
    public static XElement Build(string name, CellStyle style, string? dataStyleName)
    {
        var element = new XElement(Style + "style",
            new XAttribute(Style + "name", name),
            new XAttribute(Style + "family", "table-cell"));
        if (dataStyleName is not null)
        {
            element.Add(new XAttribute(Style + "data-style-name", dataStyleName));
        }

        var cellProps = new XElement(Style + "table-cell-properties");
        if (style.Fill.HasBackground)
        {
            cellProps.Add(new XAttribute(Fo + "background-color", style.Fill.Background!.Value.ToHex()));
        }

        cellProps.Add(new XAttribute(Style + "vertical-align", style.Alignment.Vertical switch
        {
            VerticalAlignment.Top => "top",
            VerticalAlignment.Middle => "middle",
            _ => "bottom",
        }));

        if (style.Alignment.WrapText)
        {
            cellProps.Add(new XAttribute(Fo + "wrap-option", "wrap"));
        }

        AddBorders(cellProps, style.Border);
        if (cellProps.HasAttributes)
        {
            element.Add(cellProps);
        }

        if (style.Alignment.Horizontal != HorizontalAlignment.General)
        {
            element.Add(new XElement(Style + "paragraph-properties",
                new XAttribute(Fo + "text-align", style.Alignment.Horizontal switch
                {
                    HorizontalAlignment.Left => "start",
                    HorizontalAlignment.Center => "center",
                    HorizontalAlignment.Right => "end",
                    _ => "start",
                })));
        }

        var textProps = new XElement(Style + "text-properties");
        var font = style.Font;
        if (font.Bold)
        {
            textProps.Add(new XAttribute(Fo + "font-weight", "bold"));
        }

        if (font.Italic)
        {
            textProps.Add(new XAttribute(Fo + "font-style", "italic"));
        }

        if (font.Underline)
        {
            textProps.Add(new XAttribute(Style + "text-underline-style", "solid"));
            textProps.Add(new XAttribute(Style + "text-underline-width", "auto"));
            textProps.Add(new XAttribute(Style + "text-underline-color", "font-color"));
        }

        if (!font.Color.Equals(ArgbColor.Black) && !font.Color.IsTransparent)
        {
            textProps.Add(new XAttribute(Fo + "color", font.Color.ToHex()));
        }

        if (!string.Equals(font.Family, Defaults.FontFamily, StringComparison.Ordinal))
        {
            textProps.Add(new XAttribute(Fo + "font-family", font.Family));
        }

        if (font.SizePoints != Defaults.FontSizePoints)
        {
            textProps.Add(new XAttribute(Fo + "font-size",
                font.SizePoints.ToString(CultureInfo.InvariantCulture) + "pt"));
        }

        if (textProps.HasAttributes)
        {
            element.Add(textProps);
        }

        return element;
    }

    public static CellStyle Parse(XElement styleElement, Func<string, NumberFormat> resolveDataStyle)
    {
        var font = Font.Default;
        var fill = Fill.None;
        var alignment = Alignment.Default;
        var border = Border.None;

        var textProps = styleElement.Element(Style + "text-properties");
        if (textProps is not null)
        {
            font = font with
            {
                Bold = textProps.Attribute(Fo + "font-weight")?.Value == "bold",
                Italic = textProps.Attribute(Fo + "font-style")?.Value == "italic",
                Underline = textProps.Attribute(Style + "text-underline-style") is { } u && u.Value != "none",
            };

            if (ArgbColor.TryFromHex(textProps.Attribute(Fo + "color")?.Value, out var color))
            {
                font = font with { Color = color };
            }

            if (textProps.Attribute(Fo + "font-family")?.Value is { Length: > 0 } family)
            {
                font = font with { Family = family };
            }

            if (ParsePt(textProps.Attribute(Fo + "font-size")?.Value) is { } size)
            {
                font = font with { SizePoints = size };
            }
        }

        var cellProps = styleElement.Element(Style + "table-cell-properties");
        if (cellProps is not null)
        {
            if (ArgbColor.TryFromHex(cellProps.Attribute(Fo + "background-color")?.Value, out var bg) &&
                cellProps.Attribute(Fo + "background-color")?.Value != "transparent")
            {
                fill = Fill.Solid(bg);
            }

            var vertical = cellProps.Attribute(Style + "vertical-align")?.Value switch
            {
                "top" => VerticalAlignment.Top,
                "middle" => VerticalAlignment.Middle,
                _ => VerticalAlignment.Bottom,
            };
            bool wrap = cellProps.Attribute(Fo + "wrap-option")?.Value == "wrap";
            alignment = alignment with { Vertical = vertical, WrapText = wrap };
            border = ParseBorders(cellProps);
        }

        var paragraphProps = styleElement.Element(Style + "paragraph-properties");
        if (paragraphProps?.Attribute(Fo + "text-align")?.Value is { } align)
        {
            alignment = alignment with
            {
                Horizontal = align switch
                {
                    "center" => HorizontalAlignment.Center,
                    "end" => HorizontalAlignment.Right,
                    "start" => HorizontalAlignment.Left,
                    _ => HorizontalAlignment.General,
                },
            };
        }

        string? dataStyleName = styleElement.Attribute(Style + "data-style-name")?.Value;
        var numberFormat = dataStyleName is null ? NumberFormat.General : resolveDataStyle(dataStyleName);

        return new CellStyle(font, fill, border, alignment, numberFormat);
    }

    private static void AddBorders(XElement cellProps, Border border)
    {
        if (!border.HasAnyEdge)
        {
            return;
        }

        AddEdge(cellProps, "border-left", border.Left);
        AddEdge(cellProps, "border-right", border.Right);
        AddEdge(cellProps, "border-top", border.Top);
        AddEdge(cellProps, "border-bottom", border.Bottom);
    }

    private static void AddEdge(XElement cellProps, string attribute, BorderEdge edge)
    {
        if (edge.IsVisible)
        {
            cellProps.Add(new XAttribute(Fo + attribute, BorderToOdf(edge)));
        }
    }

    private static string BorderToOdf(BorderEdge edge)
    {
        (string width, string style) = edge.Style switch
        {
            BorderLineStyle.Thin => ("0.5pt", "solid"),
            BorderLineStyle.Medium => ("1pt", "solid"),
            BorderLineStyle.Thick => ("2pt", "solid"),
            BorderLineStyle.Dashed => ("0.5pt", "dashed"),
            BorderLineStyle.Dotted => ("0.5pt", "dotted"),
            BorderLineStyle.Double => ("1pt", "double"),
            _ => ("0.5pt", "solid"),
        };

        return $"{width} {style} {edge.Color.ToHex()}";
    }

    private static Border ParseBorders(XElement cellProps)
    {
        var all = cellProps.Attribute(Fo + "border")?.Value;
        BorderEdge Edge(string side)
        {
            string? value = cellProps.Attribute(Fo + side)?.Value ?? all;
            return ParseEdge(value);
        }

        return new Border(Edge("border-left"), Edge("border-right"), Edge("border-top"), Edge("border-bottom"));
    }

    private static BorderEdge ParseEdge(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "none")
        {
            return BorderEdge.None;
        }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var color = ArgbColor.Black;
        string lineStyle = "solid";
        double widthPt = 0.5;
        foreach (var part in parts)
        {
            if (ArgbColor.TryFromHex(part, out var c))
            {
                color = c;
            }
            else if (part.EndsWith("pt", StringComparison.OrdinalIgnoreCase) &&
                     double.TryParse(part[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double w))
            {
                widthPt = w;
            }
            else
            {
                lineStyle = part.ToLowerInvariant();
            }
        }

        var style = lineStyle switch
        {
            "dashed" => BorderLineStyle.Dashed,
            "dotted" => BorderLineStyle.Dotted,
            "double" => BorderLineStyle.Double,
            _ => widthPt >= 2 ? BorderLineStyle.Thick : widthPt >= 1 ? BorderLineStyle.Medium : BorderLineStyle.Thin,
        };

        return new BorderEdge(style, color);
    }

    private static double? ParsePt(string? value)
    {
        if (value is null || !value.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return double.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double pt) ? pt : null;
    }
}
