namespace Cellestra.Core.Styling;

/// <summary>A cell's background fill. <see cref="None"/> (no <see cref="Background"/>) is the default.</summary>
public sealed record Fill(ArgbColor? Background)
{
    public static readonly Fill None = new((ArgbColor?)null);

    public bool HasBackground => Background is { IsTransparent: false };

    public static Fill Solid(ArgbColor color) => new(color);
}
