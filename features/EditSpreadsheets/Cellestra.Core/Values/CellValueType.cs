namespace Cellestra.Core.Values;

/// <summary>The kind of data a <see cref="CellValue"/> holds.</summary>
public enum CellValueType
{
    /// <summary>No content. The default for an untouched cell.</summary>
    Empty,

    /// <summary>A floating-point number. Dates/times are numbers under a date format.</summary>
    Number,

    /// <summary>A text string.</summary>
    Text,

    /// <summary>A logical TRUE/FALSE. Numeric-compatible (TRUE = 1, FALSE = 0).</summary>
    Boolean,

    /// <summary>An error value such as #DIV/0! — see <see cref="CellError"/>.</summary>
    Error,
}
