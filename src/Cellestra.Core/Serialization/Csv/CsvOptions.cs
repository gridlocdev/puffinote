namespace Cellestra.Core.Serialization.Csv;

/// <summary>
/// CSV import/export options (PRD §8.4). UTF-8 throughout; comma is the default delimiter
/// (semicolon and tab are selectable); RFC 4180 quoting; export writes LF line endings.
/// </summary>
public sealed record CsvOptions
{
    public char Delimiter { get; init; } = ',';

    public static CsvOptions Default { get; } = new();

    public static CsvOptions Comma { get; } = new() { Delimiter = ',' };

    public static CsvOptions Semicolon { get; } = new() { Delimiter = ';' };

    public static CsvOptions Tab { get; } = new() { Delimiter = '\t' };
}
