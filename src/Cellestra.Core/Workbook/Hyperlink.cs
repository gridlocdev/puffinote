namespace Cellestra.Core.Model;

/// <summary>
/// A hyperlink attached to a cell (web or file URL), serialized to/from ODF <c>text:a</c>.
/// The visible cell text is the cell's own value; <see cref="Target"/> is where it points.
/// </summary>
public sealed record Hyperlink(string Target)
{
    /// <summary>Optional tooltip shown on hover.</summary>
    public string? Tooltip { get; init; }
}
