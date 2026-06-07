using System.Globalization;

namespace Cellestra.Core.Values;

/// <summary>
/// A typed, immutable cell value: empty, number, text, boolean, or error.
/// A value type so a worksheet of hundreds of thousands of cells carries no
/// per-value heap object (ARCHITECTURE "no object per cell").
/// </summary>
public readonly struct CellValue : IEquatable<CellValue>
{
    private readonly double _number;
    private readonly string? _text;
    private readonly CellError? _error;

    private CellValue(CellValueType type, double number, string? text, CellError? error)
    {
        Type = type;
        _number = number;
        _text = text;
        _error = error;
    }

    /// <summary>The empty value — an untouched cell.</summary>
    public static CellValue Empty { get; } = new(CellValueType.Empty, 0, null, null);

    public static CellValue Number(double value) => new(CellValueType.Number, value, null, null);

    public static CellValue Text(string value) =>
        new(CellValueType.Text, 0, value ?? throw new ArgumentNullException(nameof(value)), null);

    public static CellValue Boolean(bool value) =>
        new(CellValueType.Boolean, value ? 1d : 0d, null, null);

    public static CellValue FromError(CellError error) =>
        new(CellValueType.Error, 0, null, error ?? throw new ArgumentNullException(nameof(error)));

    public CellValueType Type { get; }

    public bool IsEmpty => Type == CellValueType.Empty;
    public bool IsNumber => Type == CellValueType.Number;
    public bool IsText => Type == CellValueType.Text;
    public bool IsBoolean => Type == CellValueType.Boolean;
    public bool IsError => Type == CellValueType.Error;

    /// <summary>The numeric payload. Valid for Number and Boolean (1/0).</summary>
    public double AsNumber => Type is CellValueType.Number or CellValueType.Boolean
        ? _number
        : throw new InvalidOperationException($"CellValue of type {Type} is not numeric.");

    public string AsText => _text
        ?? throw new InvalidOperationException($"CellValue of type {Type} is not text.");

    public bool AsBoolean => Type == CellValueType.Boolean
        ? _number != 0d
        : throw new InvalidOperationException($"CellValue of type {Type} is not boolean.");

    public CellError AsError => _error
        ?? throw new InvalidOperationException($"CellValue of type {Type} is not an error.");

    /// <summary>
    /// A culture-invariant, lossless string form used for storing the cached value
    /// in <c>.ods</c> and for tab-separated clipboard text. Display formatting
    /// (locale, number formats) is a separate concern handled by the styling layer.
    /// </summary>
    public string ToInvariantString() => Type switch
    {
        CellValueType.Empty => string.Empty,
        CellValueType.Number => _number.ToString("R", CultureInfo.InvariantCulture),
        CellValueType.Text => _text!,
        CellValueType.Boolean => _number != 0d ? "TRUE" : "FALSE",
        CellValueType.Error => _error!.Token,
        _ => string.Empty,
    };

    public bool Equals(CellValue other)
    {
        if (Type != other.Type)
        {
            return false;
        }

        return Type switch
        {
            CellValueType.Empty => true,
            CellValueType.Number or CellValueType.Boolean => _number.Equals(other._number),
            CellValueType.Text => string.Equals(_text, other._text, StringComparison.Ordinal),
            CellValueType.Error => _error!.Equals(other._error),
            _ => false,
        };
    }

    public override bool Equals(object? obj) => obj is CellValue other && Equals(other);

    public override int GetHashCode() => Type switch
    {
        CellValueType.Number or CellValueType.Boolean => HashCode.Combine(Type, _number),
        CellValueType.Text => HashCode.Combine(Type, _text),
        CellValueType.Error => HashCode.Combine(Type, _error),
        _ => HashCode.Combine(Type),
    };

    public static bool operator ==(CellValue left, CellValue right) => left.Equals(right);

    public static bool operator !=(CellValue left, CellValue right) => !left.Equals(right);

    public override string ToString() => ToInvariantString();
}
