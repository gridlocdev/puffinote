namespace Cellestra.Core.Styling;

public enum BorderLineStyle
{
    None,
    Thin,
    Medium,
    Thick,
    Dashed,
    Dotted,
    Double,
}

/// <summary>One edge of a cell border: a line style and color.</summary>
public sealed record BorderEdge(BorderLineStyle Style, ArgbColor Color)
{
    public static readonly BorderEdge None = new(BorderLineStyle.None, ArgbColor.Black);

    public bool IsVisible => Style != BorderLineStyle.None;

    public static BorderEdge Of(BorderLineStyle style) => new(style, ArgbColor.Black);
}

/// <summary>The four borders of a cell. <see cref="None"/> (all edges off) is the default.</summary>
public sealed record Border(BorderEdge Left, BorderEdge Right, BorderEdge Top, BorderEdge Bottom)
{
    public static readonly Border None = new(BorderEdge.None, BorderEdge.None, BorderEdge.None, BorderEdge.None);

    public bool HasAnyEdge => Left.IsVisible || Right.IsVisible || Top.IsVisible || Bottom.IsVisible;

    public static Border All(BorderLineStyle style)
    {
        var edge = BorderEdge.Of(style);
        return new Border(edge, edge, edge, edge);
    }
}
