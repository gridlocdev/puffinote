namespace Cellestra.Core.Styling;

/// <summary>Horizontal text alignment. <see cref="General"/> resolves to left for text, right for numbers (PRD §8.8).</summary>
public enum HorizontalAlignment
{
    General,
    Left,
    Center,
    Right,
}

public enum VerticalAlignment
{
    Top,
    Middle,
    Bottom,
}

/// <summary>A cell's alignment and wrapping. The default is General / Bottom / no wrap (PRD §8.8).</summary>
public sealed record Alignment(
    HorizontalAlignment Horizontal = HorizontalAlignment.General,
    VerticalAlignment Vertical = VerticalAlignment.Bottom,
    bool WrapText = false)
{
    public static readonly Alignment Default = new();
}
