namespace Cellestra.Core.Model;

/// <summary>
/// The rules a worksheet name must satisfy (PRD §4 "rename"): non-empty, within a length cap,
/// and free of the characters that have meaning in references — <c>: \ / ? * [ ]</c> — plus the
/// leading/trailing apostrophe that quoting uses. Uniqueness is enforced separately by the
/// <see cref="Workbook"/>, which owns the set of existing names. Names with spaces are legal and
/// are quoted in references by <see cref="CellAddress"/>.
/// </summary>
public static class SheetName
{
    /// <summary>The maximum length of a worksheet name (matches the LibreOffice/Excel cap).</summary>
    public const int MaxLength = 31;

    /// <summary>Characters that may not appear in a worksheet name.</summary>
    public static readonly char[] ForbiddenChars = [':', '\\', '/', '?', '*', '[', ']'];

    public static bool IsValid(string? name, out string error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "A sheet name cannot be empty.";
            return false;
        }

        if (name.Length > MaxLength)
        {
            error = $"A sheet name cannot be longer than {MaxLength} characters.";
            return false;
        }

        int forbidden = name.IndexOfAny(ForbiddenChars);
        if (forbidden >= 0)
        {
            error = $"A sheet name cannot contain '{name[forbidden]}'.";
            return false;
        }

        if (name[0] == '\'' || name[^1] == '\'')
        {
            error = "A sheet name cannot start or end with an apostrophe.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Throws <see cref="ArgumentException"/> with a human-readable reason if invalid.</summary>
    public static void Validate(string? name)
    {
        if (!IsValid(name, out string error))
        {
            throw new ArgumentException(error, nameof(name));
        }
    }
}
