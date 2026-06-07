using System.Globalization;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Csv;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Serialization;

public class CsvTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void Writes_rows_with_lf_endings()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("a"));
        sheet.SetValue(0, 1, CellValue.Number(1));
        sheet.SetValue(1, 0, CellValue.Text("b"));
        sheet.SetValue(1, 1, CellValue.Number(2));

        Assert.Equal("a,1\nb,2\n", CsvWriter.Write(workbook, sheet));
    }

    [Fact]
    public void Quotes_fields_with_commas_quotes_and_newlines()
    {
        var workbook = Workbook.CreateNew();
        var sheet = workbook.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("a,b"));
        sheet.SetValue(0, 1, CellValue.Text("say \"hi\""));
        sheet.SetValue(0, 2, CellValue.Text("line1\nline2"));

        Assert.Equal("\"a,b\",\"say \"\"hi\"\"\",\"line1\nline2\"\n", CsvWriter.Write(workbook, sheet));
    }

    [Fact]
    public void Parses_quoted_fields_and_embedded_delimiters()
    {
        var rows = CsvReader.Parse("a,\"b,c\",\"d\"\"e\"\n1,2,3\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b,c", "d\"e" }, rows[0]);
        Assert.Equal(new[] { "1", "2", "3" }, rows[1]);
    }

    [Fact]
    public void Strips_bom_and_accepts_crlf()
    {
        var rows = CsvReader.Parse("﻿a,b\r\nc,d\r\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b" }, rows[0]);
    }

    [Fact]
    public void Infers_numbers_on_import()
    {
        var workbook = CsvReader.ReadToWorkbook("10,hello,3.5\n", culture: Invariant);
        var sheet = workbook.ActiveSheet;

        Assert.Equal(10, sheet.GetValue(0, 0).AsNumber);
        Assert.Equal("hello", sheet.GetValue(0, 1).AsText);
        Assert.Equal(3.5, sheet.GetValue(0, 2).AsNumber);
    }

    [Fact]
    public void Round_trips_text_and_numbers()
    {
        var workbook = CsvReader.ReadToWorkbook("name,score\nAda,99\nBev,87\n", culture: Invariant);
        string csv = CsvWriter.Write(workbook, workbook.ActiveSheet);

        Assert.Equal("name,score\nAda,99\nBev,87\n", csv);
    }
}
