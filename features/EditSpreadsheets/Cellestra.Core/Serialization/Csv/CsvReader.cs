using System.Globalization;
using System.Text;
using Cellestra.Core.Formulas.Evaluation;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;

namespace Cellestra.Core.Serialization.Csv;

/// <summary>
/// Reads CSV per PRD §8.4: UTF-8 with optional BOM stripped, LF/CRLF accepted, RFC 4180
/// quoting (doubled quotes inside quoted fields). Number and date cells are inferred using
/// the active locale; everything else stays text.
/// </summary>
public static class CsvReader
{
    /// <summary>Parse CSV text into rows of raw string fields.</summary>
    public static List<List<string>> Parse(string text, CsvOptions? options = null)
    {
        options ??= CsvOptions.Default;
        char delimiter = options.Delimiter;

        // Strip a UTF-8 BOM if it survived decoding.
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        bool fieldStarted = false;

        void EndField()
        {
            row.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
        }

        void EndRow()
        {
            EndField();
            rows.Add(row);
            row = new List<string>();
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when !fieldStarted || field.Length == 0:
                    inQuotes = true;
                    fieldStarted = true;
                    break;
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    EndRow();
                    break;
                case '\n':
                    EndRow();
                    break;
                default:
                    if (c == delimiter)
                    {
                        EndField();
                    }
                    else
                    {
                        field.Append(c);
                        fieldStarted = true;
                    }

                    break;
            }
        }

        // Flush the final field/row unless the text ended exactly on a newline.
        if (field.Length > 0 || row.Count > 0 || fieldStarted)
        {
            EndRow();
        }

        return rows;
    }

    /// <summary>Parse CSV and infer typed cell values into a new single-sheet workbook.</summary>
    public static Workbook ReadToWorkbook(string text, CsvOptions? options = null, CultureInfo? culture = null)
    {
        var workbook = Workbook.CreateNew();
        Fill(workbook, workbook.ActiveSheet, text, options, culture);
        return workbook;
    }

    /// <summary>Fill an existing worksheet from CSV, inferring number/date values.</summary>
    public static void Fill(Workbook workbook, Worksheet sheet, string text, CsvOptions? options = null, CultureInfo? culture = null)
    {
        culture ??= workbook.Settings.EffectiveLocale;
        var rows = Parse(text, options);
        int? dateStyleId = null;

        for (int r = 0; r < rows.Count; r++)
        {
            var cells = rows[r];
            for (int c = 0; c < cells.Count; c++)
            {
                string raw = cells[c];
                if (raw.Length == 0)
                {
                    continue;
                }

                if (double.TryParse(raw, NumberStyles.Any, culture, out double number) && double.IsFinite(number))
                {
                    sheet.SetValue(r, c, CellValue.Number(number));
                }
                else if (DateTime.TryParse(raw, culture, DateTimeStyles.None, out var date))
                {
                    dateStyleId ??= workbook.Styles.Intern(
                        CellStyle.Default.WithNumberFormat(new NumberFormat("yyyy-mm-dd")));
                    sheet.SetValue(r, c, CellValue.Number(DateConversion.ToSerial(date)));
                    sheet.GetOrCreate(r, c).StyleId = dateStyleId.Value;
                }
                else
                {
                    sheet.SetValue(r, c, CellValue.Text(raw));
                }
            }
        }
    }
}
