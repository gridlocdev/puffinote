using System.Globalization;
using Cellestra.Core.Values;

namespace Cellestra.Core.Formulas.Evaluation;

/// <summary>
/// Type-coercion rules shared by the evaluator and the functions. Arithmetic operators and
/// most scalar functions coerce loosely (blank → 0, "12" → 12, TRUE → 1); errors always
/// propagate. Aggregation functions decide separately which value kinds to include.
/// </summary>
public static class Coercion
{
    /// <summary>Coerce to a number for arithmetic. Blank → 0, text parsed invariantly, bool → 1/0.</summary>
    public static bool TryToNumber(CellValue value, out double number, out CellError? error)
    {
        error = null;
        switch (value.Type)
        {
            case CellValueType.Empty:
                number = 0;
                return true;
            case CellValueType.Number:
            case CellValueType.Boolean:
                number = value.AsNumber;
                return true;
            case CellValueType.Text:
                if (double.TryParse(value.AsText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    return true;
                }

                error = CellError.Value($"'{value.AsText}' is not a number.");
                number = 0;
                return false;
            case CellValueType.Error:
                error = value.AsError;
                number = 0;
                return false;
            default:
                error = CellError.Value();
                number = 0;
                return false;
        }
    }

    /// <summary>Coerce to display text for concatenation and text functions.</summary>
    public static bool TryToText(CellValue value, out string text, out CellError? error)
    {
        error = null;
        switch (value.Type)
        {
            case CellValueType.Empty:
                text = string.Empty;
                return true;
            case CellValueType.Text:
                text = value.AsText;
                return true;
            case CellValueType.Number:
                text = NumberToText(value.AsNumber);
                return true;
            case CellValueType.Boolean:
                text = value.AsBoolean ? "TRUE" : "FALSE";
                return true;
            case CellValueType.Error:
                error = value.AsError;
                text = string.Empty;
                return false;
            default:
                error = CellError.Value();
                text = string.Empty;
                return false;
        }
    }

    /// <summary>Coerce to a logical value for AND/OR/IF. Numbers: non-zero → true; text "TRUE"/"FALSE".</summary>
    public static bool TryToBoolean(CellValue value, out bool result, out CellError? error)
    {
        error = null;
        switch (value.Type)
        {
            case CellValueType.Boolean:
                result = value.AsBoolean;
                return true;
            case CellValueType.Number:
                result = value.AsNumber != 0d;
                return true;
            case CellValueType.Empty:
                result = false;
                return true;
            case CellValueType.Text:
                if (string.Equals(value.AsText, "TRUE", StringComparison.OrdinalIgnoreCase))
                {
                    result = true;
                    return true;
                }

                if (string.Equals(value.AsText, "FALSE", StringComparison.OrdinalIgnoreCase))
                {
                    result = false;
                    return true;
                }

                error = CellError.Value($"'{value.AsText}' is not a logical value.");
                result = false;
                return false;
            case CellValueType.Error:
                error = value.AsError;
                result = false;
                return false;
            default:
                error = CellError.Value();
                result = false;
                return false;
        }
    }

    /// <summary>Render a number for text contexts without float noise or thousands separators.</summary>
    public static string NumberToText(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        // 15 significant digits matches spreadsheet display precision and trims trailing zeros.
        string text = value.ToString("G15", CultureInfo.InvariantCulture);
        return text;
    }
}
