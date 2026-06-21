using System.Globalization;

namespace Cellestra.Core.Styling;

/// <summary>
/// An immutable ARGB color. Stored as a packed <see cref="uint"/> (0xAARRGGBB) so styles
/// compare and de-duplicate by value in the <see cref="StylePool"/>. ODF serializes colors
/// as <c>#RRGGBB</c>; <see cref="ToHex"/>/<see cref="FromHex"/> bridge that.
/// </summary>
public readonly record struct ArgbColor(uint Value)
{
    public static readonly ArgbColor Black = FromRgb(0, 0, 0);
    public static readonly ArgbColor White = FromRgb(255, 255, 255);
    public static readonly ArgbColor Transparent = new(0x00000000);

    public byte A => (byte)((Value >> 24) & 0xFF);
    public byte R => (byte)((Value >> 16) & 0xFF);
    public byte G => (byte)((Value >> 8) & 0xFF);
    public byte B => (byte)(Value & 0xFF);

    public bool IsTransparent => A == 0;

    public static ArgbColor FromRgb(byte r, byte g, byte b) =>
        new((uint)((0xFF << 24) | (r << 16) | (g << 8) | b));

    public static ArgbColor FromArgb(byte a, byte r, byte g, byte b) =>
        new((uint)((a << 24) | (r << 16) | (g << 8) | b));

    /// <summary>Format as <c>#RRGGBB</c> (ODF style; alpha is dropped).</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public static bool TryFromHex(string? hex, out ArgbColor color)
    {
        color = Black;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        ReadOnlySpan<char> span = hex.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
        {
            span = span[1..];
        }

        if (span.Length != 6 || !uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            return false;
        }

        color = new ArgbColor(0xFF000000 | rgb);
        return true;
    }

    public static ArgbColor FromHex(string hex) =>
        TryFromHex(hex, out var color) ? color : throw new FormatException($"'{hex}' is not a #RRGGBB color.");
}
